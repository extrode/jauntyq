using System.Linq;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests;

/// <summary>
/// Seeded tags: dotnet, sql, sqlite, database, offtopic - shared across
/// several articles via the article_tags junction, so listing must not
/// produce duplicates.
/// </summary>
public class TagTests : IClassFixture<ConduitPostgresFixture>
{
    private readonly ConduitPostgresFixture _fx;

    public TagTests(ConduitPostgresFixture fx) => _fx = fx;

    [Fact]
    public void GetAll_ReturnsAllSeededTagsOnce()
    {
        if (!_fx.Available) return;

        var tags = _fx.Db.Tags.GetAll();

        var names = tags.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "database", "dotnet", "offtopic", "sql", "sqlite" }, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
