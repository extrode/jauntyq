using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conduit.Differential.Tests;

public sealed class AllowedDivergence
{
    [JsonPropertyName("query")] public string Query { get; set; } = "";
    [JsonPropertyName("engines")] public List<string> Engines { get; set; } = new();
    [JsonPropertyName("difference")] public string Difference { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";

    public override string ToString() =>
        $"{Query} on [{string.Join(", ", Engines)}]: {Difference}";
}

/// <summary>
/// The suite's only escape from "any divergence fails".
///
/// An entry is the product's statement that two engines genuinely differ here,
/// that JauntyQ cannot make them agree, and why. Three rules keep it from
/// decaying into a suppression file: a reason is mandatory (R6), an entry
/// whose divergence stopped occurring is reported dead (R7), and every entry
/// must appear in the published reference doc (R8), which
/// CompatibilityDocParityTests enforces.
/// </summary>
public sealed class AllowList
{
    private readonly List<AllowedDivergence> _entries;
    private readonly HashSet<AllowedDivergence> _matched = new();

    public AllowList(IEnumerable<AllowedDivergence> entries) => _entries = entries.ToList();

    public IReadOnlyList<AllowedDivergence> Entries => _entries;

    public static AllowList Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"The allow-list is missing: {path}", path);

        return new AllowList(Parse(File.ReadAllText(path)));
    }

    public static IReadOnlyList<AllowedDivergence> Parse(string json) =>
        JsonSerializer.Deserialize<List<AllowedDivergence>>(json)
        ?? throw new InvalidOperationException("The allow-list parsed to null; it must be a JSON array.");

    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "allowed-divergences.json");

    /// <summary>
    /// Structural problems, checked before any engine runs so a malformed
    /// entry fails on itself rather than silently accepting nothing.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        for (int i = 0; i < _entries.Count; i++)
        {
            AllowedDivergence entry = _entries[i];
            if (string.IsNullOrWhiteSpace(entry.Query))
                problems.Add($"entry {i}: no query named");
            if (entry.Engines.Count < 2)
                problems.Add($"entry {i} ('{entry.Query}'): names {entry.Engines.Count} engine(s); a divergence is between two");
            if (string.IsNullOrWhiteSpace(entry.Difference))
                problems.Add($"entry {i} ('{entry.Query}'): no difference described");
            if (string.IsNullOrWhiteSpace(entry.Reason))
                problems.Add($"entry {i} ('{entry.Query}'): no reason given -- an entry without one is a suppression, not an acceptance");
        }
        return problems;
    }

    /// <summary>
    /// True when this divergence is accepted. Matching is by query and the
    /// pair of engines; the difference text is a substring check so a row
    /// value in the message does not have to be reproduced verbatim.
    /// </summary>
    public bool Accepts(Divergence divergence)
    {
        AllowedDivergence? hit = _entries.FirstOrDefault(e =>
            string.Equals(e.Query, divergence.Query, StringComparison.Ordinal)
            && e.Engines.Contains(divergence.PivotEngine, StringComparer.OrdinalIgnoreCase)
            && e.Engines.Contains(divergence.OtherEngine, StringComparer.OrdinalIgnoreCase)
            && divergence.Difference.Contains(e.Difference, StringComparison.OrdinalIgnoreCase));

        if (hit is null)
            return false;

        _matched.Add(hit);
        return true;
    }

    /// <summary>
    /// Entries that matched nothing in a run where their query and engines
    /// were all exercised. An entry whose engines were skipped is not dead,
    /// it was simply not tested, so it is excluded here.
    /// </summary>
    public IReadOnlyList<AllowedDivergence> DeadEntries(
        IReadOnlyCollection<string> enginesRun, IReadOnlyCollection<string> queriesRun) =>
        _entries.Where(e => !_matched.Contains(e))
            .Where(e => e.Engines.All(x => enginesRun.Contains(x, StringComparer.OrdinalIgnoreCase)))
            .Where(e => queriesRun.Contains(e.Query, StringComparer.Ordinal))
            .ToList();
}
