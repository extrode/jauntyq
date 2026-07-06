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

    public FileResult(string? hintName, string? source, ImmutableArray<DiagnosticInfo> diagnostics, FileSummary summary, string? fingerprint = null)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
        Summary = summary;
        Fingerprint = fingerprint;
    }

    public static FileResult None(string entityName, string methodName, bool claims) =>
        new FileResult(null, null, ImmutableArray<DiagnosticInfo>.Empty,
            new FileSummary(entityName, methodName, claims, emitted: false, canonicalTable: null));

    public static FileResult WithDiagnostics(string entityName, string methodName, ImmutableArray<DiagnosticInfo> diagnostics) =>
        new FileResult(null, null, diagnostics,
            new FileSummary(entityName, methodName, claims: true, emitted: false, canonicalTable: null));
}
