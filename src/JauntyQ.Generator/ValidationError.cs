using Microsoft.CodeAnalysis;

namespace JauntyQ.Generator;

public class ValidationError
{
    public string Code { get; }
    public string Message { get; }
    public ValidationSeverity Severity { get; }
    public DiagnosticDescriptor? Descriptor { get; }

    public ValidationError(DiagnosticDescriptor descriptor, string message)
    {
        Descriptor = descriptor;
        Code = descriptor.Id;
        Message = message;
        Severity = descriptor.DefaultSeverity == DiagnosticSeverity.Error
            ? ValidationSeverity.Error
            : ValidationSeverity.Warning;
    }

    public override string ToString() => $"{Code}: {Message}";
}

public enum ValidationSeverity
{
    Error,
    Warning
}
