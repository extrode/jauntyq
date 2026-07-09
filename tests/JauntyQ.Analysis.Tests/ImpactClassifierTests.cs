using JauntyQ.Analysis.Diff;
using JauntyQ.Analysis.Impact;
using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;
using Xunit;

namespace JauntyQ.Analysis.Tests;

public class ImpactClassifierTests
{
    // ── schema builders ───────────────────────────────────
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

    private static ColumnSchema Col(string name, string dbType = "int", int? maxLen = null) =>
        new() { Name = name, DbType = dbType, MaxLength = maxLen };

    // ── query builders ────────────────────────────────────
    private static QueryModel Select(string table, params string[] columns)
    {
        var m = new QueryModel { Name = "q", StatementType = StatementType.Select };
        m.Tables.Add(new TableRef { TableName = table });
        foreach (var c in columns)
            m.Columns.Add(new ColumnRef { ColumnName = c });
        return m;
    }

    private static QueryImpactInput Input(string method, QueryModel m, bool pre = false, bool unmodeled = false) =>
        new($"{method}.sql", method, ReferencedObjects.Resolve(m), pre, unmodeled);

    private static SchemaDelta Delta(DatabaseSchema baseline, DatabaseSchema effective) =>
        StructuralSchemaDiff.Compute(baseline, effective);

    private static MigrationImpactReport Run(SchemaDelta delta, params QueryImpactInput[] inputs) =>
        ImpactClassifier.Classify(delta, inputs, "schema/jaunty.schema.json", new[] { "0001.sql" });

    // ── tests ─────────────────────────────────────────────

    [Fact]
    public void Safe_WhenNoReferencedObjectChanged()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar")));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar"), Col("added", "int")));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "name")));

        Assert.Equal(Classification.Safe, Assert.Single(report.Entries).Classification);
    }

    [Fact]
    public void Breaking_WhenReferencedColumnRemoved()
    {
        var baseline = Schema(Table("users", Col("id"), Col("legacy_flag", "bit")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "legacy_flag")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.legacy_flag" && r.ChangeKind == "removed");
    }

    [Fact]
    public void Breaking_WhenReferencedTableRemoved()
    {
        var baseline = Schema(Table("users", Col("id")), Table("legacy", Col("id")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective), Input("Legacy.Get", Select("legacy", "id")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Breaking, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "legacy" && r.ChangeKind == "removed");
    }

    [Fact]
    public void Risky_WhenReferencedColumnModified()
    {
        var baseline = Schema(Table("users", Col("id"), Col("name", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("name", "varchar", 50)));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "id", "name")));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.SchemaObject == "users.name" && r.ChangeKind == "maxLength");
    }

    [Fact]
    public void Risky_WhenUnmodeledStatementTouchesReferencedTable()
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users", Col("id")));

        var report = Run(Delta(baseline, effective),
            Input("User.Get", Select("users", "id"), unmodeled: true));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Risky, entry.Classification);
        Assert.Contains(entry.Reasons, r => r.ChangeKind == "unmodeled");
    }

    [Fact]
    public void ExactK_DroppedColumnReferencedByTwoQueries() // SC-001
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar")));
        var effective = Schema(Table("users", Col("id")));
        var delta = Delta(baseline, effective);

        var report = Run(delta,
            Input("User.GetEmail", Select("users", "id", "email")),
            Input("User.SearchEmail", Select("users", "email")),
            Input("User.GetId", Select("users", "id")));

        Assert.Equal(2, report.Count(Classification.Breaking));
        Assert.Equal(1, report.Count(Classification.Safe));
        Assert.Equal(0, report.Count(Classification.Risky));
    }

    [Fact]
    public void NoFalseSafe_AnyQueryTouchingChangedObjectIsNotSafe() // SC-004
    {
        var baseline = Schema(Table("t", Col("a"), Col("b", "varchar", 100), Col("c")));
        var effective = Schema(Table("t", Col("a"), Col("b", "varchar", 40))); // b modified, c removed
        var delta = Delta(baseline, effective);

        // every query references at least one changed object (b or c)
        var inputs = new[]
        {
            Input("Q.b", Select("t", "b")),
            Input("Q.c", Select("t", "c")),
            Input("Q.bc", Select("t", "b", "c")),
            Input("Q.abc", Select("t", "a", "b", "c")),
        };
        var report = Run(delta, inputs);

        Assert.All(report.Entries, e => Assert.NotEqual(Classification.Safe, e.Classification));
    }

    [Fact]
    public void PreexistingError_NotAttributedToMigration() // SC-006
    {
        var baseline = Schema(Table("users", Col("id")));
        var effective = Schema(Table("users"));  // id dropped

        var report = Run(Delta(baseline, effective),
            Input("User.Get", Select("users", "id"), pre: true));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(Classification.Safe, entry.Classification);
        Assert.Empty(entry.Reasons);
    }

    [Fact]
    public void Highest_ReflectsMostSevereEntry()
    {
        var baseline = Schema(Table("users", Col("id"), Col("email", "varchar", 100)));
        var effective = Schema(Table("users", Col("id"), Col("email", "varchar", 50)));

        var report = Run(Delta(baseline, effective), Input("User.Get", Select("users", "email")));

        Assert.Equal(Classification.Risky, report.Highest);
    }
}
