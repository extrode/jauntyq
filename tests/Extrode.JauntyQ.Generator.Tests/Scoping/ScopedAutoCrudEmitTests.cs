using Extrode.JauntyQ.Schema;
using Microsoft.CodeAnalysis;
using Xunit;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

public class ScopedAutoCrudEmitTests
{
    private const string OrdersOnly = @"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" } ] }";

    private static GeneratorDriverRunResult Generate(string? scopeJson, string dialect = "sqlite") =>
        Run(scopeJson == null ? Array.Empty<(string, string)>() : new[] { (ScopePath, scopeJson) },
            SchemaJson.Replace(@"""dialect"": ""sqlite""", $@"""dialect"": ""{dialect}"""), null, true);

    private static string Source(GeneratorDriverRunResult result, string hintName)
    {
        foreach (var r in result.Results)
            foreach (var s in r.GeneratedSources)
                if (s.HintName == hintName)
                    return s.SourceText.ToString();
        throw new InvalidOperationException($"{hintName} was not generated");
    }

    private static Dictionary<string, string> Sources(GeneratorDriverRunResult result)
    {
        var all = new Dictionary<string, string>();
        foreach (var r in result.Results)
            foreach (var s in r.GeneratedSources)
                all[s.HintName] = s.SourceText.ToString();
        return all;
    }

    [Fact]
    public void Update_TakesTheScopeFirst()
    {
        var update = Source(Generate(OrdersOnly), "Orders.Update.auto.g.cs");

        Assert.Contains("public static int Update(DbConnection conn, int tenant_id, string? note, int id, DbTransaction? transaction = null)", update);
    }

    [Fact]
    public void Update_OfAnUnscopedTable_KeepsSetThenKey()
    {
        var update = Source(Generate(OrdersOnly), "OrderLines.Update.auto.g.cs");

        Assert.Contains("public static int Update(DbConnection conn, int order_id, int tenant_id, int qty, int id, DbTransaction? transaction = null)", update);
    }

    [Fact]
    public void PocoOverloads_TakeTheScopeBeforeTheRow_AndForwardItInPlaceOfTheRowsOwn()
    {
        var poco = Source(Generate(OrdersOnly), "Orders.Poco.auto.g.cs");

        Assert.Contains("public int Insert(int tenant_id, Order row) => Insert(tenant_id, row.Id, row.Note);", poco);
        Assert.Contains("public static int Update(DbConnection conn, int tenant_id, Order row, DbTransaction? transaction = null) => Update(conn, tenant_id, row.Note, row.Id, transaction);", poco);
        Assert.Contains("public Task<int> DeleteAsync(int tenant_id, Order row, CancellationToken cancellationToken = default) => DeleteAsync(tenant_id, row.Id, cancellationToken);", poco);
        Assert.Contains("public static Task<int> UpsertAsync(DbConnection conn, int tenant_id, Order row, DbTransaction? transaction = null, CancellationToken cancellationToken = default) => UpsertAsync(conn, tenant_id, row.Id, row.Note, transaction, cancellationToken);", poco);
        Assert.DoesNotContain("row.TenantId", poco);
    }

    [Fact]
    public void PocoInsert_ThatReturnsTheIdentity_TakesTheScopeBeforeTheRow()
    {
        var schema = SchemaJson
            .Replace(@"""dialect"": ""sqlite""", @"""dialect"": ""sqlserver""")
            .Replace(@"""isPrimaryKey"": true }", @"""isPrimaryKey"": true, ""isIdentity"": true }");
        var result = Run(new[] { (ScopePath, OrdersOnly) }, schema, null, true);

        var poco = Source(result, "Orders.Poco.auto.g.cs");
        Assert.Contains("public int Insert(int tenant_id, Order row)", poco);
        Assert.Contains("int id = Insert(tenant_id, row.Note);", poco);
        Assert.Contains("public static int Insert(DbConnection conn, int tenant_id, Order row, DbTransaction? transaction = null)", poco);
        Assert.Contains("public async Task<int> InsertAsync(int tenant_id, Order row, CancellationToken cancellationToken = default)", poco);
        Assert.Contains("public static async Task<int> InsertAsync(DbConnection conn, int tenant_id, Order row, DbTransaction? transaction = null, CancellationToken cancellationToken = default)", poco);
    }

    [Fact]
    public void Upsert_OnSqlite_UpdatesOnlyTheCallersRow()
    {
        var upsert = Source(Generate(OrdersOnly), "Orders.Upsert.auto.g.cs");

        Assert.Contains("ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id, note = EXCLUDED.note\n", upsert.Replace("\r\n", "\n"));
        Assert.Contains("WHERE orders.tenant_id = EXCLUDED.tenant_id", upsert);
        Assert.Contains("Upsert(DbConnection conn, int tenant_id, int id, string? note", upsert);
    }

    [Fact]
    public void Upsert_OnSqlServer_MatchesOnTheScopeToo()
    {
        var upsert = Source(Generate(OrdersOnly, "sqlserver"), "Orders.Upsert.auto.g.cs");

        Assert.Contains("ON target.id = src.id AND target.tenant_id = src.tenant_id", upsert);
    }

    [Fact]
    public void Reads_FilterOnTheScope_AndTakeItFirst()
    {
        var result = Generate(OrdersOnly);

        var getAll = Source(result, "Orders.GetAll.auto.g.cs");
        Assert.Contains("public static List<Order> GetAll(DbConnection conn, int tenant_id, DbTransaction? transaction = null)", getAll);
        Assert.Contains("WHERE orders.tenant_id = @tenant_id", getAll);
        Assert.Contains("public static Order? GetById(DbConnection conn, int tenant_id, int id, DbTransaction? transaction = null)", Source(result, "Orders.GetById.auto.g.cs"));
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("MySQL")]
    public void Upsert_OnMySql_IsNotGenerated_ForAScopedTable(string dialect)
    {
        var result = Generate(OrdersOnly, dialect);

        Assert.False(Emits(result, "Orders.Upsert.auto.g.cs"));
        Assert.True(Emits(result, "Customers.Upsert.auto.g.cs"));
        Assert.DoesNotContain("Upsert", Source(result, "Orders.Poco.auto.g.cs"));
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "JNT4007");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(
            "No Upsert is generated for table 'orders': it is scoped by 'tenant_id', and MySQL's ON DUPLICATE KEY UPDATE " +
            "has no WHERE, so a key collision with another scope's row would overwrite that row. Use Insert and Update, " +
            "or write the upsert by hand and prove the scope in it.",
            diagnostic.GetMessage());
    }

    [Fact]
    public void Upsert_OnMySql_IsGenerated_WithoutAScopeFile()
    {
        var result = Generate(null, "mysql");

        Assert.True(Emits(result, "Orders.Upsert.auto.g.cs"));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT4007");
    }

    [Fact]
    public void EmitUpsert_RefusesAScopedMySqlTable()
    {
        var table = new TableSchema { Name = "orders" };
        table.Columns["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true };
        table.Columns["tenant_id"] = new ColumnSchema { Name = "tenant_id", DbType = "int" };
        table.Columns["note"] = new ColumnSchema { Name = "note", DbType = "varchar" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CodeEmitter.EmitUpsert("Orders", table, "mysql", null, new List<ColumnSchema> { table.Columns["tenant_id"] }));
        Assert.Equal("Table 'orders' is scoped, and MySQL has no upsert form that leaves another scope's row alone.", ex.Message);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void BulkInsert_OfAScopedTable_IsPublicOnlyThroughTheScope(string dialect)
    {
        var bulk = Source(Generate(OrdersOnly, dialect), "Orders.BulkInsert.auto.g.cs");

        Assert.Contains("private static int __BulkInsertUnscoped(DbConnection conn, IEnumerable<Order> rows, DbTransaction? transaction = null)", bulk);
        Assert.Contains("private async Task<int> __BulkInsertUnscopedAsync(IEnumerable<Order> rows, CancellationToken cancellationToken = default)", bulk);
        Assert.Contains("public int BulkInsert(int tenant_id, IEnumerable<Order> rows) => __BulkInsertUnscoped(__ScopeRows(rows, tenant_id));", bulk);
        Assert.Contains("public static int BulkInsert(DbConnection conn, int tenant_id, IEnumerable<Order> rows, DbTransaction? transaction = null) => __BulkInsertUnscoped(conn, __ScopeRows(rows, tenant_id), transaction);", bulk);
        Assert.Contains("public Task<int> BulkInsertAsync(int tenant_id, IEnumerable<Order> rows, CancellationToken cancellationToken = default) => __BulkInsertUnscopedAsync(__ScopeRows(rows, tenant_id), cancellationToken);", bulk);
        Assert.Contains("public static Task<int> BulkInsertAsync(DbConnection conn, int tenant_id, IEnumerable<Order> rows, DbTransaction? transaction = null, CancellationToken cancellationToken = default) => __BulkInsertUnscopedAsync(conn, __ScopeRows(rows, tenant_id), transaction, cancellationToken);", bulk);
        Assert.Contains("private static IEnumerable<Order> __ScopeRows(IEnumerable<Order> rows, int tenant_id)", bulk);
        Assert.Contains("row.TenantId = tenant_id;", bulk);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void BulkInsert_OfAnUnscopedTable_IsUnchanged(string dialect)
    {
        var bulk = Source(Generate(OrdersOnly, dialect), "Customers.BulkInsert.auto.g.cs");

        Assert.Contains("public static int BulkInsert(DbConnection conn, IEnumerable<Customer> rows, DbTransaction? transaction = null)", bulk);
        Assert.DoesNotContain("__BulkInsertUnscoped", bulk);
        Assert.DoesNotContain("__ScopeRows", bulk);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    public void AnEmptyScopeFile_GeneratesWhatNoScopeFileDoes(string dialect)
    {
        Assert.Equal(Sources(Generate(null, dialect)), Sources(Generate(@"{ ""scopes"": [] }", dialect)));
    }
}
