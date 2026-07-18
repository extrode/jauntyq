using System.Collections.Generic;

namespace JauntyQ.Generator;

/// <summary>
/// Central trust boundary for every name that flows from a .sql file or a
/// schema snapshot into emitted C#. The generator turns build inputs into
/// compiled code, so an identifier or string literal that is not validated
/// or escaped is a build-time code-injection vector (a hostile column alias
/// such as <c>[X { get; } static int Z = Evil(); //]</c> would otherwise be
/// emitted verbatim as a member name and executed by the compiler / IDE).
///
/// Two guarantees:
///   * <see cref="IsValidIdentifier"/> rejects anything that is not a bare
///     C# identifier, so callers can raise a diagnostic instead of emitting.
///   * <see cref="Escape"/> makes a validated identifier safe to emit even
///     when it collides with a C# keyword (prefixing '@').
///   * <see cref="ToStringLiteral"/> encodes an arbitrary string for safe
///     embedding inside a regular "..." C# literal.
/// </summary>
public static class IdentifierGuard
{
    // C# reserved keywords that must be '@'-escaped when used as identifiers.
    // Contextual keywords (e.g. 'value', 'var') are legal identifiers and omitted.
    private static readonly HashSet<string> Keywords = new(System.StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// True when <paramref name="name"/> is a bare C# identifier
    /// (<c>[A-Za-z_][A-Za-z0-9_]*</c>). Rejects empty, whitespace, and any
    /// name containing C#-significant characters (<c>{ } ; " = ( )</c>, etc.).
    /// </summary>
    public static bool IsValidIdentifier(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        char first = name![0];
        if (!IsAsciiLetter(first) && first != '_')
            return false;

        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Emission-safe form of a validated identifier. Assumes the caller has
    /// already gated on <see cref="IsValidIdentifier"/>; prefixes '@' when the
    /// name is a C# keyword so it can be used as a member/parameter name.
    /// </summary>
    public static string Escape(string name)
        => Keywords.Contains(name) ? "@" + name : name;

    /// <summary>
    /// True when <paramref name="name"/> is a reserved C# keyword. A name taken
    /// from a file/folder path becomes a class or method identifier that is
    /// emitted verbatim (unlike parameters and column aliases, which go through
    /// <see cref="Escape"/>), so a keyword there produces uncompilable code —
    /// the caller rejects it with a diagnostic instead.
    /// </summary>
    public static bool IsReservedKeyword(string? name)
        => name != null && Keywords.Contains(name);

    /// <summary>
    /// Encodes an arbitrary string as the body of a regular C# "..." literal
    /// (not verbatim). Escapes backslash, quote, and control characters so the
    /// value cannot break out of the literal.
    ///
    /// AUD-R62-01: the C# lexical grammar's <c>New_Line_Character</c> set is
    /// five code points -- U+000D, U+000A, U+0085 (NEL), U+2028 (LINE
    /// SEPARATOR), and U+2029 (PARAGRAPH SEPARATOR) -- every one of which is
    /// illegal unescaped inside a regular string literal. The first two are
    /// below 0x20 and were already covered by the control-character fallback
    /// below, but the latter three are ordinary printable code points (>=
    /// 0x20) that used to fall through unescaped, letting a bracket-quoted
    /// SQL alias containing one of them break the emitted `__...Columns`
    /// literal at build time with a raw, unattributed CS1010/CS1002 cascade.
    /// </summary>
    public static string ToStringLiteral(string? value)
    {
        if (value == null)
            return "";

        var sb = new System.Text.StringBuilder(value.Length + 2);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (c < 0x20 || c == '\u0085' || c == '\u2028' || c == '\u2029')
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
