using System.Text;
using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// The suite proper: one corpus, five engines, results compared to one
/// another rather than to a hand-written expectation.
///
/// It is a single test method rather than a theory per case because the
/// mutation script is ordered and the engines are shared collection state --
/// xunit gives no ordering guarantee between test methods, and a corpus case
/// that ran after Articles/Delete instead of before it would compare rows
/// nobody intended.
/// </summary>
[Trait("Category", "Differential")]
[Collection("Differential")]
public class DifferentialTests(EngineSet engines)
{
    private readonly EngineSet _engines = engines;

    [SkippableFact]
    public void TheSameCorpusProducesTheSameResultsOnEveryEngine()
    {
        var allowList = AllowList.Load(AllowList.DefaultPath);

        // Structural problems fail on the allow-list itself, before any engine
        // runs -- a malformed entry must not read as "nothing was accepted".
        var problems = allowList.Validate();
        Assert.True(problems.Count == 0,
            "The allow-list is malformed:\n  " + string.Join("\n  ", problems));

        Skip.IfNot(_engines.Enough, _engines.SkipMessage);

        EngineHandle pivot = _engines.Pivot!;
        var others = _engines.AvailableEngines.Where(e => e.Name != pivot.Name).ToList();

        var divergences = new List<Divergence>();
        var accepted = new List<Divergence>();
        var queriesRun = new HashSet<string>(StringComparer.Ordinal);

        void RunPhase(IReadOnlyList<CorpusCase> cases, string phase)
        {
            foreach (CorpusCase c in cases)
            {
                queriesRun.Add(c.Query);
                EngineOutcome pivotOutcome = EngineOutcome.Of(
                    () => QueryDispatcher.Invoke(pivot.Db!, c.Query, c.Arguments));

                foreach (EngineHandle other in others)
                {
                    EngineOutcome otherOutcome = EngineOutcome.Of(
                        () => QueryDispatcher.Invoke(other.Db!, c.Query, c.Arguments));

                    Divergence? divergence = ResultComparer.Compare(
                        c.Query, $"{phase}: {c.ArgumentSet}", pivot.Name, pivotOutcome,
                        other.Name, otherOutcome, c.Ordered);

                    if (divergence is null)
                        continue;

                    if (allowList.Accepts(divergence))
                        accepted.Add(divergence);
                    else
                        divergences.Add(divergence);
                }
            }
        }

        RunPhase(Corpus.Reads, "seeded");
        RunPhase(Corpus.Mutations, "mutation");
        RunPhase(Corpus.Reads, "after mutations");

        var dead = allowList.DeadEntries(
            _engines.AvailableEngines.Select(e => e.Name).ToList(), queriesRun);

        var report = new StringBuilder();
        report.AppendLine($"Pivot: {pivot.Name}. Compared against: {string.Join(", ", others.Select(e => e.Name))}.");
        report.AppendLine($"Skipped: {(_engines.Engines.Count == _engines.AvailableEngines.Count ? "none" : string.Join(", ", _engines.Engines.Where(e => !e.Available).Select(e => e.Name)))}.");
        report.AppendLine($"Accepted divergences: {accepted.Count}.");

        foreach (Divergence a in accepted)
            report.AppendLine($"  accepted: {a}");

        if (dead.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("Allow-list entries whose divergence no longer occurs -- remove them:");
            foreach (AllowedDivergence entry in dead)
                report.AppendLine($"  {entry}");
        }

        if (divergences.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"{divergences.Count} unaccepted divergence(s):");
            foreach (Divergence d in divergences)
                report.AppendLine($"  {d}");
        }

        Assert.True(divergences.Count == 0 && dead.Count == 0, report.ToString());
    }

    [SkippableFact]
    public void EveryAvailableEngineSeedsTheSameRowCounts()
    {
        Skip.IfNot(_engines.Enough, _engines.SkipMessage);

        var counts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (EngineHandle engine in _engines.AvailableEngines)
        {
            var sizes = new List<string>();
            foreach (string entity in QueryDispatcher.EntityNames(engine.Db!).OrderBy(e => e, StringComparer.Ordinal))
                sizes.Add($"{entity}={QueryDispatcher.Invoke(engine.Db!, $"{entity}/GetAll", new Dictionary<string, object?>()).Count}");
            counts[engine.Name] = string.Join(", ", sizes);
        }

        string pivot = counts[_engines.Pivot!.Name];
        var differing = counts.Where(kvp => kvp.Value != pivot).ToList();

        Assert.True(differing.Count == 0,
            $"The seed did not produce the same rows everywhere.\n  {_engines.Pivot!.Name}: {pivot}\n  "
            + string.Join("\n  ", differing.Select(kvp => $"{kvp.Key}: {kvp.Value}")));
    }
}
