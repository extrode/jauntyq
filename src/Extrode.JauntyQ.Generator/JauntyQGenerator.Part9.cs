using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;

namespace Extrode.JauntyQ.Generator;

/// <summary>
/// Result of processing one user .sql file: the generated source (if any),
/// its diagnostics, and the shape summary the aggregate pass consumes.
/// </summary>
internal sealed class FileResult
{
    public string? HintName { get; }
    public string? Source { get; }
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
    public FileSummary Summary { get; }

    /// <summary>Normalized-SQL fingerprint; null when the file emitted nothing.</summary>
    public string? Fingerprint { get; }

    /// <summary>
    /// The parsed query model, kept for the cross-query N+1 pass (JNT8008)
    /// at the aggregate stage. Null when the file did not parse/validate
    /// clean (no model to reason about) or binds a stored procedure.
    /// </summary>
    public QueryModel? Query { get; }

    /// <summary>
    /// The .sql file's path, used to anchor aggregate-stage diagnostics
    /// (JNT8005/JNT8008) to a navigable location instead of Location.None.
    /// Null when the file emitted nothing.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// The <c>-- @mirrors</c> target this file declared, verbatim (a bare
    /// <c>Method</c> or a qualified <c>Entity.Method</c>). Kept for the
    /// cross-query predicate-drift pass (JNT8011/JNT3010) at the aggregate
    /// stage, which is the only place the named query is visible. Null when the
    /// directive is absent.
    /// </summary>
    public string? MirrorsTarget { get; }

    public FileResult(string? hintName, string? source, ImmutableArray<DiagnosticInfo> diagnostics, FileSummary summary, string? fingerprint = null, QueryModel? query = null, string? path = null, string? mirrorsTarget = null)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
        Summary = summary;
        Fingerprint = fingerprint;
        Query = query;
        Path = path;
        MirrorsTarget = mirrorsTarget;
    }

    public static FileResult None(string entityName, string methodName, bool claims) =>
        new FileResult(null, null, ImmutableArray<DiagnosticInfo>.Empty,
            new FileSummary(entityName, methodName, claims, emitted: false, canonicalTable: null));

    public static FileResult WithDiagnostics(string entityName, string methodName, ImmutableArray<DiagnosticInfo> diagnostics) =>
        new FileResult(null, null, diagnostics,
            new FileSummary(entityName, methodName, claims: true, emitted: false, canonicalTable: null));
}
