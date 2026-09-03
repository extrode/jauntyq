namespace Conduit.Differential.Tests;

/// <summary>
/// One case whose engines did not group as declared.
/// </summary>
public sealed record GroupingMismatch(string Case, string Engine, string Expected, string Observed)
{
    public override string ToString() =>
        $"{Case}: {Engine} was declared to agree with [{Expected}] but agreed with [{Observed}]";
}

public static class GroupingComparer
{
    /// <summary>
    /// Partitions engines by the result they returned. Engines land in the same
    /// group when their rendered results are identical, which is what makes
    /// "these two agree" a measurement rather than an assertion.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Observe(
        IReadOnlyDictionary<string, string> resultByEngine)
    {
        return resultByEngine
            .GroupBy(kvp => kvp.Value, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<string>)g.Select(kvp => kvp.Key)
                .OrderBy(e => e, StringComparer.Ordinal).ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Compares the observed partition to the declared one, restricted to the
    /// engines that actually ran. Restriction is what lets the suite run with
    /// three engines up and still assert something true about those three.
    ///
    /// Both directions fail: an engine that left its group and an engine that
    /// joined one produce the same kind of mismatch, because a stress case
    /// that quietly stops stressing is a behavior change too.
    /// </summary>
    public static IReadOnlyList<GroupingMismatch> Compare(
        string caseName,
        IReadOnlyList<IReadOnlyList<string>> expected,
        IReadOnlyList<IReadOnlyList<string>> observed,
        IReadOnlyCollection<string> available)
    {
        var expectedMates = Mates(expected, available);
        var observedMates = Mates(observed, available);

        var mismatches = new List<GroupingMismatch>();
        foreach (string engine in available.OrderBy(e => e, StringComparer.Ordinal))
        {
            string expectedFor = Render(expectedMates, engine);
            string observedFor = Render(observedMates, engine);
            if (!string.Equals(expectedFor, observedFor, StringComparison.Ordinal))
                mismatches.Add(new GroupingMismatch(caseName, engine, expectedFor, observedFor));
        }

        return mismatches;
    }

    /// <summary>
    /// Each engine mapped to the others it shares a group with. Comparing
    /// mate sets rather than group indexes means the report can name the engine
    /// that moved instead of two partitions the reader has to diff by eye.
    /// </summary>
    private static Dictionary<string, List<string>> Mates(
        IReadOnlyList<IReadOnlyList<string>> partition,
        IReadOnlyCollection<string> available)
    {
        var mates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> group in partition)
        {
            var present = group.Where(available.Contains).ToList();
            foreach (string engine in present)
                mates[engine] = present.Where(e => e != engine)
                    .OrderBy(e => e, StringComparer.Ordinal).ToList();
        }
        return mates;
    }

    private static string Render(Dictionary<string, List<string>> mates, string engine) =>
        mates.TryGetValue(engine, out List<string>? group)
            ? (group.Count == 0 ? "alone" : string.Join(", ", group))
            : "not declared";
}
