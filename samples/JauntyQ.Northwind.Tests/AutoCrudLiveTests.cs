using Microsoft.Data.SqlClient;
using System.Transactions;
using Xunit;

namespace JauntyQ.Northwind.Tests;

/// <summary>
/// Exercises auto-CRUD synthetics (no user .sql file exists for these methods)
/// against the live Northwind database.
/// </summary>
public class AutoCrudLiveTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public AutoCrudLiveTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void Shippers_GetById_Synthetic_ReturnsSpeedyExpress()
    {
        // db/tables/Shippers/ only contains GetAll.sql — GetById is synthesized
        var shipper = _fixture.Db.Shippers.GetById(1);
        Assert.NotNull(shipper);
        Assert.Equal("Speedy Express", shipper.CompanyName);
    }

    [Fact]
    public async Task Shippers_GetByIdAsync_Synthetic_NonExistent_ReturnsNull()
    {
        var shipper = await _fixture.Db.Shippers.GetByIdAsync(9999);
        Assert.Null(shipper);
    }

    [Fact]
    public void Shippers_Update_Synthetic_RollsBack()
    {
        using var scope = new TransactionScope();
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        conn.Open();
        int affected = JauntyQ.Generated.Shippers.Update(conn, "Temporary Name", "(555) 000-0000", 1);
        Assert.Equal(1, affected);
        // scope.Dispose() without Complete() -> rollback
    }

    [Fact]
    public async Task Shippers_DeleteAsync_Synthetic_NonExistent_ReturnsZero()
    {
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        int affected = await JauntyQ.Generated.Shippers.DeleteAsync(conn, 9999);
        Assert.Equal(0, affected);
    }
}
