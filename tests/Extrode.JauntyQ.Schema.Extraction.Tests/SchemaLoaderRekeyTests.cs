using System.Linq;
using System.Text.Json;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

[Trait("Category", "AuditRegression")]
public class SchemaLoaderRekeyTests
{
    [Fact]
    public void TableAndColumnKeys_DifferingFromTheirNames_AreRekeyedByName()
    {
        var schema = SchemaLoader.Load(@"{ ""tables"": { ""Customers"": { ""name"": ""customers"", ""columns"": {
            ""ID"": { ""name"": ""id"", ""dbType"": ""int"" }, ""note"": { ""name"": ""note"", ""dbType"": ""text"" } } } } }");

        Assert.Equal(new[] { "customers" }, schema.Tables.Keys.ToArray());
        Assert.Equal(new[] { "id", "note" }, schema.Tables["customers"].Columns.Keys.ToArray());
    }

    [Fact]
    public void ProcedureSequenceAndEnumKeys_AreRekeyedByName()
    {
        var schema = SchemaLoader.Load(@"{ ""procedures"": { ""P"": { ""name"": ""p"" } },
            ""sequences"": { ""S"": { ""name"": ""s"" } }, ""enums"": { ""E"": { ""name"": ""e"" } } }");

        Assert.Equal(new[] { "p" }, schema.Procedures.Keys.ToArray());
        Assert.Equal(new[] { "s" }, schema.Sequences.Keys.ToArray());
        Assert.Equal(new[] { "e" }, schema.Enums.Keys.ToArray());
    }

    [Fact]
    public void AnEntryWithNoName_TakesItsKeyAsItsName()
    {
        var schema = SchemaLoader.Load(@"{ ""tables"": { ""orders"": { ""columns"": { ""id"": { ""dbType"": ""int"" } } } } }");

        Assert.Equal("orders", schema.Tables["orders"].Name);
        Assert.Equal("id", schema.Tables["orders"].Columns["id"].Name);
    }

    [Fact]
    public void FunctionAndUserTypeKeys_AreKept()
    {
        var schema = SchemaLoader.Load(@"{ ""functions"": { ""calc(int)"": { ""name"": ""calc"" } },
            ""userTypes"": { ""dbo.money_amount"": { ""name"": ""money_amount"" } } }");

        Assert.Equal(new[] { "calc(int)" }, schema.Functions.Keys.ToArray());
        Assert.Equal(new[] { "dbo.money_amount" }, schema.UserTypes.Keys.ToArray());
    }

    [Fact]
    public void TwoTablesWithTheSameName_AreAParseFailure()
    {
        var ex = Assert.Throws<JsonException>(() => SchemaLoader.Load(
            @"{ ""tables"": { ""Customers"": { ""name"": ""customers"" }, ""customers"": { ""name"": ""customers"" } } }"));

        Assert.Equal("schema snapshot has two entries named 'customers' (table)", ex.Message);
    }

    [Fact]
    public void TwoColumnsWithTheSameName_NameTheirTable()
    {
        var ex = Assert.Throws<JsonException>(() => SchemaLoader.Load(
            @"{ ""tables"": { ""t"": { ""name"": ""t"", ""columns"": { ""A"": { ""name"": ""a"" }, ""a"": { ""name"": ""a"" } } } } }"));

        Assert.Equal("schema snapshot has two entries named 'a' (column in table 't')", ex.Message);
    }
}
