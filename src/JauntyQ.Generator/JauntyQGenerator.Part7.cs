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

    /// <summary>JNT9001/JNT9002 from parsing and simulating pending migrations.</summary>
    public ImmutableArray<DiagnosticInfo> MigrationDiagnostics { get; }

    private SchemaState(DatabaseSchema? schema, bool parseFailed, bool hasJson,
        ImmutableArray<DiagnosticInfo> migrationDiagnostics = default)
    {
        Schema = schema;
        ParseFailed = parseFailed;
        HasJson = hasJson;
        MigrationDiagnostics = migrationDiagnostics.IsDefault
            ? ImmutableArray<DiagnosticInfo>.Empty
            : migrationDiagnostics;
    }

    public static SchemaState Load(string? json, ImmutableArray<(string Name, string Text)> migrations)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SchemaState(null, parseFailed: false, hasJson: false);

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
        var ordered = new System.Collections.Generic.List<(string Name, string Text)>(migrations);
        ordered.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        var parsed = new System.Collections.Generic.List<(string FileName, System.Collections.Generic.List<Migrations.MigrationStatement> Statements)>();
        foreach (var migration in ordered)
            parsed.Add((migration.Name, Migrations.MigrationParser.Parse(migration.Text)));

        var errors = new System.Collections.Generic.List<ValidationError>();
        var effective = Migrations.SchemaSimulator.Apply(snapshot, parsed, errors);

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>(errors.Count);
        foreach (var error in errors)
            diagnostics.Add(DiagnosticInfo.From(error.Descriptor!, error.Message));

        return new SchemaState(effective, parseFailed: false, hasJson: true,
            diagnostics.MoveToImmutable());
    }
}
