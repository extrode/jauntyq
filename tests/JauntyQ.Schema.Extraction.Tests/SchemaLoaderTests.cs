using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

public class SchemaLoaderTests
{
    private const string NorthwindJson = @"{
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""varchar"", ""isNullable"": false },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true }
      }
    },
    ""categories"": {
      ""name"": ""categories"",
      ""columns"": {
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""category_name"": { ""name"": ""category_name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""products"", ""fromColumn"": ""category_id"", ""toTable"": ""categories"", ""toColumn"": ""category_id"" }
  ]
}";

    [Fact]
    public void Load_ValidJson_ReturnsSchema()
    {
        var schema = SchemaLoader.Load(NorthwindJson);

        Assert.Equal(2, schema.Tables.Count);
        Assert.True(schema.Tables.ContainsKey("products"));
        Assert.True(schema.Tables.ContainsKey("categories"));
    }

    [Fact]
    public void Load_TablesHaveCorrectColumns()
    {
        var schema = SchemaLoader.Load(NorthwindJson);
        var products = schema.Tables["products"];

        Assert.Equal("products", products.Name);
        Assert.Equal(3, products.Columns.Count);
        Assert.True(products.Columns.ContainsKey("product_id"));
        Assert.True(products.Columns.ContainsKey("product_name"));
        Assert.True(products.Columns.ContainsKey("category_id"));
    }

    [Fact]
    public void Load_ColumnTypesCorrect()
    {
        var schema = SchemaLoader.Load(NorthwindJson);
        var col = schema.Tables["products"].Columns["product_id"];

        Assert.Equal("product_id", col.Name);
        Assert.Equal("int", col.DbType);
        Assert.False(col.IsNullable);
    }

    [Fact]
    public void Load_NullableColumnDetected()
    {
        var schema = SchemaLoader.Load(NorthwindJson);
        var col = schema.Tables["products"].Columns["category_id"];

        Assert.True(col.IsNullable);
    }

    [Fact]
    public void Load_ForeignKeysExtracted()
    {
        var schema = SchemaLoader.Load(NorthwindJson);

        Assert.Single(schema.ForeignKeys);
        Assert.Equal("products", schema.ForeignKeys[0].FromTable);
        Assert.Equal("category_id", schema.ForeignKeys[0].FromColumn);
        Assert.Equal("categories", schema.ForeignKeys[0].ToTable);
        Assert.Equal("category_id", schema.ForeignKeys[0].ToColumn);
    }

    [Fact]
    public void RoundTrip_SerializeDeserialize_Preserves()
    {
        var schema = SchemaLoader.Load(NorthwindJson);
        var json = SchemaLoader.Serialize(schema);
        var roundTripped = SchemaLoader.Load(json);

        Assert.Equal(schema.Tables.Count, roundTripped.Tables.Count);
        Assert.Equal(schema.ForeignKeys.Count, roundTripped.ForeignKeys.Count);
        Assert.Equal(
            schema.Tables["products"].Columns.Count,
            roundTripped.Tables["products"].Columns.Count);
    }

    [Fact]
    public void Load_EmptyJson_ReturnsEmptySchema()
    {
        var schema = SchemaLoader.Load("{}");

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.ForeignKeys);
        Assert.Empty(schema.Procedures);
    }

    [Fact]
    public void Load_NullLiteral_ThrowsJsonException_NotSilentEmptySchema()
    {
        // T2 hardening (2026-07-31): `null` used to yield an empty schema,
        // silently indistinguishable from a database with no tables.
        Assert.Throws<System.Text.Json.JsonException>(() => SchemaLoader.Load("null"));
    }

    [Fact]
    public void Load_MalformedJson_ThrowsJsonException()
    {
        // Pins the pre-existing malformed path the null case now joins.
        Assert.Throws<System.Text.Json.JsonException>(() => SchemaLoader.Load("{not json"));
    }

    private const string ProcJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {},
  ""procedures"": {
    ""GetOrdersByCustomer"": {
      ""name"": ""GetOrdersByCustomer"",
      ""params"": [ { ""name"": ""CustomerId"", ""dbType"": ""nchar"", ""direction"": ""In"", ""isNullable"": false, ""maxLength"": 5 } ],
      ""results"": [ { ""name"": ""OrderId"", ""dbType"": ""int"", ""isNullable"": false } ]
    },
    ""ArchiveCustomer"": {
      ""name"": ""ArchiveCustomer"",
      ""params"": [ { ""name"": ""Count"", ""dbType"": ""int"", ""direction"": ""Out"", ""isNullable"": false } ],
      ""results"": []
    }
  }
}";

    [Fact]
    public void Load_Procedures_ParsedWithParamsAndResults()
    {
        var schema = SchemaLoader.Load(ProcJson);

        Assert.Equal(2, schema.Procedures.Count);
        var p = schema.Procedures["GetOrdersByCustomer"];
        Assert.Single(p.Params);
        Assert.Equal("CustomerId", p.Params[0].Name);
        Assert.Equal(ProcedureParamDirection.In, p.Params[0].Direction);
        Assert.Single(p.Results);
        Assert.Equal("OrderId", p.Results[0].Name);

        Assert.Equal(ProcedureParamDirection.Out,
            schema.Procedures["ArchiveCustomer"].Params[0].Direction);
    }

    [Fact]
    public void RoundTrip_Procedures_PreservesDirectionsAsStrings()
    {
        var schema = SchemaLoader.Load(ProcJson);
        var json = SchemaLoader.Serialize(schema);

        // Directions serialize as strings, not integers.
        Assert.Contains("\"In\"", json);
        Assert.Contains("\"Out\"", json);

        var roundTripped = SchemaLoader.Load(json);
        Assert.Equal(ProcedureParamDirection.Out,
            roundTripped.Procedures["ArchiveCustomer"].Params[0].Direction);
        Assert.Single(roundTripped.Procedures["GetOrdersByCustomer"].Results);
    }
}
