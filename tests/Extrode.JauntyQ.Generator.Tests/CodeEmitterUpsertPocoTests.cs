using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CodeEmitterUpsertPocoTests
{
    private static ColumnSchema Col(string name, string type = "int", bool pk = false, bool identity = false, bool computed = false)
        => new() { Name = name, DbType = type, IsPrimaryKey = pk, IsIdentity = identity, IsComputed = computed };

    private static TableSchema Table(string name, params ColumnSchema[] cols)
    {
        var t = new TableSchema { Name = name };
        foreach (var c in cols)
            t.Columns[c.Name] = c;
        return t;
    }

    [Fact]
    public void EmitUpsert_IdentityOnlyKey_ThrowsNamingTheMissingKey()
    {
        var table = Table("q", Col("id", pk: true, identity: true), Col("a"));

        var ex = Assert.Throws<InvalidOperationException>(() => CodeEmitter.EmitUpsert("Q", table, "mysql"));
        Assert.Equal("Table 'q' has no usable upsert key: no primary key, or an identity-only primary key with no secondary UNIQUE index to match on instead.", ex.Message);
    }

    [Fact]
    public void EmitUpsert_ComputedKeyColumn_ThrowsNamingTheUnbindableColumn()
    {
        var table = Table("q", Col("id", pk: true, computed: true), Col("a"));

        var ex = Assert.Throws<InvalidOperationException>(() => CodeEmitter.EmitUpsert("Q", table, "mysql"));
        Assert.Equal("Table 'q' has no usable upsert key: its primary key contains a Computed or RowVersion column, and no dialect can bind a value for one on the INSERT/conflict-target side of an upsert.", ex.Message);
    }

    [Fact]
    public void EmitUpsert_UnknownDialect_ThrowsNamingTheDialect()
    {
        var table = Table("q", Col("k", pk: true), Col("a"));

        var ex = Assert.Throws<InvalidOperationException>(() => CodeEmitter.EmitUpsert("Q", table, "oracle"));
        Assert.Equal("Upsert synthesis requires a known dialect (got 'oracle').", ex.Message);
    }

    [Fact]
    public void EmitUpsert_MySqlWithoutCompetingUnique_UpdatesEverySetColumn()
    {
        var table = Table("q", Col("k", pk: true), Col("a"), Col("b"));

        Assert.Contains("ON DUPLICATE KEY UPDATE a = VALUES(a), b = VALUES(b)", CodeEmitter.EmitUpsert("Q", table, "mysql"));
    }

    [Fact]
    public void EmitUpsert_MySqlWithCompetingUnique_MatchesOnEveryKeyColumn()
    {
        var table = Table("q", Col("k1", pk: true), Col("k2", pk: true), Col("a"));
        table.Indexes.Add(new IndexSchema { Name = "ux_a", Columns = { "a" }, IsUnique = true });

        Assert.Contains("WHERE k1 = @k1 AND k2 = @k2", CodeEmitter.EmitUpsert("Q", table, "mysql"));
    }

    private static string Poco(TableSchema table, bool insert = true, bool update = true, bool delete = true, bool upsert = false)
        => CodeEmitter.EmitPocoOverloads("Q", "QRow", table, "postgres", insert, update, delete, upsert);

    [Fact]
    public void EmitPocoOverloads_ColumnNamedTransaction_DropsTheTransactionParameter()
    {
        string plain = Poco(Table("q", Col("k", pk: true), Col("a")));
        string clash = Poco(Table("q", Col("k", pk: true), Col("transaction")));

        Assert.Contains("public static int Update(DbConnection conn, QRow row, DbTransaction? transaction = null)", plain);
        Assert.Contains("public static int Update(DbConnection conn, QRow row) =>", clash);
    }

    [Fact]
    public void EmitPocoOverloads_IdentityInsertWithColumnNamedTransaction_DropsTheTransactionParameter()
    {
        string plain = Poco(Table("q", Col("id", pk: true, identity: true), Col("a")));
        string clash = Poco(Table("q", Col("id", pk: true, identity: true), Col("transaction")));

        Assert.Contains("public static int Insert(DbConnection conn, QRow row, DbTransaction? transaction = null)", plain);
        Assert.Contains("public static int Insert(DbConnection conn, QRow row)\n", clash.Replace("\r\n", "\n"));
    }

    [Fact]
    public void EmitPocoOverloads_EmitsOnlyTheRequestedAndPossibleOperations()
    {
        var full = Table("q", Col("k", pk: true), Col("a"));

        Assert.DoesNotContain(" Insert(", Poco(full, insert: false));
        Assert.DoesNotContain(" Delete(", Poco(full, delete: false));
        Assert.DoesNotContain(" Insert(", Poco(Table("q", Col("id", pk: true, identity: true))));
        Assert.DoesNotContain(" Update(", Poco(Table("q", Col("k1", pk: true), Col("k2", pk: true))));
        Assert.DoesNotContain(" Update(", Poco(Table("q", Col("a"), Col("b"))));
        Assert.DoesNotContain(" Delete(", Poco(Table("q", Col("a"), Col("b"))));
    }

    [Fact]
    public void EmitPocoOverloads_UpsertWithoutUsableKey_Throws()
    {
        var noKey = Assert.Throws<InvalidOperationException>(() => Poco(Table("q", Col("id", pk: true, identity: true), Col("a")), upsert: true));
        var computed = Assert.Throws<InvalidOperationException>(() => Poco(Table("q", Col("id", pk: true, computed: true), Col("a")), upsert: true));

        Assert.Equal("Table 'q' has no usable upsert key: no primary key, or an identity-only primary key with no secondary UNIQUE index to match on instead.", noKey.Message);
        Assert.Equal("Table 'q' has no usable upsert key: its primary key contains a Computed or RowVersion column, and no dialect can bind a value for one on the INSERT/conflict-target side of an upsert.", computed.Message);
    }
}
