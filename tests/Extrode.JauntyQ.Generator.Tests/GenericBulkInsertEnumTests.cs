using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class GenericBulkInsertEnumTests
{
    [Fact]
    public void EnumColumns_BindAsWireStrings()
    {
        var schema = new DatabaseSchema { Dialect = "sqlite" };
        var mood = new EnumSchema { Name = "mood" };
        mood.Members.Add(new EnumMember { Value = "ok", CSharpName = "Ok" });
        schema.Enums["mood"] = mood;
        var table = new TableSchema { Name = "people" };
        table.Columns["now"] = new ColumnSchema { Name = "now", DbType = "text", EnumName = "mood" };
        table.Columns["was"] = new ColumnSchema { Name = "was", DbType = "text", EnumName = "mood", IsNullable = true };
        schema.Tables["people"] = table;

        string code = CodeEmitter.EmitBulkInsert("People", "Person", table, "sqlite", schema);

        Assert.Contains("p0.DbType = DbType.String;", code);
        Assert.Contains("p1.DbType = DbType.String;", code);
        Assert.Contains("p0.Value = MoodValues.ToWire(row.Now);", code);
        Assert.Contains("p1.Value = row.Was is null ? (object)DBNull.Value : MoodValues.ToWire(row.Was.Value);", code);
    }
}
