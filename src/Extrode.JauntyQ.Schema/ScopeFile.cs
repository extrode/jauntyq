using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Extrode.JauntyQ.Schema;

/// <summary>
/// The deserialized shape of <c>jaunty.scope.json</c>: the tables a consumer
/// declares scoped by a column (a tenant id, an owner id). Spec 021.
///
/// A separate file from the schema snapshot for the reason
/// <see cref="AcceptanceFile"/> is: <c>jauntyq schema pull</c> rewrites the
/// snapshot wholesale, and a declaration stored inside it would be erased by
/// the next pull. It also works in DDL-as-schema-source mode, where there is
/// no snapshot at all.
///
/// An unknown top-level key is a read error rather than ignored: a misspelt
/// <c>"scope"</c> would otherwise read as a file that scopes nothing.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class ScopeFile
{
    /// <summary>
    /// One entry per (table, column). A table may appear more than once, once
    /// per scope column, and each is enforced on its own. Empty or absent
    /// scopes nothing, and is reported.
    /// </summary>
    [JsonPropertyName("scopes")]
    public List<ScopeEntry> Scopes { get; set; } = new();
}

/// <summary>
/// One scoped table. Both fields are mandatory; an entry missing either is
/// reported as JNT6004 and dropped, which leaves the table unscoped, rather
/// than being read with a default.
/// </summary>
public class ScopeEntry
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("column")]
    public string? Column { get; set; }
}
