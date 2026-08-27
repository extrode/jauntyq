using JauntyQ.Conduit.Sqlite.Tests;
using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

/// <summary>
/// R9's enforcement. A query added to the Conduit projects appears on every
/// generated JauntyDb; if the corpus does not run it and no exclusion names
/// it, this fails by name rather than letting the new query go untested.
/// </summary>
[Trait("Category", "Differential")]
public class CoverageTests : IDisposable
{
    private readonly ConduitSqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static IReadOnlyList<string> CorpusQueries() =>
        Corpus.Reads.Select(c => c.Query)
            .Concat(Corpus.Mutations.Select(c => c.Query))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void EveryGeneratedQueryIsEitherRunOrExcludedWithAReason()
    {
        var generated = QueryDispatcher.AllQueryNames(_fixture.Db);
        var covered = CorpusQueries();

        var uncovered = generated
            .Where(q => !covered.Contains(q, StringComparer.Ordinal))
            .Where(q => !Corpus.Excluded.ContainsKey(q))
            .Where(q =>
            {
                string? twin = Corpus.SyncTwinOf(q);
                return twin is null
                    || (!covered.Contains(twin, StringComparer.Ordinal) && !Corpus.Excluded.ContainsKey(twin));
            })
            .ToList();

        Assert.True(uncovered.Count == 0,
            "These generated queries are neither in the corpus nor excluded:\n  "
            + string.Join("\n  ", uncovered));
    }

    [Fact]
    public void NoCorpusEntryNamesAQueryTheGeneratorDoesNotEmit()
    {
        var generated = QueryDispatcher.AllQueryNames(_fixture.Db);

        var missing = CorpusQueries()
            .Where(q => !generated.Contains(q, StringComparer.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0,
            "The corpus names queries no generated JauntyDb carries:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void NoExclusionNamesAQueryTheGeneratorDoesNotEmit()
    {
        var generated = QueryDispatcher.AllQueryNames(_fixture.Db);

        var stale = Corpus.Excluded.Keys
            .Where(q => !generated.Contains(q, StringComparer.Ordinal))
            .ToList();

        Assert.True(stale.Count == 0,
            "These exclusions name queries that no longer exist:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void EveryExclusionCarriesAReason()
    {
        Assert.All(Corpus.Excluded, e => Assert.False(string.IsNullOrWhiteSpace(e.Value)));
    }

    [Fact]
    public void NoQueryIsBothRunAndExcluded()
    {
        var both = CorpusQueries().Where(Corpus.Excluded.ContainsKey).ToList();

        Assert.True(both.Count == 0, "Run and excluded at once:\n  " + string.Join("\n  ", both));
    }
}
