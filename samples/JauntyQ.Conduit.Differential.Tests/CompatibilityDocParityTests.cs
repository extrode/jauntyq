using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// R8: the allow-list is a consumer-facing compatibility statement, not a
/// test-internal file. This fails when an entry is added to the JSON and the
/// reference page is not updated, which is the only way the two can drift --
/// nothing else reads the doc.
/// </summary>
[Trait("Category", "Differential")]
public class CompatibilityDocParityTests
{
    private const string DocRelativePath = "docs/06-reference/cross-dialect-compatibility.md";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find the repo root (no JauntyQ.slnx above the test output).");
    }

    private static string Doc() => File.ReadAllText(Path.Combine(RepoRoot(), DocRelativePath));

    [Fact]
    public void TheReferencePageExists()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), DocRelativePath)),
            $"{DocRelativePath} is the published form of the allow-list and must exist.");
    }

    [Fact]
    public void EveryAllowListEntryAppearsInTheReferencePage()
    {
        string doc = Doc();

        var missing = AllowList.Load(AllowList.DefaultPath).Entries
            .Where(e => !doc.Contains(e.Query, StringComparison.Ordinal))
            .Select(e => e.Query)
            .ToList();

        Assert.True(missing.Count == 0,
            $"These accepted divergences are not documented in {DocRelativePath}:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryEngineAnEntryNamesAppearsBesideItInTheReferencePage()
    {
        string doc = Doc();

        var problems = new List<string>();
        foreach (AllowedDivergence entry in AllowList.Load(AllowList.DefaultPath).Entries)
            foreach (string engine in entry.Engines)
                if (!doc.Contains(Displayed(engine), StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{entry.Query}: {engine}");

        Assert.True(problems.Count == 0,
            $"Engines named by an entry but absent from {DocRelativePath}:\n  " + string.Join("\n  ", problems));
    }

    private static string Displayed(string engine) => engine switch
    {
        "Postgres" => "PostgreSQL",
        "SqlServer" => "SQL Server",
        "MariaDb" => "MariaDB",
        _ => engine,
    };

    [Fact]
    public void TheReferencePageNamesTheHarnessThatProducesIt()
    {
        Assert.Contains("samples/JauntyQ.Conduit.Differential.Tests", Doc(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReferencePageStatesTheFewerThanTwoEnginesRule()
    {
        Assert.Contains("Fewer than two engines is a skip", Doc(), StringComparison.Ordinal);
    }
}
