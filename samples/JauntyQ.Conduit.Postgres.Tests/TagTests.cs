using System.Linq;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests;

/// <summary>
/// Seeded tags: dotnet, sql, sqlite, database, offtopic - shared across
/// several articles via the article_tags junction, so listing must not
/// produce duplicates.
/// </summary>
[Collection("ConduitPostgres")]
public class TagTests
{
    private readonly ConduitPostgresFixture _fx;

    public TagTests(ConduitPostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public void GetAll_ReturnsAllSeededTagsOnce()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var tags = _fx.Db.Tags.GetAll();

        var names = tags.Select(t => t.Name).ToList();
        foreach (var seeded in new[] { "database", "dotnet", "offtopic", "sql", "sqlite" })
            Assert.Equal(1, names.Count(n => n == seeded));
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
