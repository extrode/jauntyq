using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for CrudColumnRules -- the single source of truth AutoCrud
/// (SQL synthesis) and JauntyQ.Generator's CodeEmitter (Part4/6/7/11.cs, C#
/// emission) both consume so their column sets can never independently drift
/// apart (see CrudColumnRules.cs's own docs for the failure mode this
/// prevents: a generated method's C# argument list silently desyncing from
/// the SQL parameter list built from a differently-filtered column set).
/// </summary>
public class CrudColumnRulesTests
{
    private static ColumnSchema Col(string name, bool pk = false, bool identity = false, bool rowVersion = false, bool computed = false) =>
        new() { Name = name, DbType = "int", IsPrimaryKey = pk, IsIdentity = identity, IsRowVersion = rowVersion, IsComputed = computed };

    [Fact]
    public void PrimaryKeyColumns_ReturnsOnlyPkFlaggedColumns()
    {
        var cols = new List<ColumnSchema> { Col("Id", pk: true), Col("Name"), Col("TenantId", pk: true) };

        var result = CrudColumnRules.PrimaryKeyColumns(cols);

        Assert.Equal(new[] { "Id", "TenantId" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void RowVersionColumns_ReturnsOnlyRowVersionFlaggedColumns()
    {
        var cols = new List<ColumnSchema> { Col("Id", pk: true), Col("RowVer", rowVersion: true), Col("Name") };

        var result = CrudColumnRules.RowVersionColumns(cols);

        Assert.Equal(new[] { "RowVer" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void InsertableColumns_ExcludesIdentityRowVersionAndComputed()
    {
        var cols = new List<ColumnSchema>
        {
            Col("Id", pk: true, identity: true),
            Col("Name"),
            Col("RowVer", rowVersion: true),
            Col("Total", computed: true),
        };

        var result = CrudColumnRules.InsertableColumns(cols);

        Assert.Equal(new[] { "Name" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void UpdatableColumns_AlsoExcludesPrimaryKey()
    {
        // A non-PK identity column (e.g. a separate auto-increment sequence
        // alongside a natural-key PK) must stay excluded too: every dialect
        // tested (SQL Server) rejects an UPDATE targeting an identity column
        // regardless of whether it's the key.
        var cols = new List<ColumnSchema>
        {
            Col("Code", pk: true),
            Col("Seq", identity: true),
            Col("Name"),
            Col("RowVer", rowVersion: true),
        };

        var result = CrudColumnRules.UpdatableColumns(cols);

        Assert.Equal(new[] { "Name" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void SingleIdentityColumn_ReturnsTheOneIdentityColumn()
    {
        var cols = new List<ColumnSchema> { Col("Id", pk: true, identity: true), Col("Name") };

        var result = CrudColumnRules.SingleIdentityColumn(cols);

        Assert.NotNull(result);
        Assert.Equal("Id", result!.Name);
    }

    [Fact]
    public void SingleIdentityColumn_ReturnsNull_WhenNoIdentityColumn()
    {
        var cols = new List<ColumnSchema> { Col("Code", pk: true), Col("Name") };

        Assert.Null(CrudColumnRules.SingleIdentityColumn(cols));
    }

    [Fact]
    public void SingleIdentityColumn_ReturnsNull_WhenMultipleIdentityColumns()
    {
        var cols = new List<ColumnSchema> { Col("Id", pk: true, identity: true), Col("Seq", identity: true) };

        Assert.Null(CrudColumnRules.SingleIdentityColumn(cols));
    }

    // R9 §2.10 mandate (2.10-InsertableColumns/UpdatableColumns/... full
    // facet-combination matrix): IsPrimaryKey/IsIdentity/IsRowVersion/
    // IsComputed are 4 independent booleans (16 combinations). Prior rounds
    // only ever exercised a handful of hand-picked combinations on
    // different columns of the same table (never the full 2^4 grid), so a
    // combination like pk+identity+rowVersion+computed all set together —
    // or rowVersion+computed with neither pk nor identity — was never
    // actually driven through the filters. Every row is independently
    // derivable from the documented predicates:
    //   InsertableColumns keeps: !identity && !rowVersion && !computed
    //   UpdatableColumns  keeps: !pk && !identity && !rowVersion && !computed
    [Theory]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(true, false, false, false, true, false)]   // pk alone
    [InlineData(false, true, false, false, false, false)]  // identity alone
    [InlineData(false, false, true, false, false, false)]  // rowVersion alone
    [InlineData(false, false, false, true, false, false)]  // computed alone
    [InlineData(true, true, false, false, false, false)]   // pk+identity
    [InlineData(true, false, true, false, false, false)]   // pk+rowVersion
    [InlineData(true, false, false, true, false, false)]   // pk+computed
    [InlineData(false, true, true, false, false, false)]   // identity+rowVersion
    [InlineData(false, true, false, true, false, false)]   // identity+computed
    [InlineData(false, false, true, true, false, false)]   // rowVersion+computed
    [InlineData(true, true, true, false, false, false)]    // pk+identity+rowVersion
    [InlineData(true, true, false, true, false, false)]    // pk+identity+computed
    [InlineData(true, false, true, true, false, false)]    // pk+rowVersion+computed
    [InlineData(false, true, true, true, false, false)]    // identity+rowVersion+computed
    [InlineData(true, true, true, true, false, false)]     // all four set
    public void InsertableAndUpdatableColumns_FullFacetCombinationMatrix(
        bool pk, bool identity, bool rowVersion, bool computed,
        bool expectInsertable, bool expectUpdatable)
    {
        var col = Col("Facet", pk: pk, identity: identity, rowVersion: rowVersion, computed: computed);
        var cols = new List<ColumnSchema> { col };

        bool insertable = CrudColumnRules.InsertableColumns(cols).Exists(c => c.Name == "Facet");
        bool updatable = CrudColumnRules.UpdatableColumns(cols).Exists(c => c.Name == "Facet");

        Assert.Equal(expectInsertable, insertable);
        Assert.Equal(expectUpdatable, updatable);
    }

    [Fact]
    public void UpsertColumns_ExcludesIdentityUnlessPartOfKey()
    {
        // OtherId is identity but NOT part of the key: excluded (no
        // caller-supplied value can ever reach a database-assigned column).
        // Id is identity AND part of a composite key: kept, since the
        // MERGE/ON CONFLICT match clause still needs a value to bind.
        var cols = new List<ColumnSchema>
        {
            Col("Id", pk: true, identity: true),
            Col("OtherId", identity: true),
            Col("Name"),
            Col("RowVer", rowVersion: true),
            Col("Total", computed: true),
        };
        var key = new List<ColumnSchema> { cols[0] };

        var result = CrudColumnRules.UpsertColumns(cols, key);

        Assert.Equal(new[] { "Id", "Name" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void UpsertSetColumns_ExcludesKeyColumns()
    {
        var upsertCols = new List<ColumnSchema> { Col("Id", pk: true, identity: true), Col("Name"), Col("Email") };
        var key = new List<ColumnSchema> { upsertCols[0] };

        var result = CrudColumnRules.UpsertSetColumns(upsertCols, key);

        Assert.Equal(new[] { "Name", "Email" }, result.ConvertAll(c => c.Name));
    }

    [Fact]
    public void UpsertSetColumns_Empty_WhenOnlyNonKeyColumnIsANonKeyIdentity()
    {
        // A table whose only non-key column is a lone non-key identity
        // column (e.g. identity id + a single idempotency-key unique
        // column) must resolve to zero SET columns -- callers use this to
        // gate whether Upsert synthesis is worthwhile at all.
        var cols = new List<ColumnSchema> { Col("IdempotencyKey", pk: true), Col("Id", identity: true) };
        var key = new List<ColumnSchema> { cols[0] };

        var upsertCols = CrudColumnRules.UpsertColumns(cols, key);
        var setCols = CrudColumnRules.UpsertSetColumns(upsertCols, key);

        Assert.Empty(setCols);
    }
}
