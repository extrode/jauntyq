using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.MySql.Tests;

/// <summary>
/// Exercises the MySQL provider-native BulkInsert fast path
/// (MySqlConnector.MySqlBulkCopy over the AOT-safe DbDataReader adapter) against
/// a real MySQL engine. Requires the connection string's
/// AllowLoadLocalInfile=true (set in <see cref="MySqlFixture"/>) plus server
/// local_infile=1; without either, MySqlBulkCopy fails at runtime — so a green
/// run proves the requirement is wired up. Soft-skips when Docker is absent.
/// </summary>
[Collection("MySql")]
public class BulkInsertTests
{
    private readonly MySqlFixture _fx;
    public BulkInsertTests(MySqlFixture fx) => _fx = fx;

    [SkippableFact]
    public void BulkInsert_InsertsAllRows_ViaMySqlBulkCopy()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.Suppliers.GetAll().Count;
        var rows = new[]
        {
            new Supplier { CompanyName = "My Bulk A", City = "Portland" },
            new Supplier { CompanyName = "My Bulk B", City = "Seattle" },
            new Supplier { CompanyName = "My Bulk C", City = null },
        };

        int affected = _fx.Db.Suppliers.BulkInsert(rows);
        Assert.Equal(3, affected);

        var after = _fx.Db.Suppliers.GetAll();
        Assert.Equal(before + 3, after.Count);
        Assert.Contains(after, s => s.CompanyName == "My Bulk B" && s.City == "Seattle");
        Assert.Contains(after, s => s.CompanyName == "My Bulk C" && s.City == null);
    }

    [SkippableFact]
    public async Task BulkInsertAsync_Works()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.Shippers.GetAll().Count;
        var rows = new[]
        {
            new Shipper { CompanyName = "My Async Ship 1", Phone = "111" },
            new Shipper { CompanyName = "My Async Ship 2", Phone = "222" },
        };

        int affected = await _fx.Db.Shippers.BulkInsertAsync(rows);
        Assert.Equal(2, affected);
        Assert.Equal(before + 2, _fx.Db.Shippers.GetAll().Count);
    }

    [SkippableFact]
    public void BulkInsert_Empty_ReturnsZero()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        Assert.Equal(0, _fx.Db.Suppliers.BulkInsert(System.Array.Empty<Supplier>()));
    }
}
