using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

[Trait("Category", "AuditRegression")]
public class SqliteExtractorTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"jq-sqlite-extract-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath};Pooling=False";

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private Task<DatabaseSchema> Extract() => new SqliteExtractor().ExtractAsync(ConnectionString);

    private static ColumnSchema Column(DatabaseSchema schema, string table, string column)
    {
        Assert.True(schema.Tables.TryGetValue(table, out var t), $"table '{table}' was not extracted");
        Assert.True(t!.Columns.TryGetValue(column, out var c), $"column '{table}.{column}' was not extracted");
        return c!;
    }

    private void CreateAffinityTable() => Exec(@"CREATE TABLE catalog (
        id INTEGER NOT NULL PRIMARY KEY,
        sku VARCHAR(40) NOT NULL,
        descr TEXT NULL,
        memo CLOB NULL,
        code CHARACTER(8) NULL,
        sized VARCHAR(12,3) NULL,
        payload BLOB NULL,
        weight REAL NULL,
        ratio DOUBLE NULL,
        factor FLOAT NULL,
        price DECIMAL(10,2) NULL,
        qty BIGINT NULL,
        active BOOLEAN NULL,
        created DATETIME NULL,
        due DATE NULL,
        alarm TIME NULL,
        doc JSON NULL,
        untyped);");

    [Theory]
    [InlineData("id", "int")]
    [InlineData("sku", "varchar")]
    [InlineData("descr", "varchar")]
    [InlineData("memo", "varchar")]
    [InlineData("code", "varchar")]
    [InlineData("payload", "bytea")]
    [InlineData("weight", "float")]
    [InlineData("ratio", "float")]
    [InlineData("factor", "float")]
    [InlineData("price", "decimal")]
    [InlineData("qty", "bigint")]
    [InlineData("active", "bool")]
    [InlineData("created", "datetime")]
    [InlineData("due", "datetime")]
    [InlineData("alarm", "datetime")]
    [InlineData("doc", "varchar")]
    [InlineData("untyped", "bytea")]
    public async Task DeclaredTypeMapsToDbType(string column, string expected)
    {
        CreateAffinityTable();

        Assert.Equal(expected, Column(await Extract(), "catalog", column).DbType);
    }

    [Fact]
    public async Task VarcharCarriesItsDeclaredLength_AndNoPrecisionOrScale()
    {
        CreateAffinityTable();

        var sku = Column(await Extract(), "catalog", "sku");

        Assert.Equal(40, sku.MaxLength);
        Assert.Null(sku.Precision);
        Assert.Null(sku.Scale);
        Assert.True(sku.IsUnicode);
    }

    [Fact]
    public async Task DecimalCarriesPrecisionAndScale_AndNoMaxLength()
    {
        CreateAffinityTable();

        var price = Column(await Extract(), "catalog", "price");

        Assert.Equal(10, price.Precision);
        Assert.Equal(2, price.Scale);
        Assert.Null(price.MaxLength);
        Assert.Null(price.IsUnicode);
    }

    [Fact]
    public async Task SecondDeclaredNumberOnANonDecimal_IsNotReadAsScale()
    {
        CreateAffinityTable();

        var sized = Column(await Extract(), "catalog", "sized");

        Assert.Equal(12, sized.MaxLength);
        Assert.Null(sized.Scale);
        Assert.Null(sized.Precision);
    }

    [Fact]
    public async Task TypeWithoutParentheses_HasNoLengthFacets()
    {
        CreateAffinityTable();

        var descr = Column(await Extract(), "catalog", "descr");

        Assert.Null(descr.MaxLength);
        Assert.Null(descr.Precision);
        Assert.Null(descr.Scale);
    }

    [Fact]
    public async Task BlobCarriesItsDeclaredLength()
    {
        Exec("CREATE TABLE blobs (payload BLOB(64) NULL);");

        Assert.Equal(64, Column(await Extract(), "blobs", "payload").MaxLength);
    }

    [Fact]
    public async Task NotNullAndNullableAreDistinguished()
    {
        CreateAffinityTable();

        var schema = await Extract();

        Assert.False(Column(schema, "catalog", "sku").IsNullable);
        Assert.True(Column(schema, "catalog", "descr").IsNullable);
    }

    [Fact]
    public async Task SingleIntegerPrimaryKey_IsIdentity()
    {
        CreateAffinityTable();

        var id = Column(await Extract(), "catalog", "id");

        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
        Assert.False(Column(await Extract(), "catalog", "sku").IsPrimaryKey);
    }

    [Fact]
    public async Task CompositePrimaryKeyOfIntegers_IsNotIdentity()
    {
        Exec(@"CREATE TABLE pairs (
            a INTEGER NOT NULL,
            b INTEGER NOT NULL,
            PRIMARY KEY (a, b));");

        var schema = await Extract();

        Assert.True(Column(schema, "pairs", "a").IsPrimaryKey);
        Assert.True(Column(schema, "pairs", "b").IsPrimaryKey);
        Assert.False(Column(schema, "pairs", "a").IsIdentity);
        Assert.False(Column(schema, "pairs", "b").IsIdentity);
    }

    [Fact]
    public async Task SingleBigintPrimaryKey_IsNotIdentity()
    {
        Exec("CREATE TABLE ledger (id BIGINT NOT NULL PRIMARY KEY, note TEXT NULL);");

        var id = Column(await Extract(), "ledger", "id");

        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsIdentity);
    }

    [Fact]
    public async Task ViewsAreExtractedAndFlagged_TablesAreNot()
    {
        CreateAffinityTable();
        Exec("CREATE VIEW cheap_catalog AS SELECT id, sku FROM catalog WHERE price < 10;");

        var schema = await Extract();

        Assert.True(schema.Tables["cheap_catalog"].IsView);
        Assert.False(schema.Tables["catalog"].IsView);
        Assert.Equal("varchar", Column(schema, "cheap_catalog", "sku").DbType);
    }

    [Fact]
    public async Task SqliteInternalTablesAreExcluded()
    {
        Exec("CREATE TABLE counters (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT);");
        Exec("INSERT INTO counters (name) VALUES ('first');");

        var schema = await Extract();

        Assert.Contains("counters", schema.Tables.Keys);
        Assert.DoesNotContain(schema.Tables.Keys, k => k.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GeneratedColumnIsExtractedAndFlaggedComputed()
    {
        Exec(@"CREATE TABLE lines (
            qty INTEGER NOT NULL,
            unit_price DECIMAL(10,2) NOT NULL,
            line_total DECIMAL(10,2) GENERATED ALWAYS AS (qty * unit_price) STORED);");

        var schema = await Extract();

        Assert.True(Column(schema, "lines", "line_total").IsComputed);
        Assert.False(Column(schema, "lines", "qty").IsComputed);
    }

    [Fact]
    public async Task UniqueIndexIsCaptured_WithKeyColumnsInOrder()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, tenant TEXT, email TEXT);");
        Exec("CREATE UNIQUE INDEX ux_people_tenant_email ON people (tenant, email);");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ux_people_tenant_email");

        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "tenant", "email" }, index.Columns);
    }

    [Fact]
    public async Task PartialUniqueIndexIsCaptured_ButNotAsUnique()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT, deleted_at TEXT);");
        Exec("CREATE UNIQUE INDEX ux_people_email ON people (email) WHERE deleted_at IS NULL;");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ux_people_email");

        Assert.False(index.IsUnique);
        Assert.Equal(new[] { "email" }, index.Columns);
    }

    [Fact]
    public async Task AnAllExpressionUniqueIndexIsCaptured_WithNoColumnsAndTheExpressionFlag()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);");
        Exec("CREATE UNIQUE INDEX ux_people_lower_email ON people (lower(email));");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ux_people_lower_email");

        Assert.True(index.IsUnique);
        Assert.True(index.HasExpressionKeyPart);
        Assert.Empty(index.Columns);
    }

    [Fact]
    public async Task AMixedExpressionIndexKeepsOnlyItsRealColumns_AndIsFlagged()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, tenant TEXT, email TEXT);");
        Exec("CREATE UNIQUE INDEX ux_people_tenant_lower_email ON people (tenant, lower(email));");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ux_people_tenant_lower_email");

        Assert.True(index.HasExpressionKeyPart);
        Assert.Equal(new[] { "tenant" }, index.Columns);
    }

    [Fact]
    public async Task APartialAllExpressionUniqueIndexIsCaptured_ButNotAsUnique()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT, deleted_at TEXT);");
        Exec("CREATE UNIQUE INDEX ux_people_lower_email ON people (lower(email)) WHERE deleted_at IS NULL;");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ux_people_lower_email");

        Assert.False(index.IsUnique);
        Assert.True(index.HasExpressionKeyPart);
    }

    [Fact]
    public async Task APlainIndexIsNotFlaggedAsHavingAnExpressionKeyPart()
    {
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);");
        Exec("CREATE INDEX ix_people_email ON people (email);");

        var index = Assert.Single(
            (await Extract()).Tables["people"].Indexes,
            i => i.Name == "ix_people_email");

        Assert.False(index.HasExpressionKeyPart);
        Assert.False(index.IsUnique);
        Assert.Equal(new[] { "email" }, index.Columns);
    }

    [Fact]
    public async Task ForeignKeysAreCaptured()
    {
        Exec("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT);");
        Exec(@"CREATE TABLE orders (
            id INTEGER PRIMARY KEY,
            customer_id INTEGER NOT NULL REFERENCES customers(id));");

        var fk = Assert.Single((await Extract()).ForeignKeys);

        Assert.Equal("orders", fk.FromTable);
        Assert.Equal("customer_id", fk.FromColumn);
        Assert.Equal("customers", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }

    [Fact]
    public async Task QuotedIdentifiersWithEmbeddedQuotes_AreExtracted()
    {
        Exec("CREATE TABLE \"odd\"\"name\" (id INTEGER PRIMARY KEY, label TEXT NOT NULL);");

        var schema = await Extract();

        Assert.Equal("varchar", Column(schema, "odd\"name", "label").DbType);
    }

    [Fact]
    public async Task SqliteCapturesNoFunctionsOrUserTypes()
    {
        // Spec 014's deliberate no-op, asserted so it stays deliberate.
        // SQLite has no CREATE FUNCTION and no user-defined types, so the two
        // new dictionaries stay empty here -- and "empty because the engine
        // has none" is indistinguishable in a snapshot from "empty because
        // nobody wired the fourth extractor up". Only a test tells them apart,
        // and only this test fails if a later change starts populating either
        // from SQLite's pragma output.
        Exec("CREATE TABLE people (id INTEGER PRIMARY KEY, email TEXT);");

        var schema = await Extract();

        Assert.Empty(schema.Functions);
        Assert.Empty(schema.UserTypes);
    }
}
