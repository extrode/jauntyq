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
