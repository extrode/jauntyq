using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// The divergence-stressing corpus: queries chosen to make the engines
/// disagree, each asserting the grouping it is expected to produce.
///
/// 017's suite asks whether the five engines agree on an application's
/// queries. This one asks where they stop agreeing, which is the half a
/// consumer needs before porting.
/// </summary>
[Trait("Category", "Differential")]
[Collection("Differential")]
public class StressTests(EngineSet engines, ITestOutputHelper output)
{
    private readonly EngineSet _engines = engines;
    private readonly ITestOutputHelper _output = output;

    [SkippableFact]
    public void EveryStressCaseGroupsTheEnginesAsDeclared()
    {
        var problems = StressCorpus.Validate();
        Assert.True(problems.Count == 0,
            "The stress corpus is malformed:\n  " + string.Join("\n  ", problems));

        Skip.IfNot(_engines.Enough, _engines.SkipMessage);

        var available = _engines.AvailableEngines;
        var availableNames = available.Select(e => e.Name).ToList();

        var mismatches = new List<GroupingMismatch>();
        var report = new StringBuilder();
        report.AppendLine($"Engines: {string.Join(", ", availableNames)}.");

        foreach (StressCase stressCase in StressCorpus.Cases)
        {
            var resultByEngine = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (EngineHandle engine in available)
            {
                StressOutcome outcome = SqlRunner.Run(engine.Connection!, stressCase, engine.Name);
                resultByEngine[engine.Name] = outcome.GroupKey(stressCase.Ordered);
            }

            var observed = GroupingComparer.Observe(resultByEngine);
            mismatches.AddRange(GroupingComparer.Compare(
                stressCase.ToString(), stressCase.Groups, observed, availableNames));

            report.AppendLine($"  {stressCase}: {RenderPartition(observed)}");
        }

        if (mismatches.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"{mismatches.Count} case(s) did not group as declared:");
            foreach (GroupingMismatch mismatch in mismatches)
                report.AppendLine($"  {mismatch}");
        }

        _output.WriteLine(report.ToString());
        Assert.True(mismatches.Count == 0, report.ToString());
    }

    /// <summary>
    /// The discovery pass. Runs every case and reports what the engines
    /// actually did, which is the input to each declared grouping -- the
    /// groupings in the corpus are measurements, and this is what measures
    /// them. Gated on an environment variable because it asserts nothing.
    /// </summary>
    [SkippableFact]
    [Trait("Discovery", "true")]
    public void DiscoveryPassReportsWhatEachEngineReturns()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("JAUNTYQ_STRESS_DISCOVERY") == "1",
            "Set JAUNTYQ_STRESS_DISCOVERY=1 to run the discovery pass.");
        Skip.IfNot(_engines.Enough, _engines.SkipMessage);

        var available = _engines.AvailableEngines;
        var report = new StringBuilder();
        report.AppendLine($"# 018 discovery pass");
        report.AppendLine();
        report.AppendLine($"Engines: {string.Join(", ", available.Select(e => e.Name))}.");

        foreach (StressCase stressCase in StressCorpus.Cases)
        {
            report.AppendLine();
            report.AppendLine($"## {stressCase}");
            var resultByEngine = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (EngineHandle engine in available)
            {
                StressOutcome outcome = SqlRunner.Run(engine.Connection!, stressCase, engine.Name);
                resultByEngine[engine.Name] = outcome.GroupKey(stressCase.Ordered);
                report.AppendLine($"  {engine.Name,-10} {outcome.Describe(stressCase.Ordered)}");
            }

            report.AppendLine($"  => {RenderPartition(GroupingComparer.Observe(resultByEngine))}");
        }

        _output.WriteLine(report.ToString());
        string scratch = Path.Combine(RepoRoot(), "tmp");
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "018-discovery.md"), report.ToString());
    }

    private static string RenderPartition(IReadOnlyList<IReadOnlyList<string>> partition) =>
        string.Join(" | ", partition.Select(g => string.Join("+", g)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("JauntyQ.slnx not found above the test output directory.");
    }
}
