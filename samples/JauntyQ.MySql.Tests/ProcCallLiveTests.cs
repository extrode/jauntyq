using Xunit;

namespace JauntyQ.MySql.Tests;

/// <summary>
/// Live proof that a -- @call binding executes a real stored procedure via
/// CommandType.StoredProcedure against MySQL, materializing the result set the
/// snapshot declared. Soft-skips when Docker is unavailable.
/// </summary>
public class ProcCallLiveTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fx;
    public ProcCallLiveTests(MySqlFixture fx) => _fx = fx;

    [Fact]
    public void CallStoredProcedure_ReturnsTypedRows()
    {
        if (!_fx.Available) return;

        // Category 1 (Beverages) has Chai and Chang seeded.
        var rows = _fx.Db.Products.GetProductsByCategoryProc(1);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.ProductName)));
        Assert.Contains(rows, r => r.ProductName == "Chai");
    }
}
