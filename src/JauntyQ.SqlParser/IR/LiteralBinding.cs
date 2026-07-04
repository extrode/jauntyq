namespace JauntyQ.SqlParser.IR;

public enum LiteralKind
{
    String,
    Number
}

/// <summary>
/// A SQL literal that is compared to or assigned into a specific column
/// (WHERE col = 'x', SET col = 19.99, INSERT ... VALUES ('x'), col IN (1, 2)).
/// Captured so the validator can prove at compile time that the value fits
/// the column (max length, precision/scale, integer range).
/// </summary>
public class LiteralBinding
{
    public LiteralKind Kind { get; set; }

    /// <summary>Raw token text: string literals keep doubled '' escapes; numbers keep their digits.</summary>
    public string Value { get; set; } = string.Empty;

    public string BoundTableAlias { get; set; } = string.Empty;
    public string BoundColumnName { get; set; } = string.Empty;
}
