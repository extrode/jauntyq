using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class RuntimeIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RuntimeIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // Create test schema
        ExecuteNonQuery(@"
            CREATE TABLE categories (
                category_id INTEGER PRIMARY KEY,
                category_name TEXT NOT NULL
            )");

        ExecuteNonQuery(@"
            CREATE TABLE products (
                product_id INTEGER PRIMARY KEY,
                product_name TEXT NOT NULL,
                category_id INTEGER,
                unit_price REAL NOT NULL,
                discontinued INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (category_id) REFERENCES categories(category_id)
            )");

        // Insert test data
        ExecuteNonQuery("INSERT INTO categories (category_id, category_name) VALUES (1, 'Beverages')");
        ExecuteNonQuery("INSERT INTO categories (category_id, category_name) VALUES (2, 'Condiments')");

        ExecuteNonQuery("INSERT INTO products (product_id, product_name, category_id, unit_price, discontinued) VALUES (1, 'Chai', 1, 18.00, 0)");
        ExecuteNonQuery("INSERT INTO products (product_id, product_name, category_id, unit_price, discontinued) VALUES (2, 'Chang', 1, 19.00, 0)");
        ExecuteNonQuery("INSERT INTO products (product_id, product_name, category_id, unit_price, discontinued) VALUES (3, 'Aniseed Syrup', 2, 10.00, 0)");
        ExecuteNonQuery("INSERT INTO products (product_id, product_name, category_id, unit_price, discontinued) VALUES (4, 'Discontinued Item', NULL, 5.00, 1)");
    }

    [Fact]
    public void SimpleQuery_ReturnsAllProducts()
    {
        var sql = "select product_id, product_name from products";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        var results = new List<(int Id, string Name)>();
        while (reader.Read())
        {
            results.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        Assert.Equal(4, results.Count);
        Assert.Contains(results, r => r.Name == "Chai");
    }

    [Fact]
    public void ParameterizedQuery_FiltersCorrectly()
    {
        var sql = "select p.product_id, p.product_name from products p where p.category_id = @categoryId";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var p0 = cmd.CreateParameter(); p0.ParameterName = "@categoryId"; p0.Value = 1; cmd.Parameters.Add(p0);
        using var reader = cmd.ExecuteReader();

        var results = new List<(int Id, string Name)>();
        while (reader.Read())
        {
            results.Add((reader.GetInt32(0), reader.GetString(1)));
        }

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Name == "Chai");
        Assert.Contains(results, r => r.Name == "Chang");
    }

    [Fact]
    public void JoinQuery_ReturnsJoinedData()
    {
        var sql = @"select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var p0 = cmd.CreateParameter(); p0.ParameterName = "@categoryId"; p0.Value = 1; cmd.Parameters.Add(p0);
        using var reader = cmd.ExecuteReader();

        var results = new List<(int Id, string Name, string Category)>();
        while (reader.Read())
        {
            results.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("Beverages", r.Category));
    }

    [Fact]
    public void NullableColumn_HandledCorrectly()
    {
        var sql = "select product_id, product_name, category_id from products where product_id = @id";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var p0 = cmd.CreateParameter(); p0.ParameterName = "@id"; p0.Value = 4; cmd.Parameters.Add(p0); // Discontinued Item has NULL category_id
        using var reader = cmd.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(4, reader.GetInt32(0));
        Assert.Equal("Discontinued Item", reader.GetString(1));
        Assert.True(reader.IsDBNull(2)); // category_id is NULL
    }

    [Fact]
    public void OrdinalAccess_WorksCorrectly()
    {
        var sql = "select product_id, product_name, unit_price from products where product_id = 1";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));            // product_id
        Assert.Equal("Chai", reader.GetString(1));       // product_name
        Assert.Equal(18.0, reader.GetDouble(2));         // unit_price (SQLite stores as REAL → double)
    }

    [Fact]
    public void ParameterBinding_InlinedStyle_Works()
    {
        // Verifies the inline parameter-binding pattern that generated code emits
        // (no helper — ADO.NET directly, matching what CodeEmitter produces).
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "select product_name from products where product_id = @id";
        var p0 = cmd.CreateParameter(); p0.ParameterName = "@id"; p0.Value = 1; cmd.Parameters.Add(p0);
        using var reader = cmd.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("Chai", reader.GetString(0));
    }

    [Fact]
    public void GeneratedCodeSimulation_FullPipeline()
    {
        // Simulate what the generated code does:
        // 1. Create command with SQL text
        // 2. Bind parameters
        // 3. Execute reader with ordinal access
        // 4. Map to DTO

        var sql = @"select p.product_id, p.product_name, c.category_name
from products p
join categories c on p.category_id = c.category_id
where p.category_id = @categoryId";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;

        var p0 = cmd.CreateParameter();
        p0.ParameterName = "@categoryId";
        p0.Value = (object?)1 ?? DBNull.Value;
        cmd.Parameters.Add(p0);

        using var reader = cmd.ExecuteReader();
        var results = new List<GetProductsByCategoryRow>();
        while (reader.Read())
        {
            results.Add(new GetProductsByCategoryRow
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                CategoryName = reader.GetString(2)
            });
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Chai", results[0].ProductName);
        Assert.Equal("Beverages", results[0].CategoryName);
        Assert.Equal("Chang", results[1].ProductName);
    }

    private void ExecuteNonQuery(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

// Simulated DTO (mirrors what CodeEmitter would generate)
public class GetProductsByCategoryRow
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string CategoryName { get; set; } = null!;
}
