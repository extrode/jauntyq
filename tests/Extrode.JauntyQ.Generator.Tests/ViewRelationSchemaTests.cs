using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 T1: the <c>isView</c> flag on the schema model, and the one thing
/// that must not regress with it — a snapshot written before views existed has
/// no <c>isView</c> key at all, and every table in it must still deserialize as
/// an insertable base table.
///
/// This is why insertability is derived from <c>IsView</c> rather than stored:
/// a stored <c>isInsertable</c> would default to false on exactly these
/// snapshots and silently make every existing table read-only.
/// </summary>
public class ViewRelationSchemaTests
{
    // A snapshot in the pre-015 shape: no isView key anywhere.
    private const string LegacySnapshot = """
    {
      "dialect": "postgres",
      "tables": {
        "users": {
          "name": "users",
          "columns": {
            "id": { "name": "id", "dbType": "integer", "isPrimaryKey": true },
            "email": { "name": "email", "dbType": "text" }
          },
          "indexes": [
            { "name": "users_pkey", "columns": ["id"], "isUnique": true }
          ]
        }
      }
    }
    """;

    [Fact]
    public void LegacySnapshot_WithNoIsViewKey_LoadsEveryTableAsInsertable()
    {
        var schema = SchemaLoader.Load(LegacySnapshot);

        var users = schema.Tables["users"];
        Assert.False(users.IsView);
        Assert.True(users.IsInsertable);
    }

    [Fact]
    public void ViewFlag_RoundTrips_ThroughSerializeAndLoad()
    {
        var schema = SchemaLoader.Load(LegacySnapshot);
        schema.Tables["v_active_users"] = new TableSchema
        {
            Name = "v_active_users",
            IsView = true,
            Columns = { ["id"] = new ColumnSchema { Name = "id", DbType = "integer" } },
        };

        var reloaded = SchemaLoader.Load(SchemaLoader.Serialize(schema));

        Assert.True(reloaded.Tables["v_active_users"].IsView);
        Assert.False(reloaded.Tables["v_active_users"].IsInsertable);

        // The base table alongside it is unaffected.
        Assert.False(reloaded.Tables["users"].IsView);
        Assert.True(reloaded.Tables["users"].IsInsertable);
    }

    [Fact]
    public void IsInsertable_IsAlwaysTheNegationOfIsView()
    {
        Assert.True(new TableSchema().IsInsertable);
        Assert.False(new TableSchema { IsView = true }.IsInsertable);
    }
}
