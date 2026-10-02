using System.Text.Json;

namespace Extrode.JauntyQ.Schema;

/// <summary>
/// Reads <c>jaunty.scope.json</c>. Spec 021. Shaped like
/// <see cref="AcceptanceLoader"/>, including the guard against a file blanked
/// to the literal <c>null</c>: read as "scopes nothing", it would switch every
/// tenant check off without a word.
/// </summary>
public static class ScopeLoader
{
    public static ScopeFile Load(string json)
    {
        return JsonSerializer.Deserialize(json, SchemaJsonContext.Default.ScopeFile)
            ?? throw new JsonException("scope file JSON was the literal 'null', not a scope object");
    }
}
