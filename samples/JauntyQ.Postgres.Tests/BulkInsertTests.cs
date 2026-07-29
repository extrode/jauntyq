using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Postgres.Tests;

/// <summary>
/// Exercises the PostgreSQL provider-native BulkInsert fast path (Npgsql binary
/// COPY) against a real Postgres engine: many rows inserted, row count returned,
/// spot-check value round-trips, sync + async. Soft-skips when Docker is absent.
/// </summary>
[Collection("Postgres")]
public class BulkInsertTests
{
    private readonly PostgresFixture _fx;
    public BulkInsertTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public void BulkInsert_InsertsAllRows_ViaBinaryCopy()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.Suppliers.GetAll().Count;
        var rows = new[]
        {
            new Supplier { CompanyName = "Pg Bulk A", City = "Portland" },
            new Supplier { CompanyName = "Pg Bulk B", City = "Seattle" },
            new Supplier { CompanyName = "Pg Bulk C", City = null },
        };

        int affected = _fx.Db.Suppliers.BulkInsert(rows);
        Assert.Equal(3, affected);

        var after = _fx.Db.Suppliers.GetAll();
        Assert.Equal(before + 3, after.Count);
        Assert.Contains(after, s => s.CompanyName == "Pg Bulk B" && s.City == "Seattle");
        Assert.Contains(after, s => s.CompanyName == "Pg Bulk C" && s.City == null);
    }

    [SkippableFact]
    public async Task BulkInsertAsync_Works()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int before = _fx.Db.Shippers.GetAll().Count;
        var rows = new[]
        {
            new Shipper { CompanyName = "Pg Async Ship 1", Phone = "111" },
            new Shipper { CompanyName = "Pg Async Ship 2", Phone = "222" },
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
