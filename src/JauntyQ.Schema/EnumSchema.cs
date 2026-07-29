using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

/// <summary>
/// An enumerated type the database itself enforces, captured by 'jaunty schema
/// pull' so the generator can emit a real C# enum instead of an untyped
/// string. Two very different database constructs land here:
///
///   * PostgreSQL native enums (<c>CREATE TYPE ... AS ENUM</c>), read from
///     pg_type/pg_enum. These are named types shared by any number of columns,
///     so one entity serves them all.
///   * MySQL inline column enums (<c>status ENUM('a','b')</c>), parsed out of
///     information_schema.columns.COLUMN_TYPE. MySQL has no named enum type at
///     all, so the entity is named {Table}{Column} and two columns that happen
///     to declare identical member lists stay two distinct entities — merging
///     them would make the generated type name depend on emission order.
///
/// SQL Server and SQLite have no native enum, so this dictionary is empty for
/// them. MySQL <c>SET</c> is deliberately NOT captured: it is multi-valued and
/// keeps its existing string mapping (spec 013, FR-011).
/// </summary>
public class EnumSchema
{
    /// <summary>
    /// Snapshot key. The PostgreSQL type name as declared, or the synthesized
    /// {Table}{Column} name for a MySQL inline enum.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Members in declaration order. Order is meaningful and is compared as
    /// drift: PostgreSQL's enumsortorder defines <c>&lt;</c> and ORDER BY for
    /// the type, so a reorder changes query semantics, not just presentation.
    /// </summary>
    [JsonPropertyName("members")]
    public List<EnumMember> Members { get; set; } = new();
}

/// <summary>
/// One member of an <see cref="EnumSchema"/>. Carries both spellings because
/// they are not derivable from each other at every stage: <see cref="Value"/>
/// is what the database stores and what must go back on the wire verbatim,
/// while <see cref="CSharpName"/> is the identifier the generator emits.
/// Persisting the folded name rather than re-deriving it at emission time is
/// what stops the two from drifting apart across a snapshot round-trip.
/// </summary>
public class EnumMember
{
    /// <summary>The database's own value, exactly as stored. May contain
    /// commas, quotes, spaces, or be the empty string (MySQL permits
    /// <c>ENUM('')</c>).</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    /// <summary>The C# identifier emitted for this member.</summary>
    [JsonPropertyName("csharpName")]
    public string CSharpName { get; set; } = string.Empty;
}
