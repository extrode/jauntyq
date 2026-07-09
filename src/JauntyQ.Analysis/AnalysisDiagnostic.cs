namespace JauntyQ.Analysis;

/// <summary>
/// A Roslyn-free diagnostic produced by the shared analysis primitives
/// (migration parsing, schema simulation, auto-CRUD synthesis, impact
/// classification). The Roslyn generator maps <see cref="Code"/> back to its
/// <c>DiagnosticDescriptor</c> at its boundary; the CLI renders these directly.
/// This keeps <c>JauntyQ.Analysis</c> free of a <c>Microsoft.CodeAnalysis</c>
/// dependency so it can be referenced by the net8.0 CLI.
/// </summary>
public readonly struct AnalysisDiagnostic
{
    /// <summary>The JauntyQ diagnostic id, e.g. <c>"JNT9002"</c>.</summary>
    public string Code { get; }

    /// <summary>The fully-formatted, human-readable message.</summary>
    public string Message { get; }

    /// <summary>Error or warning; drives fatality when adapted to Roslyn.</summary>
    public AnalysisSeverity Severity { get; }

    public AnalysisDiagnostic(string code, string message, AnalysisSeverity severity)
    {
        Code = code;
        Message = message;
        Severity = severity;
    }

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Severity of an <see cref="AnalysisDiagnostic"/>.</summary>
public enum AnalysisSeverity
{
    Error,
    Warning
}
