using System.Text.Json;

namespace Extrode.JauntyQ.Schema;

/// <summary>
/// Reads <c>jaunty.accept.json</c>. Spec 016. Kept beside
/// <see cref="SchemaLoader"/> and shaped identically, including the null-literal
/// guard: an acceptance file blanked to <c>null</c> would otherwise deserialize
/// to a null object without a <c>JsonException</c>, and a caller substituting an
/// empty file for it cannot tell "accepts nothing" from "this file is corrupt".
/// The two readings differ — the first is a consumer who has retired every
/// acceptance, the second is a file that needs looking at.
/// </summary>
public static class AcceptanceLoader
{
    public static AcceptanceFile Load(string json)
    {
        return JsonSerializer.Deserialize(json, SchemaJsonContext.Default.AcceptanceFile)
            ?? throw new JsonException("acceptance file JSON was the literal 'null', not an acceptance object");
    }
}
