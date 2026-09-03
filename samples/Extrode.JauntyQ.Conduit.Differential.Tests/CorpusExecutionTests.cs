using Conduit.Sqlite.Tests;
using Xunit;

namespace Conduit.Differential.Tests;

/// <summary>
/// Runs the whole corpus against SQLite alone.
///
/// This asserts nothing about cross-engine agreement -- one engine cannot
/// disagree with itself. It exists so a wrong argument name or a stale
/// identity value in the corpus fails on a machine with no Docker, instead of
/// surfacing as a container-only failure nobody can reproduce locally.
/// </summary>
[Trait("Category", "Differential")]
public class CorpusExecutionTests : IDisposable
{
    private readonly ConduitSqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    public static TheoryData<string, string> ReadCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in Corpus.Reads)
            data.Add(c.Query, c.ArgumentSet);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReadCases))]
    public void EveryReadCaseDispatches(string query, string argumentSet)
    {
        CorpusCase c = Corpus.Reads.Single(x =>
            x.Query == query && x.ArgumentSet == argumentSet);

        QueryDispatcher.Invoke(_fixture.Db, c.Query, c.Arguments);
    }

    [Fact]
    public void TheMutationScriptRunsInOrderAndTheReadsStillDispatchAfterIt()
    {
        foreach (CorpusCase c in Corpus.Mutations)
            QueryDispatcher.Invoke(_fixture.Db, c.Query, c.Arguments);

        foreach (CorpusCase c in Corpus.Reads)
            QueryDispatcher.Invoke(_fixture.Db, c.Query, c.Arguments);
    }

    [Fact]
    public void TheMutationScriptChangesWhatTheReadsReturn()
    {
        var before = QueryDispatcher.Invoke(_fixture.Db, "Users/GetAll", new Dictionary<string, object?>());

        foreach (CorpusCase c in Corpus.Mutations)
            QueryDispatcher.Invoke(_fixture.Db, c.Query, c.Arguments);

        var after = QueryDispatcher.Invoke(_fixture.Db, "Users/GetAll", new Dictionary<string, object?>());

        Assert.Equal(4, before.Count);
        Assert.Equal(5, after.Count);
    }

    [Fact]
    public void TheInsertsLandOnTheIdentitiesTheLaterMutationsAssume()
    {
        foreach (CorpusCase c in Corpus.Mutations.Take(3))
            QueryDispatcher.Invoke(_fixture.Db, c.Query, c.Arguments);

        var user = Assert.Single(QueryDispatcher.Invoke(
            _fixture.Db, "Users/GetByUsername", new Dictionary<string, object?> { ["Username"] = "erin" }));
        Assert.Equal(5L, RowNormalizer.Normalize(user["Id"]));

        var article = Assert.Single(QueryDispatcher.Invoke(
            _fixture.Db, "Articles/GetBySlug", new Dictionary<string, object?> { ["Slug"] = "differential-testing" }));
        Assert.Equal(5L, RowNormalizer.Normalize(article["Id"]));

        var tag = Assert.Single(QueryDispatcher.Invoke(
            _fixture.Db, "Tags/GetByName", new Dictionary<string, object?> { ["Name"] = "testing" }));
        Assert.Equal(6L, RowNormalizer.Normalize(tag["Id"]));
    }

    [Fact]
    public void NoTwoReadCasesShareAQueryAndArgumentSet()
    {
        var duplicates = Corpus.Reads
            .GroupBy(c => $"{c.Query} [{c.ArgumentSet}]", StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join("\n  ", duplicates));
    }
}
