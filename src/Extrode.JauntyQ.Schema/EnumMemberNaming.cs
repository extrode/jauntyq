namespace Extrode.JauntyQ.Schema;

/// <summary>
/// Folds a database enum member's wire value into the C# identifier the
/// generator emits for it (spec 013, FR-010).
///
/// This lives in Extrode.JauntyQ.Schema rather than beside
/// <c>DialectMapper.ToPascalCase</c> in Extrode.JauntyQ.Analysis because both sides of
/// the pipeline need it and only this assembly is visible to both: the
/// extractors in Extrode.JauntyQ.Schema.Extraction fold at capture time, and the
/// generator resolves the same names at emission time. One implementation, so
/// the snapshot and the emitted code cannot disagree about what a member is
/// called.
///
/// The rules match <c>ToPascalCase</c> deliberately — split on any run of
/// non-alphanumeric characters, keep only letters and digits, prefix a leading
/// digit — which also makes this a trust boundary: a member value is arbitrary
/// database content, and a hostile one such as
/// <c>"X { get; } static int Z = Evil(); //"</c> collapses to a harmless
/// identifier rather than injecting C#.
///
/// It differs in exactly one place, and that difference is the point:
/// <c>ToPascalCase</c> early-returns empty input unchanged, because an empty
/// table or column name cannot occur. An empty enum member can — MySQL permits
/// <c>ENUM('')</c>, confirmed live — so empty folds to <c>_</c> here rather
/// than to <c>""</c>, which would emit uncompilable C#.
/// </summary>
public static class EnumMemberNaming
{
    /// <summary>
    /// The identifier for <paramref name="value"/>. Always a legal bare C#
    /// identifier, and always the same identifier for the same input.
    /// </summary>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "_";

        var sb = new System.Text.StringBuilder(value!.Length);
        bool startOfWord = true;
        foreach (char c in value)
        {
            bool isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool isDigit = c >= '0' && c <= '9';
            if (isLetter)
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = false;
            }
            else if (isDigit)
            {
                sb.Append(c);
                startOfWord = false;
            }
            else
            {
                startOfWord = true;
            }
        }

        if (sb.Length == 0)
            return "_";
        if (sb[0] >= '0' && sb[0] <= '9')
            sb.Insert(0, '_');

        return sb.ToString();
    }
}
