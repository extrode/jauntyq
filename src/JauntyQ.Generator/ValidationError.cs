namespace JauntyQ.Generator;

public class ValidationError
{
    public string Code { get; }
    public string Message { get; }
    public ValidationSeverity Severity { get; }

    public ValidationError(string code, string message, ValidationSeverity severity = ValidationSeverity.Error)
    {
        Code = code;
        Message = message;
        Severity = severity;
    }

    public override string ToString() => $"{Code}: {Message}";
}

public enum ValidationSeverity
{
    Error,
    Warning
}
