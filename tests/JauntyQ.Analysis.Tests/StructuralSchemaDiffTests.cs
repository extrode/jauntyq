using JauntyQ.Analysis.Diff;
using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Analysis.Tests;

public class StructuralSchemaDiffTests
{
    private static DatabaseSchema Schema(params TableSchema[] tables)
    {
        var s = new DatabaseSchema { Dialect = "sqlserver" };
        foreach (var t in tables)
            s.Tables[t.Name] = t;
        return s;
    }

    private static TableSchema Table(string name, params ColumnSchema[] cols)
    {
        var t = new TableSchema { Name = name };
        foreach (var c in cols)
            t.Columns[c.Name] = c;
        return t;
    }

    private static ColumnSchema Col(string name, string dbType = "int", bool nullable = false) =>
        new() { Name = name, DbType = dbType, IsNullable = nullable };

    [Fact]
    public void NoChange_EmptyDelta()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar")));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar")));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        Assert.True(delta.IsEmpty);
    }

    [Fact]
    public void RemovedTable_Tracked()
    {
        var baseline = Schema(Table("users", Col("id")), Table("legacy", Col("id")));
        var effective = Schema(Table("users", Col("id")));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        Assert.Equal(new[] { "legacy" }, delta.RemovedTables);
        Assert.True(delta.IsTableRemoved("LEGACY"));
        Assert.Empty(delta.AddedTables);
    }

    [Fact]
    public void AddedTable_Tracked()
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users", Col("id")), Table("audit", Col("id")));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        Assert.Equal(new[] { "audit" }, delta.AddedTables);
        Assert.Empty(delta.RemovedTables);
    }

    [Fact]
    public void RemovedColumn_Tracked()
    {
        var baseline = Schema(Table("users", Col("id"), Col("legacy_flag", "bit")));
        var effective = Schema(Table("users", Col("id")));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        var t = Assert.Single(delta.ModifiedTables);
        Assert.Equal(new[] { "legacy_flag" }, t.RemovedColumns);
        Assert.True(delta.IsColumnRemoved("users", "LEGACY_FLAG"));
    }

    [Fact]
    public void AddedColumn_Tracked()
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users", Col("id"), Col("created_at", "datetime")));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        var t = Assert.Single(delta.ModifiedTables);
        Assert.Equal(new[] { "created_at" }, t.AddedColumns);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("nullability")]
    [InlineData("maxlength")]
    [InlineData("precisionscale")]
    [InlineData("unicode")]
    [InlineData("primarykey")]
    [InlineData("identity")]
    [InlineData("rowversion")]
    [InlineData("computed")]
    public void ModifiedColumn_ChangeKindDetected(string kind)
    {
        var before = new ColumnSchema { Name = "c", DbType = "varchar", MaxLength = 100, Precision = 10, Scale = 2 };
        var after = new ColumnSchema { Name = "c", DbType = "varchar", MaxLength = 100, Precision = 10, Scale = 2 };

        ColumnChangeKind expected;
        switch (kind)
        {
            case "type": after.DbType = "int"; expected = ColumnChangeKind.Type; break;
            case "nullability": after.IsNullable = true; expected = ColumnChangeKind.Nullability; break;
            case "maxlength": after.MaxLength = 50; expected = ColumnChangeKind.MaxLength; break;
            case "precisionscale": after.Scale = 4; expected = ColumnChangeKind.PrecisionScale; break;
            case "unicode": after.IsUnicode = true; expected = ColumnChangeKind.Unicode; break;
            case "primarykey": after.IsPrimaryKey = true; expected = ColumnChangeKind.PrimaryKey; break;
            case "identity": after.IsIdentity = true; expected = ColumnChangeKind.Identity; break;
            case "computed": after.IsComputed = true; expected = ColumnChangeKind.Computed; break;
            default: after.IsRowVersion = true; expected = ColumnChangeKind.RowVersion; break;
        }

        var delta = StructuralSchemaDiff.Compute(
            Schema(Table("t", before)),
            Schema(Table("t", after)));

        var change = delta.FindColumnChange("t", "c");
        Assert.NotNull(change);
        Assert.Contains(expected, change!.Kinds);
    }

    [Fact]
    public void ModifiedColumn_MultipleKinds()
    {
        var baseline = Schema(Table("users", new ColumnSchema { Name = "name", DbType = "varchar", MaxLength = 100 }));
        var effective = Schema(Table("users", new ColumnSchema { Name = "name", DbType = "nvarchar", MaxLength = 50, IsNullable = true }));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        var change = delta.FindColumnChange("users", "name");
        Assert.NotNull(change);
        Assert.Contains(ColumnChangeKind.Type, change!.Kinds);
        Assert.Contains(ColumnChangeKind.MaxLength, change.Kinds);
        Assert.Contains(ColumnChangeKind.Nullability, change.Kinds);
    }

    [Fact]
    public void UnchangedColumn_NoLookupHit()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar")));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", nullable: true)));

        var delta = StructuralSchemaDiff.Compute(baseline, effective);

        Assert.Null(delta.FindColumnChange("users", "id"));
        Assert.False(delta.IsColumnRemoved("users", "id"));
        Assert.NotNull(delta.FindColumnChange("users", "name"));
    }
}
