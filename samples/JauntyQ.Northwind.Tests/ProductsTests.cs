using Microsoft.Data.SqlClient;
using System.Transactions;
using Xunit;

namespace JauntyQ.Northwind.Tests;

public class ProductsTests : IClassFixture<NorthwindFixture>
{
    private readonly NorthwindFixture _fixture;
    public ProductsTests(NorthwindFixture fixture) => _fixture = fixture;

    [Fact]
    public void GetAll_Returns77Products()
    {
        var results = _fixture.Db.Products.GetAll();
        Assert.Equal(77, results.Count);
    }

    [Fact]
    public void GetById_ReturnsChai()
    {
        // -- @first: single row or null
        var product = _fixture.Db.Products.GetById(1);
        Assert.NotNull(product);
        Assert.Equal("Chai", product.ProductName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsChai()
    {
        var product = await _fixture.Db.Products.GetByIdAsync(1);
        Assert.NotNull(product);
        Assert.Equal("Chai", product.ProductName);
    }

    [Fact]
    public async Task GetAllAsync_Returns77Products()
    {
        var results = await _fixture.Db.Products.GetAllAsync();
        Assert.Equal(77, results.Count);
    }

    [Fact]
    public async Task GetByCategoryAsync_StaticWithCancellation_ReturnsProducts()
    {
        using var cts = new CancellationTokenSource();
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        var results = await JauntyQ.Generated.Products.GetByCategoryAsync(conn, 1, cts.Token);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void GetByCategory_Beverages_ReturnsProducts()
    {
        var results = _fixture.Db.Products.GetByCategory(1);
        Assert.NotEmpty(results);
        Assert.All(results, p => Assert.NotNull(p.CategoryName));
    }

    [Fact]
    public void GetBySupplier_ReturnsProducts()
    {
        var results = _fixture.Db.Products.GetBySupplier(1);
        Assert.NotEmpty(results);
        Assert.All(results, p => Assert.NotNull(p.SupplierName));
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        var product = _fixture.Db.Products.GetById(9999);
        Assert.Null(product);
    }

    [Fact]
    public async Task InsertAsync_RollsBackWithTransactionScope()
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        conn.Open();
        int affected = await JauntyQ.Generated.Products.InsertAsync(
            conn, "TestProductAsync", 1, 1, 9.99m, false);
        Assert.Equal(1, affected);
        // scope.Dispose() without Complete() -> rollback
    }

    [Fact]
    public void Insert_ReturnsAffectedRows()
    {
        // TransactionScope with a fresh connection ensures auto-enlistment and rollback
        using var scope = new TransactionScope();
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        conn.Open();
        int affected = JauntyQ.Generated.Products.Insert(
            conn, "TestProduct", 1, 1, 9.99m, false);
        Assert.Equal(1, affected);
        // scope.Dispose() without Complete() → rollback
    }

    [Fact]
    public void Update_ReturnsAffectedRows()
    {
        using var scope = new TransactionScope();
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        conn.Open();
        int affected = JauntyQ.Generated.Products.Update(
            conn, "UpdatedChai", 19.99m, false, 1);
        Assert.Equal(1, affected);
    }

    [Fact]
    public void Delete_ReturnsAffectedRows()
    {
        using var scope = new TransactionScope();
        using var conn = new SqlConnection(NorthwindFixture.ConnectionString);
        conn.Open();
        // Insert a product first, then delete it (avoids FK conflicts with existing data)
        JauntyQ.Generated.Products.Insert(conn, "ToDelete", null, null, null, false);
        // Get the inserted product's ID
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(ProductId) FROM Products";
        int newId = (int)cmd.ExecuteScalar()!;
        int affected = JauntyQ.Generated.Products.Delete(conn, newId);
        Assert.Equal(1, affected);
    }
}
