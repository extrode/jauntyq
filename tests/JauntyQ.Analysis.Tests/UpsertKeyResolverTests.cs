using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for <see cref="UpsertKeyResolver"/> -- the schema-only,
/// Roslyn-free PK/secondary-unique-index resolver shared by <see
/// cref="AutoCrud"/>'s Upsert-synthesis gate and JauntyQ.Generator's
/// <c>CodeEmitter.Part6.cs::EmitUpsert</c>.
/// </summary>
public class UpsertKeyResolverTests
{
    private static ColumnSchema Col(string name, bool pk = false, bool identity = false, bool rowVersion = false, bool computed = false) =>
        new() { Name = name, DbType = "int", IsPrimaryKey = pk, IsIdentity = identity, IsRowVersion = rowVersion, IsComputed = computed };

    private static TableSchema Table(params ColumnSchema[] cols)
    {
        var t = new TableSchema { Name = "T" };
        foreach (var c in cols)
            t.Columns[c.Name] = c;
        return t;
    }

    [Fact]
    public void Resolve_OrdinaryNonIdentityPrimaryKey_ReturnsThatKey()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));

        var result = UpsertKeyResolver.Resolve(table);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
    }

    /// <summary>
    /// AUD-R36-01. A table whose sole primary key is a PERSISTED computed
    /// column (a real, documented SQL Server pattern -- a deterministic
    /// PERSISTED computed column may be part of a PRIMARY KEY constraint;
    /// SqlServerExtractor's own IsPrimaryKey/IsComputed detection are two
    /// fully independent COLUMNPROPERTY() queries with nothing preventing
    /// both being true for the same column) must still resolve as the
    /// table's usable Upsert key, exactly as <see
    /// cref="CrudColumnRules.PrimaryKeyColumns"/> (AutoCrud.cs's own
    /// GetById/Update/Delete WHERE-key resolution, run against the very same
    /// table in the very same AutoCrud.Synthesize pass) already treats it.
    /// </summary>
    [Fact]
    public void Resolve_ComputedPrimaryKeyColumn_StillResolvesAsKey()
    {
        var table = Table(Col("Id", pk: true, computed: true), Col("Name"));

        // Sibling algorithm for the identical schema fact: must agree.
        var crudPk = CrudColumnRules.PrimaryKeyColumns(table.Columns.Values);
        Assert.Equal(new[] { "Id" }, crudPk.ConvertAll(c => c.Name));

        var result = UpsertKeyResolver.Resolve(table);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
    }

    /// <summary>
    /// Same defect shape, the RowVersion sibling: a table whose sole PK
    /// column is also flagged IsRowVersion (unusual, but nothing in the
    /// schema model or any extractor structurally forbids it) must not be
    /// silently treated as having no primary key at all.
    /// </summary>
    [Fact]
    public void Resolve_RowVersionPrimaryKeyColumn_StillResolvesAsKey()
    {
        var table = Table(Col("Id", pk: true, rowVersion: true), Col("Name"));

        var result = UpsertKeyResolver.Resolve(table);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
    }

    [Fact]
    public void Resolve_IdentityOnlyPrimaryKey_FallsBackToSecondaryUniqueIndex()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });

        var result = UpsertKeyResolver.Resolve(table);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Token" }, result!.ConvertAll(c => c.Name));
    }

    [Fact]
    public void Resolve_IdentityOnlyPrimaryKey_NoSecondaryUniqueIndex_ReturnsNull()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Name"));

        var result = UpsertKeyResolver.Resolve(table);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_NoPrimaryKeyAtAll_ReturnsNull()
    {
        var table = Table(Col("Name"));

        var result = UpsertKeyResolver.Resolve(table);

        Assert.Null(result);
    }
}
