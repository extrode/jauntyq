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

    // ── AUD-R4-16: HasCompetingUniqueConstraint ───────────────────────────

    /// <summary>
    /// The shape that reopened AUD-R4-16, and the one that actually ships:
    /// Conduit's <c>users</c> -- an identity-only PK plus two secondary unique
    /// indexes. Resolve falls through to <c>email</c>, and both the PK and
    /// <c>username</c> are then constraints MySQL's ON DUPLICATE KEY UPDATE
    /// could match on instead.
    /// </summary>
    [Fact]
    public void HasCompeting_IdentityPkPlusTwoUniqueIndexes_IsTrue()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Email"), Col("Username"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Email", IsUnique = true, Columns = { "Email" } });
        table.Indexes.Add(new IndexSchema { Name = "UX_Username", IsUnique = true, Columns = { "Username" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Email" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// The single-UNIQUE case, which must stay false: it is what keeps every
    /// existing MySQL sample's generated SQL byte-identical.
    /// </summary>
    [Fact]
    public void HasCompeting_OrdinaryPrimaryKeyAlone_IsFalse()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));

        var key = UpsertKeyResolver.Resolve(table);

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key!));
    }

    /// <summary>
    /// An identity-only PK with exactly one secondary unique index still has a
    /// competing constraint -- the PK itself. An insert supplying no identity
    /// value cannot violate it, but one supplying an explicit id can, and
    /// ON DUPLICATE KEY UPDATE would then match on the PK rather than the
    /// resolved token.
    /// </summary>
    [Fact]
    public void HasCompeting_IdentityPkPlusOneUniqueIndex_IsTrue()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Token" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// A unique index that merely restates the primary key -- which every
    /// engine reports, since a PK is implemented as one (Postgres
    /// <c>users_pkey</c>, MySQL <c>PRIMARY</c>) -- is not a competing
    /// constraint. Without set comparison this would make every table on
    /// every dialect look ambiguous and take the two-statement path.
    /// </summary>
    [Fact]
    public void HasCompeting_UniqueIndexRestatingThePrimaryKey_IsFalse()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema { Name = "PRIMARY", IsUnique = true, Columns = { "Id" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key!));
    }

    /// <summary>
    /// Set equality is order-insensitive: <c>UNIQUE (a, b)</c> and
    /// <c>UNIQUE (b, a)</c> reject exactly the same rows, so an index whose
    /// columns are the key's in a different order is the same constraint, not
    /// a competing one.
    /// </summary>
    [Fact]
    public void HasCompeting_CompositeKeyRestatedInAnotherOrder_IsFalse()
    {
        var table = Table(Col("A", pk: true), Col("B", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema { Name = "UX_BA", IsUnique = true, Columns = { "B", "A" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "A", "B" }, key!.ConvertAll(c => c.Name));
        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// A non-unique index is not a conflict target on any engine.
    /// </summary>
    [Fact]
    public void HasCompeting_NonUniqueIndex_IsFalse()
    {
        var table = Table(Col("Id", pk: true), Col("City"));
        table.Indexes.Add(new IndexSchema { Name = "IX_City", IsUnique = false, Columns = { "City" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key!));
    }

    /// <summary>
    /// The independent review of AUD-R4-16 found the secondary-index branch
    /// unreachable by test: every case asserting true above has a primary key
    /// that differs from the resolved key, so it returns on the PK check and
    /// the <c>Indexes</c> loop never runs. This is the shape that reaches it --
    /// an ordinary (non-identity) PK which IS the resolved key, so the PK check
    /// passes, plus a secondary UNIQUE that competes. An inverted condition in
    /// that loop would previously have gone unnoticed while silently emitting
    /// untargeted ON DUPLICATE KEY UPDATE.
    /// </summary>
    [Fact]
    public void HasCompeting_OrdinaryPkIsTheKey_PlusSecondaryUnique_IsTrue()
    {
        var table = Table(Col("Id", pk: true), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Email", IsUnique = true, Columns = { "Email" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Id" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// A full-column UNIQUE over a strict SUPERSET of the key is not competing:
    /// violating <c>UNIQUE (Id, Tenant)</c> requires duplicating <c>Id</c>, and
    /// with <c>Id</c> unique the row it matches is the key's own match — the
    /// engine cannot diverge through it. This relaxation shipped ungated once,
    /// was reverted because a prefix index (<c>UNIQUE (Id(3), Tenant)</c>)
    /// arrived looking identical, and returned once <c>MySqlExtractor</c>
    /// started capturing <c>SUB_PART</c> as <c>HasPrefixKeyPart</c>. The
    /// flagged shape is pinned separately below.
    /// </summary>
    [Fact]
    public void HasCompeting_FullColumnUniqueIndexOverSupersetOfKey_IsFalse()
    {
        var table = Table(Col("Id", pk: true), Col("Tenant"), Col("Name"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Id_Tenant", IsUnique = true, Columns = { "Id", "Tenant" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Id" }, key!.ConvertAll(c => c.Name));
        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// The same superset with a prefix key part IS competing — the exact shape
    /// that got the ungated relaxation reverted. <c>UNIQUE (Id(3), Tenant)</c>
    /// fires on rows sharing only the first three characters of <c>Id</c>,
    /// entirely independently of a key of <c>(Id)</c>, so MySQL's untargeted
    /// ON DUPLICATE KEY UPDATE can match a row the key would not. The
    /// relaxation must stay gated on <c>HasPrefixKeyPart</c>.
    /// </summary>
    [Fact]
    public void HasCompeting_PrefixUniqueIndexOverSupersetOfKey_IsTrue()
    {
        var table = Table(Col("Id", pk: true), Col("Tenant"), Col("Name"));
        table.Indexes.Add(new IndexSchema
        {
            Name = "UX_Id_Tenant",
            IsUnique = true,
            Columns = { "Id", "Tenant" },
            HasPrefixKeyPart = true
        });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Id" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// The opposite direction, and the reason the superset rule is not simply
    /// "ignore anything overlapping the key": a UNIQUE over a strict SUBSET is
    /// stricter than the key and rejects rows the key permits, so it is
    /// genuinely competing.
    /// </summary>
    [Fact]
    public void HasCompeting_UniqueIndexOverSubsetOfKey_IsTrue()
    {
        var table = Table(Col("A", pk: true), Col("B", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema { Name = "UX_A", IsUnique = true, Columns = { "A" } });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "A", "B" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    // ── Prefix-only keys are refused (decided 2026-07-30) ─────────────────

    /// <summary>
    /// The email(5) hole: an identity-only PK whose only fallback UNIQUE is a
    /// prefix index. The index enforces uniqueness over five characters, so an
    /// Upsert matching on it would deliver prefix semantics under a full-value
    /// signature — Resolve refuses and names the index so the generator can
    /// report JNT2019 instead of a silent skip.
    /// </summary>
    [Fact]
    public void Resolve_PrefixUniqueAsOnlyFallbackKey_RefusesAndNamesTheIndex()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Email_Prefix", IsUnique = true, Columns = { "Email" }, HasPrefixKeyPart = true });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Null(result);
        Assert.NotNull(prefixOnlyKey);
        Assert.Equal("UX_Email_Prefix", prefixOnlyKey!.Name);
    }

    /// <summary>
    /// The skip keeps scanning: a later full-column UNIQUE still resolves as
    /// the key, and the skipped prefix index then counts as competing (pinned
    /// below) rather than as a refusal.
    /// </summary>
    [Fact]
    public void Resolve_PrefixUniqueSkipped_LaterFullColumnUniqueStillResolves()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Email"), Col("Token"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Email_Prefix", IsUnique = true, Columns = { "Email" }, HasPrefixKeyPart = true });
        table.Indexes.Add(new IndexSchema { Name = "UX_Token", IsUnique = true, Columns = { "Token" } });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Equal(new[] { "Token" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    /// <summary>
    /// MySQL permits prefix key parts in a PRIMARY KEY too (PRIMARY KEY
    /// (name(10))). Column-level PK flags carry no prefix information, but the
    /// extractor captures the PRIMARY index entry like any other — when it is
    /// flagged and no full-column set-equal UNIQUE exists, the PK enforces
    /// only prefix uniqueness and the key is refused.
    /// </summary>
    [Fact]
    public void Resolve_PkEnforcedOnlyByPrefixPrimaryIndex_Refuses()
    {
        var table = Table(Col("Name", pk: true), Col("City"));
        table.Indexes.Add(new IndexSchema { Name = "PRIMARY", IsUnique = true, Columns = { "Name" }, HasPrefixKeyPart = true });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Null(result);
        Assert.Equal("PRIMARY", prefixOnlyKey!.Name);
    }

    /// <summary>
    /// A full-column UNIQUE set-equal to the prefix-flagged PK proves the key
    /// IS enforced in full somewhere — the PK resolves, and the flagged
    /// restatement competes instead (two-statement form, full-column WHERE).
    /// </summary>
    [Fact]
    public void Resolve_PrefixPkWithFullColumnUniqueRestatement_StillResolves()
    {
        var table = Table(Col("Name", pk: true), Col("City"));
        table.Indexes.Add(new IndexSchema { Name = "PRIMARY", IsUnique = true, Columns = { "Name" }, HasPrefixKeyPart = true });
        table.Indexes.Add(new IndexSchema { Name = "UX_Name_Full", IsUnique = true, Columns = { "Name" } });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Equal(new[] { "Name" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, result!));
    }

    /// <summary>
    /// A simulated schema (migration parsing) has no index entries at all, so
    /// there is no evidence of a prefix PK — it resolves exactly as before.
    /// This is what keeps the refusal from touching non-extracted snapshots.
    /// </summary>
    [Fact]
    public void Resolve_PkWithNoIndexEntries_ResolvesAsBefore()
    {
        var table = Table(Col("Name", pk: true), Col("City"));

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Equal(new[] { "Name" }, result!.ConvertAll(c => c.Name));
        Assert.Null(prefixOnlyKey);
    }

    /// <summary>
    /// A prefix restatement of the key is competing, not redundant: with the
    /// key enforced by the full-column PK, UNIQUE (Id(3)) still fires on rows
    /// sharing three characters — rows the key would never match — so MySQL's
    /// untargeted ON DUPLICATE KEY UPDATE can diverge through it. Contrast
    /// with <see cref="HasCompeting_UniqueIndexRestatingThePrimaryKey_IsFalse"/>:
    /// only the FULL-column restatement is exempt.
    /// </summary>
    [Fact]
    public void HasCompeting_PrefixRestatementOfTheKey_IsTrue()
    {
        var table = Table(Col("Id", pk: true), Col("Name"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Id_Prefix", IsUnique = true, Columns = { "Id" }, HasPrefixKeyPart = true });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Id" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    // ── Expression indexes: represented flagged, never the key, always competing ──

    /// <summary>
    /// A flagged index's Columns is only the real-column SUBSET of its key —
    /// UNIQUE (Token, lower(Email)) arrives as [Token], and Token alone is not
    /// unique — so Resolve must never pick it as the fallback key. Before
    /// 2026-07-30 such indexes were excluded at capture; the skip preserves
    /// exactly that reading.
    /// </summary>
    [Fact]
    public void Resolve_ExpressionUniqueAsOnlyFallback_IsNotPickedAsKey()
    {
        var table = Table(Col("Id", pk: true, identity: true), Col("Token"), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "UX_Token_LowerEmail", IsUnique = true, Columns = { "Token" }, HasExpressionKeyPart = true });

        var result = UpsertKeyResolver.Resolve(table, out var prefixOnlyKey);

        Assert.Null(result);
        Assert.Null(prefixOnlyKey); // not the prefix refusal -- status-quo silent skip
    }

    /// <summary>
    /// A UNIQUE with an expression key part is always competing: it is never
    /// the key, and it constrains rows through an expression the key's columns
    /// say nothing about. Deliberately the flag-DEPENDENT shape — the real-
    /// column subset [Id] is set-equal to the key, which the unflagged rule
    /// would dismiss as a restatement — so this test fails the moment the
    /// flag check is dropped (a differing-columns index competes under the
    /// generic rule and would pin nothing).
    /// </summary>
    [Fact]
    public void HasCompeting_ExpressionUniqueIndex_SubsetEqualToKey_IsTrue()
    {
        var table = Table(Col("Id", pk: true), Col("Email"));
        // UNIQUE (Id, lower(Email)) -- arrives as [Id] + flag.
        table.Indexes.Add(new IndexSchema { Name = "UX_Id_LowerEmail", IsUnique = true, Columns = { "Id" }, HasExpressionKeyPart = true });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.Equal(new[] { "Id" }, key!.ConvertAll(c => c.Name));
        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }

    /// <summary>
    /// The ALL-expression shape — UNIQUE (lower(Email)) arrives flagged with
    /// NO columns at all — must not slip through the empty-Columns guard that
    /// (correctly) ignores degenerate column-less indexes otherwise.
    /// </summary>
    [Fact]
    public void HasCompeting_AllExpressionUniqueIndex_EmptyColumns_IsTrue()
    {
        var table = Table(Col("Id", pk: true), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "UX_LowerEmail", IsUnique = true, HasExpressionKeyPart = true });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key!));
    }

    /// <summary>
    /// A NON-unique expression index competes with nothing — it is not a
    /// conflict target on any engine — and must not trip the flag check.
    /// </summary>
    [Fact]
    public void HasCompeting_NonUniqueExpressionIndex_IsFalse()
    {
        var table = Table(Col("Id", pk: true), Col("Email"));
        table.Indexes.Add(new IndexSchema { Name = "IX_LowerEmail", IsUnique = false, HasExpressionKeyPart = true });

        var key = UpsertKeyResolver.Resolve(table);

        Assert.False(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key!));
    }

    /// <summary>
    /// The same conservative treatment applies to a primary key covering a
    /// superset of the resolved key — MySQL permits prefix key parts in a
    /// PRIMARY KEY too. The key is passed directly rather than via <see
    /// cref="UpsertKeyResolver.Resolve"/>, which would choose the PK itself for
    /// this table and never produce the combination under test.
    /// </summary>
    [Fact]
    public void HasCompeting_PrimaryKeyOverSupersetOfKey_IsTrue_Conservatively()
    {
        var table = Table(Col("Email", pk: true), Col("Tenant", pk: true));

        var key = new List<ColumnSchema> { table.Columns["Email"] };

        Assert.True(UpsertKeyResolver.HasCompetingUniqueConstraint(table, key));
    }
}
