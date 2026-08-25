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

        // Case-insensitive hint-name collisions (JNT2008): Roslyn's AddSource
        // treats hint names case-insensitively (they map to file paths), so two
        // files whose {entity}.{method} names differ only by case — reachable on
        // a case-sensitive filesystem such as a Linux CI runner — would make the
        // second AddSource throw and abort the ENTIRE generator with an opaque
        // CS8785, erasing every generated type. Detect the collision across the
        // whole file set (cached until a file's hint name changes, i.e. an
        // add/rename/delete — not a body edit) so the emit step below can report
        // it and skip the colliding files instead of crashing.
        var hintCollisions = perFile
            .Select(static (r, _) => (Hint: r.HintName, Path: r.Path))
            .Collect()
            .Select(static (entries, _) => ComputeHintCollisions(entries))
            .WithTrackingName("JauntyQ_HintCollisions");

        context.RegisterSourceOutput(perFile.Combine(hintCollisions), static (ctx, pair) =>
        {
            var (result, collisions) = pair;
            foreach (var diag in result.Diagnostics)
                ctx.ReportDiagnostic(diag.ToDiagnostic());
            if (result.HintName == null || result.Source == null)
                return;

            if (result.Path != null)
            {
                foreach (var collision in collisions)
                {
                    if (string.Equals(collision.Path, result.Path, StringComparison.Ordinal))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2008,
                            FileLocation(result.Path), collision.Message));
                        return; // skip emission so AddSource never collides/throws
                    }
                }
            }

            ctx.AddSource(result.HintName, SourceText.From(result.Source, Encoding.UTF8));
        });

        // Duplicate-query detection (JNT8005): plain string comparison in its
        // own node, so body edits never invalidate the expensive aggregate.
        var fingerprints = perFile
            .Select(static (r, _) => (Entity: r.Summary.EntityName, Method: r.Summary.MethodName, Fingerprint: r.Fingerprint, Path: r.Path))
            .Collect()
            .WithTrackingName("JauntyQ_Fingerprints");

        context.RegisterSourceOutput(fingerprints, static (ctx, entries) =>
        {
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string Name, string? Path)>>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Fingerprint))
                    continue;
                if (!groups.TryGetValue(entry.Fingerprint!, out var members))
                {
                    members = new System.Collections.Generic.List<(string, string?)>();
                    groups[entry.Fingerprint!] = members;
                }
                members.Add(($"{entry.Entity}.{entry.Method}", entry.Path));
            }
            foreach (var group in groups)
            {
                if (group.Value.Count < 2)
                    continue;
                group.Value.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                var memberNames = new System.Collections.Generic.List<string>(group.Value.Count);
                foreach (var m in group.Value)
                    memberNames.Add(m.Name);
                // Anchor to the first member's .sql file so the IDE can navigate.
                ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT8005, FileLocation(group.Value[0].Path),
                    $"Queries {string.Join(", ", memberNames)} compile to identical SQL; consolidate them to keep one plan and one maintenance point."));
            }
        });

        // N+1 heuristic (JNT8008): cross-query, so it runs here at the
        // aggregate stage like JNT8005 — it pairs a child point-lookup by a
        // full foreign key with a parent-collection query over the FK's
        // parent table, which needs the whole corpus plus the FK graph.
        var nPlusOneInput = perFile
            .Select(static (r, _) => (Name: r.Summary.EntityName + "." + r.Summary.MethodName, Path: r.Path, Query: r.Query))
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

        // Predicate drift (JNT8011) and un-comparable pairings (JNT3010): also
        // cross-query, so it sits here beside the N+1 pass. Its own node rather
        // than a wider nPlusOneInput tuple, so adding or removing a -- @mirrors
        // directive does not invalidate the N+1 aggregate and vice versa.
        var predicateDriftInput = perFile
            .Select(static (r, _) => new PredicateDriftAnalyzer.Entry(
                r.Summary.EntityName + "." + r.Summary.MethodName, r.Path, r.Query, r.MirrorsTarget))
            .Collect()
            .Combine(schemaState)
            .WithTrackingName("JauntyQ_PredicateDrift");

        context.RegisterSourceOutput(predicateDriftInput, static (ctx, pair) =>
        {
            var (entries, schema) = pair;
            if (schema.Schema == null)
                return;
            foreach (var diag in PredicateDriftAnalyzer.Analyze(entries, schema.Schema))
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

        // commonPrefix rides along so a JNT8004 on a SYNTHETIC query can name
        // the .sql file that would claim its slot -- the only route by which a
        // consumer can accept an auto-CRUD scan.
        //
        // On the common path this is free: a body edit leaves every path string
        // equal, so the prefix is unchanged and the aggregate stays cached. It
        // is NOT free in general, and the difference is worth stating rather
        // than glossing. FileSummary (Part8.cs:17-38) carries entity, method,
        // claims, emitted and canonical table -- no path. So renaming the SQL
        // root (db/ -> queries/, or relocating the project) changes every path
        // and hence the prefix, while the summaries array stays value-equal
        // under FileSummaryArrayComparer: before this Combine the aggregate
        // stayed cached across such a rename, and now it re-runs.
        //
        // That re-run is required, not a regression. A cached aggregate would
        // keep printing the pre-rename path in the hint, which is the one thing
        // the diagnostic must not do -- its whole value is that the path it
        // names is the path that works.
        // Spec 016: jaunty.accept.json, the per-column record that a generated
        // query's unindexed scan is deliberate. Found by suffix like the schema
        // snapshot above, and resolved the same way -- ordinal-lowest path wins,
        // deterministically, because AdditionalTexts order is not guaranteed.
        //
        // Path AND text travel together in one tuple: every diagnostic this file
        // produces names it, and an acceptance reported against the wrong path
        // sends the consumer to edit a file that is not the one in force.
        // ValueTuple of two strings is value-equatable, so a build that does not
        // touch the file leaves the whole aggregate cached.
        var acceptFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".accept.json", StringComparison.OrdinalIgnoreCase));

        var acceptance = acceptFiles.Collect().Select(static (files, ct) =>
        {
            if (files.IsEmpty)
                return ((string?)null, (string?)null);
            AdditionalText winner = files[0];
            for (int i = 1; i < files.Length; i++)
            {
                if (StringComparer.Ordinal.Compare(files[i].Path, winner.Path) < 0)
                    winner = files[i];
            }
            return ((string?)winner.Path, (string?)(winner.GetText(ct)?.ToString()));
        });

        // The candidate list for the more-than-one case, in its own node so that
        // editing the winning file's CONTENT does not re-run this report. Same
        // shape as JNT6002's above, and reported separately from the aggregate
        // for the same reason: it is a fact about the file set, not about the
        // schema or the queries.
        var acceptCandidates = acceptFiles
            .Select(static (f, _) => f.Path)
            .Collect()
            .Select(static (paths, _) => paths.Sort(StringComparer.Ordinal))
            .WithTrackingName("JauntyQ_AcceptCandidates");

        context.RegisterSourceOutput(acceptCandidates, static (ctx, paths) =>
        {
            if (paths.Length < 2)
                return;
            ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT6003, Location.None,
                $"Found {paths.Length} acceptance files ({string.Join(", ", paths)}); using '{paths[0]}'. " +
                "Acceptances in the others are ignored entirely -- they are not merged. Keep one " +
                "*.accept.json (or exclude the extras from AdditionalFiles)."));
        });

        var aggregateInput = summaries
            .Combine(schemaState)
            .Combine(autoCrudEnabled)
            .Combine(commonPrefix)
            .Combine(acceptance)
            .WithTrackingName("JauntyQ_AggregateInput");

        context.RegisterSourceOutput(aggregateInput, static (ctx, pair) =>
        {
            var ((((fileSummaries, schema), autoCrud), prefix), accept) = pair;
            EmitAggregates(ctx, fileSummaries, schema, autoCrud, prefix, accept.Item1, accept.Item2);
        });
    }

    /// <summary>
    /// Groups the emitted files by hint name, case-insensitively (matching how
    /// Roslyn's AddSource compares them), and returns one (path, message) entry
    /// for every file that shares its generated name with at least one other —
    /// the empty array when all names are unique. The result is value-equatable
    /// (ImmutableArray of tuples), so it stays cached across body edits that
    /// leave every hint name unchanged.
    /// </summary>
    private static ImmutableArray<(string Path, string Message)> ComputeHintCollisions(
        ImmutableArray<(string? Hint, string? Path)> entries)
    {
        var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(string Hint, string Path)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Hint) || string.IsNullOrEmpty(e.Path))
                continue;
            if (!groups.TryGetValue(e.Hint!, out var list))
                groups[e.Hint!] = list = new System.Collections.Generic.List<(string, string)>();
            list.Add((e.Hint!, e.Path!));
        }

        var builder = ImmutableArray.CreateBuilder<(string, string)>();
        foreach (var list in groups.Values)
        {
            if (list.Count < 2)
                continue;
            list.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
            var listPaths = new System.Collections.Generic.List<string>(list.Count);
            foreach (var m in list)
                listPaths.Add(m.Path);
            string paths = string.Join(", ", listPaths);
            string message =
                $"SQL files [{paths}] all generate the file '{list[0].Hint}'. Generated file names are compared case-insensitively, so these collide and would abort code generation. Rename one so the generated names differ by more than case.";
            foreach (var m in list)
                builder.Add((m.Path, message));
        }
        builder.Sort(static (a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return builder.ToImmutable();
    }

    /// <summary>
    /// A file-level Location (line 1) for aggregate-stage diagnostics, so the
    /// IDE can navigate to the offending .sql file; Location.None when the
    /// path is unknown.
    /// </summary>
    internal static Location FileLocation(string? path) =>
        string.IsNullOrEmpty(path)
            ? Location.None
            : Location.Create(path!, new TextSpan(0, 0), new LinePositionSpan());

}
