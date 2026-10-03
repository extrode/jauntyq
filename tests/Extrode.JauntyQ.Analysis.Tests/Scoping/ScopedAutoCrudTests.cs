using Extrode.JauntyQ.Analysis.Scoping;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests.Scoping;

public class ScopedAutoCrudTests
{
    private static ColumnSchema Col(string name, bool pk = false) =>
        new() { Name = name, DbType = "int", IsPrimaryKey = pk };

    private static DatabaseSchema Schema()
    {
        var orders = new TableSchema { Name = "orders" };
        foreach (var c in new[] { Col("id", pk: true), Col("note"), Col("tenant_id"), Col("customer_id"), Col("region") })
            orders.Columns[c.Name] = c;
        var tags = new TableSchema { Name = "tags" };
        foreach (var c in new[] { Col("id", pk: true), Col("label") })
            tags.Columns[c.Name] = c;
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["orders"] = orders;
        schema.Tables["tags"] = tags;
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "customer_id", ToTable = "customers", ToColumn = "id" });
        schema.ForeignKeys.Add(new ForeignKeySchema { FromTable = "orders", FromColumn = "tenant_id", ToTable = "tenants", ToColumn = "id" });
        return schema;
    }

    private static readonly ScopeColumn[] Tenant = { new("orders", "tenant_id") };
    private static readonly ScopeColumn[] RegionThenTenant = { new("ORDERS", "Region"), new("orders", "tenant_id") };

    private static string Sql(IReadOnlyList<ScopeColumn>? scopes, string table, string method) =>
        AutoCrud.Synthesize(Schema(), scopes).Single(q => q.TableName == table && q.MethodName == method).Sql;

    [Fact]
    public void GetAll_FiltersOnTheScope()
    {
        Assert.Equal(
            "SELECT id, note, tenant_id, customer_id, region\nFROM orders\nWHERE orders.tenant_id = @tenant_id",
            Sql(Tenant, "orders", "GetAll"));
    }

    [Fact]
    public void GetAll_FiltersOnEveryScope_InScopeFileOrder()
    {
        Assert.Equal(
            "SELECT id, note, tenant_id, customer_id, region\nFROM orders\nWHERE orders.region = @region AND orders.tenant_id = @tenant_id",
            Sql(RegionThenTenant, "orders", "GetAll"));
    }

    [Fact]
    public void GetById_PutsTheScopeBeforeTheKey()
    {
        Assert.Equal(
            "-- @first\nSELECT id, note, tenant_id, customer_id, region\nFROM orders\nWHERE orders.tenant_id = @tenant_id AND orders.id = @id",
            Sql(Tenant, "orders", "GetById"));
    }

    [Fact]
    public void ForeignKeyLoader_PutsTheScopeBeforeTheForeignKey()
    {
        Assert.Equal(
            "SELECT id, note, tenant_id, customer_id, region\nFROM orders\nWHERE orders.region = @region AND orders.tenant_id = @tenant_id AND orders.customer_id = @customer_id",
            Sql(RegionThenTenant, "orders", "GetByCustomerId"));
    }

    [Fact]
    public void ForeignKeyLoader_IsNotSynthesized_OnTheScopeColumn()
    {
        var queries = AutoCrud.Synthesize(Schema(), Tenant);

        Assert.DoesNotContain(queries, q => q.MethodName == "GetByTenantId");
        Assert.Contains(AutoCrud.Synthesize(Schema(), null), q => q.MethodName == "GetByTenantId");
    }

    [Fact]
    public void Insert_ListsTheScopeFirst()
    {
        Assert.Equal(
            "INSERT INTO orders (region, tenant_id, id, note, customer_id)\nVALUES (@region, @tenant_id, @id, @note, @customer_id)",
            Sql(RegionThenTenant, "orders", "Insert"));
    }

    [Fact]
    public void Update_LeavesTheScopeOutOfSet_AndFiltersOnItFirst()
    {
        Assert.Equal(
            "UPDATE orders\nSET note = @note, customer_id = @customer_id, region = @region\nWHERE tenant_id = @tenant_id AND id = @id",
            Sql(Tenant, "orders", "Update"));
    }

    [Fact]
    public void Delete_FiltersOnTheScopeFirst()
    {
        Assert.Equal(
            "DELETE FROM orders\nWHERE region = @region AND tenant_id = @tenant_id AND id = @id",
            Sql(RegionThenTenant, "orders", "Delete"));
    }

    [Fact]
    public void AnUnscopedTable_IsUnchanged()
    {
        var scoped = AutoCrud.Synthesize(Schema(), Tenant).Where(q => q.TableName == "tags").Select(q => q.Sql);
        var plain = AutoCrud.Synthesize(Schema()).Where(q => q.TableName == "tags").Select(q => q.Sql);

        Assert.Equal(plain, scoped);
    }

    [Fact]
    public void NoScopes_IsTheUnscopedSynthesis()
    {
        var plain = AutoCrud.Synthesize(Schema()).Select(q => q.Sql);

        Assert.Equal(plain, AutoCrud.Synthesize(Schema(), Array.Empty<ScopeColumn>()).Select(q => q.Sql));
        Assert.Equal("SELECT id, note, tenant_id, customer_id, region\nFROM orders", Sql(null, "orders", "GetAll"));
    }

    [Fact]
    public void ScopeFirst_MovesOnlyColumnsInTheList()
    {
        var a = Col("a");
        var b = Col("b");
        var s = Col("s");

        Assert.Equal(new[] { a, b }, AutoCrud.ScopeFirst(new List<ColumnSchema> { a, b }, new List<ColumnSchema> { s }));
        Assert.Equal(new[] { b, a }, AutoCrud.ScopeFirst(new List<ColumnSchema> { a, b }, new List<ColumnSchema> { s, b }));
    }

    [Fact]
    public void ColumnsOf_MatchesCaseInsensitively_AndSkipsOtherTables()
    {
        var orders = Schema().Tables["orders"];

        var cols = ScopeResolver.ColumnsOf(orders, new[] { new ScopeColumn("tags", "id"), new ScopeColumn("Orders", "TENANT_ID"), new ScopeColumn("orders", "missing") });

        Assert.Equal("tenant_id", Assert.Single(cols).Name);
        Assert.Empty(ScopeResolver.ColumnsOf(orders, null));
    }
}
