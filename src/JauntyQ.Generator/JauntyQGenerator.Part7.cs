using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;

/// <summary>
/// Parsed schema snapshot state. Rebuilt only when the snapshot json changes;
/// otherwise every pipeline node shares the same cached instance, so the
/// default reference equality is exactly right for incremental caching.
/// </summary>
internal sealed class SchemaState
{
    public DatabaseSchema? Schema { get; }
    public bool ParseFailed { get; }
    public bool HasJson { get; }

    /// <summary>
    /// JNT0001 text when <see cref="Load"/> itself threw, else null. Distinct from
    /// <see cref="ParseFailed"/>, which is the expected outcome of a malformed
    /// snapshot and already has JNT6001 to say so: this is an unexpected throw out
    /// of migration simulation or DDL parsing, and without it that throw escapes
    /// into the driver and takes the whole generator with it.
    /// </summary>
    public string? InternalError { get; }

    /// <summary>JNT9001/JNT9002 from parsing and simulating pending migrations.</summary>
    public ImmutableArray<DiagnosticInfo> MigrationDiagnostics { get; }

    /// <summary>
    /// Baseline→effective delta when pending migrations were applied, else null.
    /// Drives the build-time JNT9004 RISKY warning. Computed once here so every
    /// per-file node shares it (reference-cached with the rest of SchemaState).
    /// </summary>
    public SchemaDelta? MigrationDelta { get; }

    /// <summary>
    /// The state a throw out of <see cref="Load"/> degrades to: no schema, and the
    /// message that says why. ParseFailed is left false so JNT6001 does not also
    /// fire — one internal error reads better than an internal error plus a
    /// "run jaunty schema pull" that would not help.
    /// </summary>
    public static SchemaState Failed(string internalError) =>
        new SchemaState(null, parseFailed: false, hasJson: false, internalError: internalError);

    private SchemaState(DatabaseSchema? schema, bool parseFailed, bool hasJson,
        ImmutableArray<DiagnosticInfo> migrationDiagnostics = default,
        SchemaDelta? migrationDelta = null,
        string? internalError = null)
    {
        Schema = schema;
        ParseFailed = parseFailed;
        HasJson = hasJson;
        InternalError = internalError;
        MigrationDiagnostics = migrationDiagnostics.IsDefault
            ? ImmutableArray<DiagnosticInfo>.Empty
            : migrationDiagnostics;
        MigrationDelta = migrationDelta;
    }

    public static SchemaState Load(
        string? json,
        ImmutableArray<(string Name, string Text)> migrations,
        ImmutableArray<(string Name, string Text)> ddlFiles,
        string? dialectOverride)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            // A JSON snapshot is authoritative. db/ddl/*.sql files, if any, are
            // deliberately NOT consulted here (no merge, no diagnostic): DDL is
            // the ALTERNATIVE base-schema source used only when no snapshot has
            // been pulled, so the snapshot wins outright when it exists.
            DatabaseSchema snapshot;
            try
            {
                snapshot = SchemaLoader.Load(json!);
            }
            catch
            {
                return new SchemaState(null, parseFailed: true, hasJson: true);
            }

            if (migrations.IsDefaultOrEmpty)
                return new SchemaState(snapshot, parseFailed: false, hasJson: true);

