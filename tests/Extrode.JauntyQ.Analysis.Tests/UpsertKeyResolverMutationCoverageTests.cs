using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for UpsertKeyResolver gaps not exercised by
/// UpsertKeyResolverTests: the RowVersion/Computed exclusion actually
/// affecting fallback-index resolution, the non-unique/zero-column/
/// unresolvable-column-in-a-composite-index skip branches, the "first
/// candidate wins" ??= remembering of a skipped expression/prefix index, and
/// FindPrefixOnlyPkEnforcement's/HasCompetingUniqueConstraint's own guard
/// clauses.
/// </summary>
[Trait("Category", "AuditRegression")]
public class UpsertKeyResolverMutationCoverageTests
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

    // ── Fallback path: RowVersion exclusion must actually bite ──────────

    [Fact]
    public void Resolve_FallbackCandidateColumn_ExcludesRowVersionColumn_EvenThoughNotComputed()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Ver", rowVersion: true, computed: false));
        table.Indexes.Add(new IndexSchema { Name = "UX_Ver", IsUnique = true, Columns = { "Ver" } });

        var result = UpsertKeyResolver.Resolve(table);

        Assert.Null(result);
    }

    // ── Fallback loop: non-unique indexes are skipped, not considered ───

    [Fact]
    public void Resolve_FallbackLoop_SkipsNonUniqueIndex_ButStillFindsTheUniqueOne()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Name"), Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "IX_Name", IsUnique = false, Columns = { "Name" } });
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });

        var result = UpsertKeyResolver.Resolve(table);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Token" }, result!.ConvertAll(c => c.Name));
    }

    // ── Fallback loop: a non-expression unique index with zero columns is inert ──

    [Fact]
    public void Resolve_FallbackLoop_ZeroColumnNonExpressionUniqueIndex_IsSkippedAndNotRemembered()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Weird", IsUnique = true, Columns = { }, HasExpressionKeyPart = false });
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out var expressionOnlyKey);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Token" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
        Assert.Null(expressionOnlyKey);
    }

    // ── Fallback loop: a composite index with one identity/unresolvable column is rejected whole ──

    [Fact]
    public void Resolve_CompositeIndex_WithASecondIdentityColumn_IsRejectedEntirely_NotPartiallyAccepted()
    {
        var table = Table(
            Col("Id", pk: true, identity: true),
            Col("Name"),
            Col("SerialNo", identity: true));
        table.Indexes.Add(new IndexSchema { Name = "UX_Composite", IsUnique = true, Columns = { "Name", "SerialNo" } });

        var result = UpsertKeyResolver.Resolve(table);

        Assert.Null(result);
    }

    // ── Fallback loop: "first skipped candidate wins", not the last ─────

    [Fact]
    public void Resolve_TwoExpressionOnlyUniqueIndexes_RemembersOnlyTheFirstAsTheOutCandidate()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Expr1", IsUnique = true, Columns = { }, HasExpressionKeyPart = true });
        table.Indexes.Add(new IndexSchema { Name = "UX_Expr2", IsUnique = true, Columns = { }, HasExpressionKeyPart = true });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out var expressionOnlyKey);

        Assert.Null(result);
        Assert.Null(prefixOnlyKey);
        Assert.NotNull(expressionOnlyKey);
        Assert.Equal("UX_Expr1", expressionOnlyKey!.Name);
    }

    [Fact]
    public void Resolve_TwoPrefixOnlyResolvableIndexes_RemembersOnlyTheFirstAsTheOutCandidate()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Token1"), Col("Token2"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Token1", IsUnique = true, Columns = { "Token1" }, HasPrefixKeyPart = true });
        table.Indexes.Add(new IndexSchema { Name = "UX_Token2", IsUnique = true, Columns = { "Token2" }, HasPrefixKeyPart = true });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out var expressionOnlyKey);

        Assert.Null(result);
        Assert.NotNull(prefixOnlyKey);
        Assert.Equal("UX_Token1", prefixOnlyKey!.Name);
        Assert.Null(expressionOnlyKey);
    }

    // ── FindPrefixOnlyPkEnforcement's own OR-guard, term by term ─────────

    [Fact]
    public void Resolve_NonIdentityPk_NonUniqueIndex_FullyMatchingAndPrefixFlagged_IsIgnored_PkResolvesNormally()
    {
        // Guards the "!IsUnique" term: an index that isn't even UNIQUE proves
        // nothing about the PK's enforcement, however well it otherwise
        // matches -- it must be skipped, not mistaken for the PK's own
        // PRIMARY entry.
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            Name = "PRIMARY", IsUnique = false, Columns = { "Id" }, HasPrefixKeyPart = true
        });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out _);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    [Fact]
    public void Resolve_NonIdentityPk_UniqueIndexThatDoesNotCoverTheKey_IsIgnored_PkResolvesNormally()
    {
        // Guards the "!CoversKey" term: a unique index over unrelated columns
        // (wrong name/count) says nothing about the PK's own enforcement,
        // even if it happens to carry Name="PRIMARY" and a prefix flag.
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            Name = "PRIMARY", IsUnique = true, Columns = { "Name" }, HasPrefixKeyPart = true
        });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out _);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    [Fact]
    public void Resolve_NonIdentityPk_ExpressionFlaggedIndexCoveringTheKey_ProvesNothing_PkResolvesNormally()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            // HasPrefixKeyPart=true here (an odd combination in practice, but
            // structurally unconstrained): if the HasExpressionKeyPart guard
            // didn't `continue`, this would fall through into the
            // prefix/PRIMARY-name checks below and wrongly refuse the PK.
            Name = "PRIMARY", IsUnique = true, Columns = { "Id" },
            HasExpressionKeyPart = true, HasPrefixKeyPart = true
        });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out _);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    [Fact]
    public void Resolve_NonIdentityPk_FullColumnNonPrefixIndexCoveringTheKey_ProvesPkIsFine()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            Name = "PRIMARY", IsUnique = true, Columns = { "Id" }, HasPrefixKeyPart = false
        });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out _);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Id" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    [Fact]
    public void Resolve_NonIdentityPk_OnlyPrefixFlaggedPrimaryCoversTheKey_PkIsRefused()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            Name = "PRIMARY", IsUnique = true, Columns = { "Id" }, HasPrefixKeyPart = true
        });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out _);

        Assert.Null(result);
        Assert.NotNull(prefixOnlyKey);
        Assert.Equal("PRIMARY", prefixOnlyKey!.Name);
    }

    // ── HasCompetingUniqueConstraint ─────────────────────────────────────

    [Fact]
    public void HasCompetingUniqueConstraint_EmptyKeyCols_ReturnsFalse()
    {
        var table = Table(Col("Id", pk: true, identity: true));

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, new List<ColumnSchema>()));
    }

    [Fact]
    public void HasCompetingUniqueConstraint_NoPrimaryKeyAtAll_KeyFromSecondaryUnique_NotFlaggedAsCompeting()
    {
        var table = Table(Col("Token"));
        var keyCols = new List<ColumnSchema> { table.Columns["Token"] };

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, keyCols));
    }

    [Fact]
    public void HasCompetingUniqueConstraint_ZeroColumnNonExpressionUniqueIndex_IsSkippedNotTreatedAsCompeting()
    {
        var table = Table(Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });
        table.Indexes.Add(new IndexSchema { Name = "UX_Weird", IsUnique = true, Columns = { }, HasExpressionKeyPart = false });
        var keyCols = new List<ColumnSchema> { table.Columns["Token"] };

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, keyCols));
    }
}
