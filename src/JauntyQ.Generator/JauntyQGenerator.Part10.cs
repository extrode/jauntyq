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
/// Cache-friendly diagnostic carrier: plain strings compared by value, so a
/// re-run that produces the same diagnostics counts as unchanged. Rehydrated
/// into a Roslyn Diagnostic only at report time.
/// </summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    private readonly string _id;
    private readonly string _title;
    private readonly string _messageFormat;
    private readonly string _category;
    private readonly DiagnosticSeverity _severity;
    private readonly string? _arg;

    private DiagnosticInfo(string id, string title, string messageFormat, string category, DiagnosticSeverity severity, string? arg)
    {
        _id = id;
        _title = title;
        _messageFormat = messageFormat;
        _category = category;
        _severity = severity;
        _arg = arg;
    }

    public static DiagnosticInfo From(DiagnosticDescriptor descriptor, string arg) =>
        new DiagnosticInfo(descriptor.Id, descriptor.Title.ToString(), descriptor.MessageFormat.ToString(),
            descriptor.Category, descriptor.DefaultSeverity, arg);

    /// <summary>
    /// Title and category come from the real descriptor when the error carries
    /// one, which every JauntyDiagnostics-constructed ValidationError does
    /// (ValidationError.cs:12-16 sets Descriptor and derives Code from it).
    ///
    /// This used to hard-code category "JauntyQ" and use the code string as the
    /// title, discarding the descriptor the error was already holding. That
    /// mattered once a second report site started using the descriptor
    /// directly: the same ID could reach the compiler under two categories and
    /// two titles depending on which path raised it, so severity-by-category
    /// (dotnet_analyzer_diagnostic_category-*) applied to some instances of an
    /// ID and not others, and SARIF carried two rule entries for one rule. ID-
    /// keyed configuration -- NoWarn, dotnet_diagnostic.JNTxxxx.severity -- was
    /// never affected either way.
    ///
    /// Severity still comes from the ValidationError, not the descriptor: a
    /// validator may report an otherwise-Error diagnostic as a Warning for a
    /// particular shape, and that decision outranks the default.
    ///
    /// messageFormat stays the raw message with arg null, so ToDiagnostic's
    /// no-argument Create returns it unformatted and a brace in a column name
    /// cannot throw.
    /// </summary>
    public static DiagnosticInfo ForValidation(ValidationError error) =>
        new DiagnosticInfo(
            error.Code,
            error.Descriptor?.Title.ToString() ?? error.Code,
            error.Message,
            error.Descriptor?.Category ?? "JauntyQ",
            error.Severity == ValidationSeverity.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            arg: null);

    public Diagnostic ToDiagnostic()
    {
        var descriptor = new DiagnosticDescriptor(_id, _title, _messageFormat, _category, _severity, isEnabledByDefault: true);
        return _arg != null
            ? Diagnostic.Create(descriptor, Location.None, _arg)
            : Diagnostic.Create(descriptor, Location.None);
    }

    public bool Equals(DiagnosticInfo? other) =>
        other != null
        && _severity == other._severity
        && string.Equals(_id, other._id, StringComparison.Ordinal)
        && string.Equals(_title, other._title, StringComparison.Ordinal)
        && string.Equals(_messageFormat, other._messageFormat, StringComparison.Ordinal)
        && string.Equals(_category, other._category, StringComparison.Ordinal)
        && string.Equals(_arg, other._arg, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = _id.GetHashCode();
            hash = hash * 31 + _messageFormat.GetHashCode();
            hash = hash * 31 + (_arg?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
