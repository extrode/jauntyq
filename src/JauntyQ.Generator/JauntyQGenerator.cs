using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;

[Generator(LanguageNames.CSharp)]
public partial class JauntyQGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Shared one-time shape guard (exists exactly once per compilation)
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("JauntyQShapeGuard.g.cs", SourceText.From(CodeEmitter.EmitShapeGuardSource(), Encoding.UTF8)));

        // Collect SQL query files (migrations and ddl are separate pipelines)
        var sqlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                && !IsMigrationPath(f.Path) && !IsDdlPath(f.Path));

        // Migration files: db/migrations/NNNN_name.sql, applied to the
        // snapshot in filename order to form the effective schema.
        var migrationFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && IsMigrationPath(f.Path))
            .Select(static (f, ct) => (Name: System.IO.Path.GetFileName(f.Path), Text: f.GetText(ct)?.ToString() ?? ""))
            .Collect();

        // DDL files: db/ddl/*.sql, CREATE TABLE / ALTER TABLE statements that
        // define the base schema when no JSON snapshot is pulled. Consulted
        // only in that case; a JSON snapshot always wins when present.
        var ddlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && IsDdlPath(f.Path))
            .Select(static (f, ct) => (Name: System.IO.Path.GetFileName(f.Path), Text: f.GetText(ct)?.ToString() ?? ""))
            .Collect();

        // Collect schema files. More than one *.schema.json is almost always a
        // mistake (a stray copy, a rename leftover): the ordinal-lowest path is
        // the deterministic winner — AdditionalTexts order is not guaranteed —
        // and JNT6002 names every candidate so the surprise is visible.
        var schemaFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase));

        var schemaCandidates = schemaFiles
            .Select(static (f, _) => f.Path)
            .Collect()
            .Select(static (paths, _) =>
            {
                var sorted = paths.Sort(StringComparer.Ordinal);
                return sorted;
            })
            .WithTrackingName("JauntyQ_SchemaCandidates");

        context.RegisterSourceOutput(schemaCandidates, static (ctx, paths) =>
        {
            if (paths.Length < 2)
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT6002, Location.None,
                $"Found {paths.Length} schema snapshots ({string.Join(", ", paths)}); using '{paths[0]}'. " +
                "Remove the extra *.schema.json files (or exclude them from AdditionalFiles) so the schema source is unambiguous."));
        });

        // Get the winning schema file's text (ordinal-lowest path).
        var schemaText = schemaFiles.Collect().Select(static (files, ct) =>
        {
            if (files.IsEmpty)
                return null;
            AdditionalText winner = files[0];
            for (int i = 1; i < files.Length; i++)
            {
                if (StringComparer.Ordinal.Compare(files[i].Path, winner.Path) < 0)
                    winner = files[i];
            }
            return winner.GetText(ct)?.ToString();
        });

        // Dialect override for DDL-as-schema-source mode: with no JSON snapshot
        // there is nothing to infer the dialect from, so the consumer declares
        // it via <JauntyQDialect>sqlserver</JauntyQDialect> (or postgres/mysql/
        // sqlite). Absent/empty when not set; only consulted in DDL mode.
        var dialectOverride = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.JauntyQDialect", out var value) ? value : "");

        // Parse the snapshot once and apply pending migrations to produce
        // the EFFECTIVE schema that validation and emission run against.
        // Cached until the snapshot json or any migration/ddl text changes.
        // When there is no JSON snapshot, db/ddl/*.sql builds the base schema
        // (dialect from JauntyQDialect); JSON always wins when present.
        var schemaState = schemaText
            .Combine(migrationFiles)
            .Combine(ddlFiles)
            .Combine(dialectOverride)
            .Select(static (pair, _) =>
            {
                var (((json, migrations), ddl), dialect) = pair;
                return SchemaState.Load(json, migrations, ddl, dialect);
            });

        // Auto-CRUD is on unless the consumer sets <JauntyQAutoCrud>false</JauntyQAutoCrud>
        var autoCrudEnabled = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            !(provider.GlobalOptions.TryGetValue("build_property.JauntyQAutoCrud", out var value)
              && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)));

        // Path-set projection: content edits leave every path string equal, so
        // the common prefix (and everything keyed on it) stays cached.
        var commonPrefix = sqlFiles
            .Select(static (f, _) => f.Path)
            .Collect()
            .Select(static (paths, _) => ComputeCommonDirectoryPrefix(paths))
            .WithTrackingName("JauntyQ_CommonPrefix");

        // Per-file pipeline: editing one .sql re-parses and re-emits only that file.
        var perFile = sqlFiles
            .Combine(commonPrefix)
            .Combine(schemaState)
            .Select(static (pair, ct) =>
            {
                var ((sqlFile, prefix), schema) = pair;
                return ProcessFile(sqlFile, prefix, schema, ct);
            })
            .WithTrackingName("JauntyQ_PerFile");

        context.RegisterSourceOutput(perFile, static (ctx, result) =>
        {
            foreach (var diag in result.Diagnostics)
                ctx.ReportDiagnostic(diag.ToDiagnostic());
            if (result.HintName != null && result.Source != null)
                ctx.AddSource(result.HintName, SourceText.From(result.Source, Encoding.UTF8));
        });

        // Duplicate-query detection (JNT8005): plain string comparison in its
        // own node, so body edits never invalidate the expensive aggregate.
        var fingerprints = perFile
            .Select(static (r, _) => (Entity: r.Summary.EntityName, Method: r.Summary.MethodName, Fingerprint: r.Fingerprint))
            .Collect()
            .WithTrackingName("JauntyQ_Fingerprints");

        context.RegisterSourceOutput(fingerprints, static (ctx, entries) =>
        {
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Fingerprint))
                    continue;
                if (!groups.TryGetValue(entry.Fingerprint!, out var members))
                {
                    members = new System.Collections.Generic.List<string>();
                    groups[entry.Fingerprint!] = members;
                }
                members.Add($"{entry.Entity}.{entry.Method}");
            }
            foreach (var group in groups)
            {
                if (group.Value.Count < 2)
                    continue;
                group.Value.Sort(StringComparer.Ordinal);
                ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT8005, Location.None,
                    $"Queries {string.Join(", ", group.Value)} compile to identical SQL; consolidate them to keep one plan and one maintenance point."));
            }
        });

        // N+1 heuristic (JNT8008): cross-query, so it runs here at the
        // aggregate stage like JNT8005 — it pairs a child point-lookup by a
        // full foreign key with a parent-collection query over the FK's
        // parent table, which needs the whole corpus plus the FK graph.
        var nPlusOneInput = perFile
            .Select(static (r, _) => (Name: r.Summary.EntityName + "." + r.Summary.MethodName, Query: r.Query))
            .Collect()
            .Combine(schemaState)
            .WithTrackingName("JauntyQ_NPlusOne");

        context.RegisterSourceOutput(nPlusOneInput, static (ctx, pair) =>
        {
            var (entries, schema) = pair;
            if (schema.Schema == null)
                return;
            foreach (var diag in NPlusOneAnalyzer.Analyze(entries, schema.Schema))
                ctx.ReportDiagnostic(diag);
        });

        // Aggregated outputs (synthetics, POCO overloads, row POCOs, entity
        // cores, JauntyDb) depend only on each file's value-equatable shape
        // summary — body edits that keep the shape leave all of it cached.
        var summaries = perFile
            .Select(static (r, _) => r.Summary)
            .Collect()
            .WithComparer(FileSummaryArrayComparer.Instance)
            .WithTrackingName("JauntyQ_Summaries");

        var aggregateInput = summaries
            .Combine(schemaState)
            .Combine(autoCrudEnabled)
            .WithTrackingName("JauntyQ_AggregateInput");

        context.RegisterSourceOutput(aggregateInput, static (ctx, pair) =>
        {
            var ((fileSummaries, schema), autoCrud) = pair;
            EmitAggregates(ctx, fileSummaries, schema, autoCrud);
        });
    }

}
