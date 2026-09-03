using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Spec 013 / T1: the snapshot model for database enums. These cover the JSON
/// contract itself — that an enum survives a round trip verbatim, and that a
/// snapshot written before spec 013 still loads unchanged. The serializer is
/// source-generated (<c>SchemaJsonContext</c>) and is read at app runtime
/// under Native AOT, so a type missing from the context fails at run time
/// rather than build time; that is what the round trip actually guards.
/// </summary>
public class EnumSchemaModelTests
{
    private static DatabaseSchema BuildSchemaWithEnum()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Enums["order_status"] = new EnumSchema
        {
            Name = "order_status",
            Members =
            {
                new EnumMember { Value = "pending", CSharpName = "Pending" },
                new EnumMember { Value = "shipped", CSharpName = "Shipped" },
                new EnumMember { Value = "canceled", CSharpName = "Canceled" }
            }
        };
        schema.Tables["orders"] = new TableSchema
        {
            Name = "orders",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true },
                ["status"] = new ColumnSchema { Name = "status", DbType = "order_status", EnumName = "order_status" },
                ["note"] = new ColumnSchema { Name = "note", DbType = "text", IsNullable = true }
            }
        };
        return schema;
    }

    [Fact]
    public void EnumSurvivesTheRoundTripWithItsMembersInOrder()
    {
        string json = SchemaLoader.Serialize(BuildSchemaWithEnum());
        var reloaded = SchemaLoader.Load(json);

        Assert.True(reloaded.Enums.ContainsKey("order_status"));
        var members = reloaded.Enums["order_status"].Members;
        Assert.Equal(new[] { "pending", "shipped", "canceled" }, members.Select(m => m.Value));
        Assert.Equal(new[] { "Pending", "Shipped", "Canceled" }, members.Select(m => m.CSharpName));
        Assert.Equal("order_status", reloaded.Tables["orders"].Columns["status"].EnumName);
    }

    [Fact]
    public void MemberValuesAreCarriedVerbatimNotNormalized()
    {
        // FR-003. MySQL permits all of these, and every one of them would be
        // destroyed by a serializer that trimmed, cased or re-escaped values.
        var schema = new DatabaseSchema { Dialect = "mysql" };
        schema.Enums["OddV"] = new EnumSchema
        {
            Name = "OddV",
            Members =
            {
                new EnumMember { Value = "a,b", CSharpName = "Ab" },
                new EnumMember { Value = "it's", CSharpName = "Its" },
                new EnumMember { Value = "", CSharpName = "_" },
                new EnumMember { Value = "in-progress", CSharpName = "InProgress" },
                new EnumMember { Value = "  padded  ", CSharpName = "Padded" }
            }
        };

        var reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema));

        Assert.Equal(
            new[] { "a,b", "it's", "", "in-progress", "  padded  " },
            reloaded.Enums["OddV"].Members.Select(m => m.Value));
    }

    [Fact]
    public void ColumnWithNoEnumOmitsTheKeyEntirelyFromTheJson()
    {
        // The property is JsonIgnore-WhenWritingNull, so a snapshot of a
        // database with no enums must be byte-identical to what the previous
        // version of JauntyQ wrote (SC-006 at the snapshot layer).
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        schema.Tables["widgets"] = new TableSchema
        {
            Name = "widgets",
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "integer", IsPrimaryKey = true }
            }
        };

        string json = SchemaLoader.Serialize(schema);

        Assert.DoesNotContain("enumName", json);
    }

    [Fact]
    public void SnapshotWrittenBeforeSpec013StillLoads()
    {
        // FR-012. Literal pre-013 JSON: no "enums" key, no "enumName" on the
        // column. Hand-written rather than generated, so it cannot silently
        // acquire the new shape.
        const string legacy = """
        {
          "dialect": "postgres",
          "tables": {
            "orders": {
              "name": "orders",
              "columns": {
                "id": { "name": "id", "dbType": "int", "isNullable": false, "isPrimaryKey": true,
                        "isIdentity": true, "isRowVersion": false, "isComputed": false },
                "status": { "name": "status", "dbType": "order_status", "isNullable": false,
                            "isPrimaryKey": false, "isIdentity": false, "isRowVersion": false,
                            "isComputed": false }
              }
            }
          },
          "foreignKeys": [],
          "procedures": {},
          "sequences": {}
        }
        """;

        var schema = SchemaLoader.Load(legacy);

        Assert.Empty(schema.Enums);
        Assert.Null(schema.Tables["orders"].Columns["status"].EnumName);
        Assert.Equal("order_status", schema.Tables["orders"].Columns["status"].DbType);
    }
}
