using Extrode.JauntyQ.Analysis.Scoping;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests.Scoping;

public class ScopeResolverTests
{
    private static DatabaseSchema Schema()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["Orders"] = new TableSchema
        {
            Name = "Orders",
            Columns =
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["Tenant_Id"] = new ColumnSchema { Name = "Tenant_Id", DbType = "int" },
                ["owner_id"] = new ColumnSchema { Name = "owner_id", DbType = "int" },
                ["seq"] = new ColumnSchema { Name = "seq", DbType = "int", IsIdentity = true },
                ["total"] = new ColumnSchema { Name = "total", DbType = "int", IsComputed = true },
                ["ver"] = new ColumnSchema { Name = "ver", DbType = "rowversion", IsRowVersion = true },
            },
        };
        return schema;
    }

    private static (List<ScopeColumn> Scopes, List<string> Problems) Resolve(params ScopeEntry[] entries)
    {
        var file = new ScopeFile();
        file.Scopes.AddRange(entries);
        var problems = new List<string>();
        var scopes = ScopeResolver.Resolve(file, Schema(), problems);
        return (scopes, problems);
    }

    [Fact]
    public void Entries_MatchWithoutRegardToCase_AndTakeTheSchemaSpelling()
    {
        var (scopes, problems) = Resolve(
            new ScopeEntry { Table = "orders", Column = "tenant_id" },
            new ScopeEntry { Table = "ORDERS", Column = "OWNER_ID" });

        Assert.Empty(problems);
        Assert.Equal(new[] { "Orders.Tenant_Id", "Orders.owner_id" }, scopes.ConvertAll(s => s.Table + "." + s.Column));
    }

    [Fact]
    public void AMissingField_IsDropped()
    {
        var (scopes, problems) = Resolve(
            new ScopeEntry { Table = "orders" },
            new ScopeEntry { Column = "tenant_id" },
            new ScopeEntry { Table = " ", Column = "tenant_id" });

        Assert.Empty(scopes);
        Assert.Equal(new[]
        {
            "Scope entry 1 needs both \"table\" and \"column\". It was dropped, so table 'orders' is unscoped by it.",
            "Scope entry 2 needs both \"table\" and \"column\". It was dropped, so table '(no table)' is unscoped by it.",
            "Scope entry 3 needs both \"table\" and \"column\". It was dropped, so table '(no table)' is unscoped by it.",
        }, problems);
    }

    [Fact]
    public void AnUnknownTableOrColumn_IsDropped()
    {
        var (scopes, problems) = Resolve(
            new ScopeEntry { Table = "invoices", Column = "tenant_id" },
            new ScopeEntry { Table = "orders", Column = "org_id" });

        Assert.Empty(scopes);
        Assert.Equal(new[]
        {
            "Scope entry 1 names table 'invoices', which is not in the schema. It was dropped, so table 'invoices' is unscoped.",
            "Scope entry 2 names column 'org_id', which table 'Orders' does not have. It was dropped, so table 'Orders' is unscoped by it.",
        }, problems);
    }

    [Theory]
    [InlineData("id", "is part of the primary key")]
    [InlineData("seq", "is an identity column")]
    [InlineData("total", "is computed")]
    [InlineData("ver", "is a rowversion column")]
    public void AColumnThatCannotBeBound_IsDropped(string column, string why)
    {
        var (scopes, problems) = Resolve(new ScopeEntry { Table = "orders", Column = column });

        Assert.Empty(scopes);
        Assert.Equal(
            $"Scope entry 1: column '{column}' of table 'Orders' {why}, so it cannot be bound from a scope parameter. It was dropped, so table 'Orders' is unscoped by it.",
            Assert.Single(problems));
    }

    [Fact]
    public void ARepeat_IsDropped_AndTheFirstStillScopes()
    {
        var (scopes, problems) = Resolve(
            new ScopeEntry { Table = "orders", Column = "tenant_id" },
            new ScopeEntry { Table = "Orders", Column = "TENANT_ID" });

        Assert.Equal("Orders", Assert.Single(scopes).Table);
        Assert.Equal(
            "Scope entry 2 repeats table 'Orders', column 'Tenant_Id'. The repeat was dropped; the first entry still scopes the table.",
            Assert.Single(problems));
    }

    [Fact]
    public void AnEmptyFile_IsReported()
    {
        var (scopes, problems) = Resolve();

        Assert.Empty(scopes);
        Assert.Equal("The scope file declares no entries under \"scopes\", so every table is unscoped.", Assert.Single(problems));
    }

    [Fact]
    public void ANullEntry_IsDropped_AndLaterEntriesStillScope()
    {
        var (scopes, problems) = Resolve(null!, new ScopeEntry { Table = "orders", Column = "tenant_id" });

        Assert.Equal("Tenant_Id", Assert.Single(scopes).Column);
        Assert.Equal("Scope entry 1 is null. It was dropped, so it scopes no table.", Assert.Single(problems));
    }

    [Fact]
    public void TableAndColumnNamesThatConcatenateAlike_AreNotARepeat()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Tables["ab"] = new TableSchema { Name = "ab", Columns = { ["c"] = new ColumnSchema { Name = "c", DbType = "int" } } };
        schema.Tables["a"] = new TableSchema { Name = "a", Columns = { ["bc"] = new ColumnSchema { Name = "bc", DbType = "int" } } };
        var file = new ScopeFile();
        file.Scopes.Add(new ScopeEntry { Table = "ab", Column = "c" });
        file.Scopes.Add(new ScopeEntry { Table = "a", Column = "bc" });
        var problems = new List<string>();

        var scopes = ScopeResolver.Resolve(file, schema, problems);

        Assert.Empty(problems);
        Assert.Equal(2, scopes.Count);
    }
}
