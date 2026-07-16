using System.Linq;
using Xunit;

namespace JauntyQ.Conduit.MySql.Tests;

/// <summary>
/// Seeded tags: dotnet, sql, sqlite, database, offtopic - shared across
/// several articles via the article_tags junction, so listing must not
/// produce duplicates.
/// </summary>
public class TagTests : IClassFixture<ConduitMySqlFixture>
{
    private readonly ConduitMySqlFixture _fx;

    public TagTests(ConduitMySqlFixture fx) => _fx = fx;

    [SkippableFact]
    public void GetAll_ReturnsAllSeededTagsOnce()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var tags = _fx.Db.Tags.GetAll();

        var names = tags.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "database", "dotnet", "offtopic", "sql", "sqlite" }, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
