using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Sqlite.Tests;

/// <summary>
/// Exercises the auto-synthesized BulkInsert(IEnumerable&lt;Row&gt;) against real
/// SQLite: many rows in one transaction, identity columns excluded, sync + async.
/// </summary>
public class BulkInsertTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public BulkInsertTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void BulkInsert_InsertsAllRows_InOneTransaction()
    {
        int before = _fx.Db.Suppliers.GetAll().Count;

        var rows = new[]
        {
            new Supplier { CompanyName = "Bulk Co A", City = "Portland" },
            new Supplier { CompanyName = "Bulk Co B", City = "Seattle" },
            new Supplier { CompanyName = "Bulk Co C", City = "Denver" },
        };

        int affected = _fx.Db.Suppliers.BulkInsert(rows);
        Assert.Equal(3, affected);

        var after = _fx.Db.Suppliers.GetAll();
        Assert.Equal(before + 3, after.Count);
        Assert.Contains(after, s => s.CompanyName == "Bulk Co B" && s.City == "Seattle");
    }

    [Fact]
    public async Task BulkInsertAsync_Works()
    {
        int before = _fx.Db.Shippers.GetAll().Count;
        var rows = new[]
        {
            new Shipper { CompanyName = "Async Ship 1", Phone = "111" },
            new Shipper { CompanyName = "Async Ship 2", Phone = "222" },
        };

        int affected = await _fx.Db.Shippers.BulkInsertAsync(rows);
        Assert.Equal(2, affected);
        Assert.Equal(before + 2, _fx.Db.Shippers.GetAll().Count);
    }

    [Fact]
    public void BulkInsert_Empty_ReturnsZero()
    {
        Assert.Equal(0, _fx.Db.Suppliers.BulkInsert(System.Array.Empty<Supplier>()));
    }
}
