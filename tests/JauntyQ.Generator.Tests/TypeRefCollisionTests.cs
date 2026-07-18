using System.Collections.Generic;
using JauntyQ.Generator;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// CodeEmitter.TypeRef exists because entity accessor class names come from
/// DialectMapper.ToPascalCase(table.Name) on a fully user-controlled table
/// name, and are declared as `public partial class {name}` directly in
/// `namespace JauntyQ.Generated`. A table named "list"/"task"/"system"
/// therefore shadows the BCL name of the same simple name for EVERY file
/// compiled into that namespace -- not merely the file generated for that
/// one entity (a plain "does this file's own entity collide" check would
/// miss the cross-file case entirely, which is exactly the bug this helper
/// closes: today's code already emits bare `System.Threading.Tasks.Task`,
/// so a table named "system" already breaks the build before any generated
/// file even mentions the word "system").
/// </summary>
public class TypeRefCollisionTests
{
    private static DatabaseSchema SchemaWithTable(string tableName)
    {
        var schema = new DatabaseSchema();
        schema.Tables[tableName] = new TableSchema
        {
            Name = tableName,
            Columns = new Dictionary<string, ColumnSchema>
            {
                ["id"] = new ColumnSchema { Name = "id", DbType = "int", IsPrimaryKey = true }
            }
        };
        return schema;
    }

    [Fact]
    public void TypeRef_NoCollision_ReturnsShortName()
    {
        var schema = SchemaWithTable("widgets");
        Assert.Equal("List", CodeEmitter.TypeRef(schema, "List", "System.Collections.Generic"));
    }

    [Fact]
    public void TypeRef_NullSchema_ReturnsShortName()
    {
        Assert.Equal("Task", CodeEmitter.TypeRef(null, "Task", "System.Threading.Tasks"));
    }

    [Theory]
    [InlineData("list", "List", "System.Collections.Generic")]
    [InlineData("task", "Task", "System.Threading.Tasks")]
    [InlineData("system", "System", "")]
    [InlineData("dictionary", "Dictionary", "System.Collections.Generic")]
    public void TypeRef_OwnEntityCollision_ReturnsGlobalQualified(string tableName, string simpleName, string ns)
    {
        var schema = SchemaWithTable(tableName);
        string expected = ns.Length == 0 ? $"global::{simpleName}" : $"global::{ns}.{simpleName}";
        Assert.Equal(expected, CodeEmitter.TypeRef(schema, simpleName, ns));
    }

    [Fact]
    public void TypeRef_CrossFileCollision_UnrelatedEntityStillGetsGlobalQualifiedReference()
    {
        // The bug a per-file-only check would miss: "task" collides
        // somewhere in the schema, so an UNRELATED entity's own reference to
        // System.Threading.Tasks.Task must still fall back to global:: --
        // it doesn't matter that this particular reference isn't "for" the
        // Task entity, because both classes land in the same namespace.
        var schema = SchemaWithTable("task");
        Assert.Equal("global::System.Threading.Tasks.Task", CodeEmitter.TypeRef(schema, "Task", "System.Threading.Tasks"));
    }

    [Fact]
    public void TypeRef_CaseDifference_DoesNotFalsePositive()
    {
        // "Widgets" (PascalCase already) does not collide with "list" -- only
        // an exact, ordinal simple-name match counts as a real collision.
        var schema = SchemaWithTable("widgets");
        Assert.Equal("Dictionary", CodeEmitter.TypeRef(schema, "Dictionary", "System.Collections.Generic"));
    }
}
