using Conduit.Sqlite.Tests;
using Xunit;

namespace Conduit.Differential.Tests;

/// <summary>
/// Drives the reflection layer against SQLite alone, which needs no container,
/// so the dispatcher stays covered on a machine with no Docker.
/// </summary>
[Trait("Category", "Differential")]
public class DispatcherTests : IDisposable
{
    private readonly ConduitSqliteFixture _fixture = new();

    private object Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static IReadOnlyDictionary<string, object?> Args(params (string, object?)[] cells)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in cells)
            map[name] = value;
        return map;
    }

    [Fact]
    public void TheEntityAccessorsAreDiscovered()
    {
        var entities = QueryDispatcher.EntityNames(Db);

        Assert.Contains("Articles", entities);
        Assert.Contains("Users", entities);
        Assert.Contains("Tags", entities);
        Assert.Contains("Comments", entities);
        Assert.Contains("Favorites", entities);
        Assert.Contains("Follows", entities);
        Assert.Contains("ArticleTags", entities);
    }

    [Fact]
    public void QueryNamesAreEntitySlashMethod()
    {
        Assert.Contains("Articles/GetBySlug", QueryDispatcher.QueryNames(Db, "Articles"));
    }

    [Fact]
    public void AFirstQueryReturningARowYieldsOneRowOfItsColumns()
    {
        var rows = QueryDispatcher.Invoke(Db, "Users/GetById", Args(("Id", 1)));

        var row = Assert.Single(rows);
        Assert.Equal("jane", row["Username"]);
        Assert.Equal("jane@example.com", row["Email"]);
    }

    [Fact]
    public void AFirstQueryMatchingNothingYieldsNoRows()
    {
        Assert.Empty(QueryDispatcher.Invoke(Db, "Users/GetById", Args(("Id", 999))));
    }

    [Fact]
    public void AMultiRowQueryYieldsARowEach()
    {
        var rows = QueryDispatcher.Invoke(Db, "Tags/GetAll", Args());

        Assert.Equal(5, rows.Count);
        Assert.Equal("database", rows[0]["Name"]);
    }

    [Fact]
    public void AnEachLoaderTakesACollectionArgument()
    {
        var rows = QueryDispatcher.Invoke(Db, "Comments/GetByArticleId", Args(("ArticleIds", new[] { 1 })));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ANullableArgumentIsPassedAsNull()
    {
        var rows = QueryDispatcher.Invoke(Db, "Articles/GetFiltered",
            Args(("Tag", null), ("Author", null), ("FavoritedBy", null), ("Skip", 0), ("Take", 10)));

        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public void AnUnknownEntityNamesTheOnesThatExist()
    {
        var ex = Assert.Throws<QueryDispatcher.DispatchException>(
            () => QueryDispatcher.Invoke(Db, "Widgets/GetAll", Args()));

        Assert.Contains("No entity 'Widgets'", ex.Message);
        Assert.Contains("Articles", ex.Message);
    }

    [Fact]
    public void AnUnknownQueryNamesTheOnesThatExist()
    {
        var ex = Assert.Throws<QueryDispatcher.DispatchException>(
            () => QueryDispatcher.Invoke(Db, "Articles/GetByNothing", Args()));

        Assert.Contains("No query 'Articles/GetByNothing'", ex.Message);
        Assert.Contains("GetBySlug", ex.Message);
    }

    [Fact]
    public void AMissingArgumentNamesBothWhatWasNeededAndWhatWasSupplied()
    {
        var ex = Assert.Throws<QueryDispatcher.DispatchException>(
            () => QueryDispatcher.Invoke(Db, "Users/GetById", Args(("WrongName", 1))));

        Assert.Contains("needs an argument named 'Id'", ex.Message);
        Assert.Contains("WrongName", ex.Message);
    }

    [Fact]
    public void AQueryNameWithoutASlashIsRejected()
    {
        var ex = Assert.Throws<QueryDispatcher.DispatchException>(
            () => QueryDispatcher.Invoke(Db, "GetById", Args(("Id", 1))));

        Assert.Contains("is not in Entity/Method form", ex.Message);
    }

    // Auto-CRUD emits Delete(id) beside Delete(row), and Upsert and Update the
    // same way. Nothing in the corpus runs an overloaded method, so these
    // cover the selection rule directly.

    [Fact]
    public void AnOverloadIsChosenByWhichParametersTheArgumentsSupply()
    {
        // Every seeded tag is referenced by article_tags, so a fresh one is
        // inserted rather than deleting into a foreign-key failure.
        var inserted = QueryDispatcher.Invoke(Db, "Tags/Insert", Args(("Name", "throwaway")));
        int id = System.Convert.ToInt32(inserted.Single().Values.Single());

        var affected = QueryDispatcher.Invoke(Db, "Tags/Delete", Args(("id", id)));

        Assert.Equal(1L, RowNormalizer.Normalize(affected.Single().Values.Single()));
        Assert.Empty(QueryDispatcher.Invoke(Db, "Tags/GetById", Args(("id", id))));
    }

    [Fact]
    public void TheColumnTakingOverloadIsChosenOverTheRowTakingOne()
    {
        var affected = QueryDispatcher.Invoke(Db, "Tags/Update", Args(("id", 4), ("name", "renamed")));

        Assert.Equal(1L, RowNormalizer.Normalize(affected.Single().Values.Single()));
        Assert.Equal("renamed", QueryDispatcher.Invoke(Db, "Tags/GetById", Args(("id", 4))).Single()["Name"]);
    }

    [Fact]
    public void ArgumentsSatisfyingNoOverloadNameEveryOnesParameters()
    {
        var ex = Assert.Throws<QueryDispatcher.DispatchException>(
            () => QueryDispatcher.Invoke(Db, "Tags/Delete", Args(("NotAParameter", 1))));

        Assert.Contains("has 2 overloads", ex.Message);
        Assert.Contains("NotAParameter", ex.Message);
        Assert.Contains("row", ex.Message);
    }

    [Fact]
    public void AScalarResultIsOneRowNamedValue()
    {
        var rows = QueryDispatcher.Invoke(Db, "Articles/GetFeedCount", Args(("UserId", 1)));

        var row = Assert.Single(rows);
        Assert.Equal(3L, RowNormalizer.Normalize(row.Values.Single()));
    }
}
