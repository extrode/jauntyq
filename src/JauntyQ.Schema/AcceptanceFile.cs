using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JauntyQ.Schema;

/// <summary>
/// The deserialized shape of <c>jaunty.accept.json</c> — a sidecar in which a
/// consumer records that a generated query's unindexed scan is deliberate.
///
/// Spec 016. It is a SEPARATE file from the schema snapshot on purpose: the
/// snapshot is rewritten wholesale by <c>jaunty schema pull</c>, so an
/// acceptance stored inside it would be erased by the next pull — silently, and
/// at exactly the moment the consumer is least likely to notice, since a pull is
/// also when a new index might have arrived.
/// </summary>
public class AcceptanceFile
{
    /// <summary>
    /// Per-column acceptances of an unindexed auto-CRUD filter (JNT8004).
    /// Empty or absent means the file accepts nothing, which is valid — a file
    /// emptied down to zero entries is how a consumer records that every
    /// acceptance has expired.
    /// </summary>
    [JsonPropertyName("allowUnindexed")]
    public List<AcceptanceEntry> AllowUnindexed { get; set; } = new();
}

/// <summary>
/// One accepted column. All three fields are mandatory; an entry missing any of
/// them is reported as JNT6003 and suppresses nothing, rather than being read
/// with a default. A silently-defaulted table or column would match no
/// synthetic and expire as JNT8012, which is the wrong diagnostic: the entry is
/// not stale, it is malformed.
///
/// The reason is mandatory for the same purpose it is mandatory on
/// <c>-- @allow-unindexed</c>: an acceptance whose justification is not written
/// down is a NoWarn entry with extra steps.
/// </summary>
public class AcceptanceEntry
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("column")]
    public string? Column { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