            // Pending migrations are applied to a clone of the snapshot in
            // filename order; the generator validates and emits against the
            // post-migration world, so a breaking migration fails this build
            // instead of the deploy.
            var snapshotDiagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            var effectiveFromJson = ApplyMigrations(snapshot, migrations, snapshotDiagnostics);
            // SchemaSimulator.Apply works on a clone, so `snapshot` is still the
            // pre-migration baseline for the impact delta.
            var jsonDelta = StructuralSchemaDiff.Compute(snapshot, effectiveFromJson);
            return new SchemaState(effectiveFromJson, parseFailed: false, hasJson: true,
                snapshotDiagnostics.ToImmutable(), jsonDelta);
        }

        // No JSON snapshot. If db/ddl/*.sql files exist, they DEFINE the base
        // schema (DDL-as-schema-source mode); otherwise there is no schema.
        if (ddlFiles.IsDefaultOrEmpty)
            return new SchemaState(null, parseFailed: false, hasJson: false);

        // DDL mode needs an explicit dialect: with no snapshot there is nothing
        // to infer it from. An unknown/empty dialect is a hard error (JNT9003)
        // and yields Schema: null so the pipeline degrades exactly as the
        // no-schema case does — but hasJson: true so the aggregate step still
        // reports the diagnostic even when there are no query files.
        //
        // KNOWN LIMITATION: the MigrationParser does not populate
        // TableSchema.Indexes — inline UNIQUE column constraints and
        // standalone CREATE [UNIQUE] INDEX statements are dropped (classified
        // Ignored). So tables DEFINED in DDL/migration files have correct
        // PK/identity/column-type metadata but NO secondary unique-index
        // metadata: JNT8004 and the Upsert alternate-key resolution only ever
        // see the PK for such tables. Snapshot-sourced tables are unaffected —
        // SchemaSimulator.Clone carries Indexes, Procedures and Sequences
        // through migration simulation.
        if (string.IsNullOrEmpty(dialectOverride) || !DialectMapper.IsKnownDialect(dialectOverride!))
        {
            var diag = ImmutableArray.Create(DiagnosticInfo.From(JauntyDiagnostics.JNT9003,
                "db/ddl/*.sql schema source found but no valid dialect is set. With no JSON snapshot there is " +
                "nothing to infer the dialect from; declare it in the consuming project's .csproj, e.g. " +
                "<JauntyQDialect>sqlserver</JauntyQDialect> (or postgres, mysql, sqlite)."));
            return new SchemaState(null, parseFailed: false, hasJson: true, diag);
        }

        // Build the base schema by simulating the ddl files onto an empty
        // database of the declared dialect, then apply any db/migrations/*.sql
        // on top of that base exactly as they apply on top of a JSON snapshot.
        var baseSchema = new DatabaseSchema { Dialect = dialectOverride! };
        var ddlDiagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var ddlBuilt = ApplyMigrations(baseSchema, ddlFiles, ddlDiagnostics);
        var effective = migrations.IsDefaultOrEmpty
            ? ddlBuilt
            : ApplyMigrations(ddlBuilt, migrations, ddlDiagnostics);

        // Impact delta only when migrations were applied on top of the DDL base.
        var ddlDelta = migrations.IsDefaultOrEmpty
            ? null
            : StructuralSchemaDiff.Compute(ddlBuilt, effective);

        return new SchemaState(effective, parseFailed: false, hasJson: true, ddlDiagnostics.ToImmutable(), ddlDelta);
    }

    /// <summary>
    /// Parses each file (filename-sorted for determinism) and simulates its
    /// statements onto a clone of <paramref name="baseSchema"/>, appending any
    /// JNT9001/JNT9002 findings to <paramref name="diagnostics"/>. Shared by
    /// the JSON-snapshot path (migrations on a pulled snapshot) and the DDL path
    /// (ddl files on an empty schema, then migrations on the ddl-built schema).
    /// </summary>
    private static DatabaseSchema ApplyMigrations(
        DatabaseSchema baseSchema,
        ImmutableArray<(string Name, string Text)> files,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var ordered = new System.Collections.Generic.List<(string Name, string Text)>(files);
        ordered.Sort(static (a, b) => NaturalCompare(a.Name, b.Name));

        var parsed = new System.Collections.Generic.List<(string FileName, System.Collections.Generic.List<MigrationStatement> Statements)>();
        foreach (var file in ordered)
            parsed.Add((file.Name, MigrationParser.Parse(file.Text)));

        var diags = new System.Collections.Generic.List<AnalysisDiagnostic>();
        var effective = SchemaSimulator.Apply(baseSchema, parsed, diags);

        foreach (var d in diags)
            diagnostics.Add(DiagnosticInfo.From(MigrationDescriptor(d.Code), d.Message));

        return effective;
    }

    /// <summary>
    /// Natural-order string comparison: runs of digits compare by numeric
    /// value, everything else compares ordinally. A plain ordinal sort (the
    /// old behavior) misorders common unpadded migration-file naming
    /// conventions -- Flyway-style "V1__init.sql", "V2__add_col.sql", ...,
    /// "V10__rename.sql" sorts ordinally as V1, V10, V2, V3, ..., V9, applying
    /// V10 to the simulator before V2-V9 ran. Zero-padded ("001_", "002_")
    /// and fixed-width date-prefixed ("20230101_") names are unaffected --
    /// digit runs of equal length compare identically either way.
    /// </summary>
    private static int NaturalCompare(string a, string b)
    {
        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                int sa = ia, sb = ib;
                while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                string da = a.Substring(sa, ia - sa).TrimStart('0');
                string db = b.Substring(sb, ib - sb).TrimStart('0');
                if (da.Length != db.Length)
                    return da.Length - db.Length;
                int numCmp = string.CompareOrdinal(da, db);
                if (numCmp != 0)
                    return numCmp;
                // Numerically equal (e.g. "007" vs "07"): fall back to raw
                // (padded) run length so the comparison stays deterministic.
                int padCmp = (ia - sa) - (ib - sb);
                if (padCmp != 0)
                    return padCmp;
            }
            else
            {
                if (a[ia] != b[ib])
                    return a[ia] - b[ib];
                ia++;
                ib++;
            }
        }
        return (a.Length - ia) - (b.Length - ib);
    }

    /// <summary>
    /// Maps a Roslyn-free <see cref="AnalysisDiagnostic"/> code from the shared
    /// migration simulator back to its Roslyn <c>DiagnosticDescriptor</c>. The
    /// simulator only emits JNT9001 (unmodeled statement) and JNT9002 (invalid
    /// operation); anything else falls back to JNT9002.
    /// </summary>
    private static DiagnosticDescriptor MigrationDescriptor(string code) => code switch
    {
        "JNT9001" => JauntyDiagnostics.JNT9001,
        "JNT9002" => JauntyDiagnostics.JNT9002,
        _ => JauntyDiagnostics.JNT9002,
    };
}
