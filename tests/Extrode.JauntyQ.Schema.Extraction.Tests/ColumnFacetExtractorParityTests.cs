using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

// §2.7 extractor parity sweep: "remaining facet x extractor cells" mandate row.
// IsComputed already had a live-verified cell for SqliteExtractor
// (AUD-R5-04); this closes the same facet's other 3 cells (SqlServer,
// Postgres, MySql), none of which had a live regression before now despite
// IsComputed feeding AutoCrud's INSERT/UPDATE column-exclusion list directly
// -- the same failure class the audit criteria's own calibration anchors
// call HIGH when a computed column is silently mishandled elsewhere
// (MigrationParser). All three extractors were read in full and are
// structurally sound (SQL Server: COLUMNPROPERTY(...,'IsComputed');
// Postgres: information_schema.columns.is_generated = 'ALWAYS'; MySql:
// INFORMATION_SCHEMA.COLUMNS.EXTRA LIKE '%GENERATED%') -- these tests prove
// it live rather than leaving it an unverified read.
public sealed class SqlServerComputedColumnFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MsSql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_computed_column");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE orders (
                id int NOT NULL PRIMARY KEY,
                qty int NOT NULL,
                price decimal(10,2) NOT NULL,
                total AS (qty * price) PERSISTED
            );
            -- A real rowversion column, for the IsRowVersion facet's own cell.
            CREATE TABLE versioned (id int NOT NULL PRIMARY KEY, rv rowversion);";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerComputedColumnExtractorTests : IClassFixture<SqlServerComputedColumnFixture>
{
    private readonly SqlServerComputedColumnFixture _fx;
    public SqlServerComputedColumnExtractorTests(SqlServerComputedColumnFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task PersistedComputedColumn_IsComputedTrue_OrdinaryColumnsFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);
        var orders = schema.Tables["orders"];

        Assert.True(orders.Columns["total"].IsComputed);
        Assert.False(orders.Columns["qty"].IsComputed);
        Assert.False(orders.Columns["price"].IsComputed);
    }

    [SkippableFact]
    public async Task RowVersionColumn_IsRowVersionTrue_OrdinaryColumnFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);
        var versioned = schema.Tables["versioned"];

        Assert.True(versioned.Columns["rv"].IsRowVersion);
        Assert.False(versioned.Columns["id"].IsRowVersion);
    }
}

public sealed class PostgresComputedColumnFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.Postgres;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_computed_column");

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE orders (
                id int PRIMARY KEY,
                qty int NOT NULL,
                price numeric(10,2) NOT NULL,
                total numeric GENERATED ALWAYS AS (qty * price) STORED
            );
            -- A real Postgres 'timestamp' column: same type-name string SQL
            -- Server's rowversion reports, per ColumnSchema.IsRowVersion's own
            -- doc comment -- proves no cross-dialect name collision, since
            -- PostgresExtractor never computes IsRowVersion at all (always
            -- defaults false here).
            CREATE TABLE events (id int PRIMARY KEY, occurred_at timestamp);";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class PostgresComputedColumnExtractorTests : IClassFixture<PostgresComputedColumnFixture>
{
    private readonly PostgresComputedColumnFixture _fx;
    public PostgresComputedColumnExtractorTests(PostgresComputedColumnFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GeneratedStoredColumn_IsComputedTrue_OrdinaryColumnsFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        var orders = schema.Tables["orders"];

        Assert.True(orders.Columns["total"].IsComputed);
        Assert.False(orders.Columns["qty"].IsComputed);
        Assert.False(orders.Columns["price"].IsComputed);
    }

    [SkippableFact]
    public async Task TimestampColumn_IsRowVersionStaysFalse_NoCrossDialectNameCollision()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        Assert.False(schema.Tables["events"].Columns["occurred_at"].IsRowVersion);
    }
}

public sealed class MySqlComputedColumnFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MySql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_computed_column");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE orders (
                id int NOT NULL PRIMARY KEY,
                qty int NOT NULL,
                price decimal(10,2) NOT NULL,
                total decimal(12,2) GENERATED ALWAYS AS (qty * price) STORED
            );";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlComputedColumnExtractorTests : IClassFixture<MySqlComputedColumnFixture>
{
    private readonly MySqlComputedColumnFixture _fx;
    public MySqlComputedColumnExtractorTests(MySqlComputedColumnFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GeneratedStoredColumn_IsComputedTrue_OrdinaryColumnsFalse()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var orders = schema.Tables["orders"];

        Assert.True(orders.Columns["total"].IsComputed);
        Assert.False(orders.Columns["qty"].IsComputed);
        Assert.False(orders.Columns["price"].IsComputed);

        // MySqlExtractor never computes IsRowVersion at all (a SQL-Server-only
        // concept per ColumnSchema.IsRowVersion's own doc comment) -- closes
        // that facet's MySql cell as a proven N/A rather than an unexamined gap.
        Assert.False(orders.Columns["qty"].IsRowVersion);
    }
}

// Round 20 (AUD-R20-01): live regression for the MySQL/MariaDB TINYINT(1)
// silent-corruption finding. Confirmed live against a real MySQL 8.0
// container (via raw docker, outside this test suite) that MySqlConnector's
// default "Treat Tiny As Boolean=true" connection option makes every typed
// reader accessor -- not just GetValue()/GetBoolean(), but GetInt16() and
// GetByte() too -- silently coerce any TINYINT(1)/BOOLEAN column's stored
// value down to 0 or 1, discarding values like 2, -1, or 127 with no
// exception and no diagnostic. Since JauntyDb wraps a connection the
// consumer supplies, JauntyQ cannot force that setting off; the only fix
// available is to have the generator emit reader.GetBoolean(...) (via a
// "bool" C# mapping) instead of reader.GetInt16(...) for such columns.
// MySqlExtractor can only make that call correctly if it first surfaces the
// TINYINT(1) display-width signal -- NUMERIC_PRECISION is always 3 for any
// tinyint regardless of width, so COLUMN_TYPE = 'tinyint(1)' is the only
// live channel -- which this fixture proves it now does, folding it into
// the existing Precision facet (the same channel "bit(n)" already uses,
// since NormalizeDbType strips any "(1)" suffix from the dbType string
// itself before DialectMapper ever sees it).
public sealed class MySqlTinyint1Fixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MySql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_tinyint1");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE staff_like (
                id int NOT NULL PRIMARY KEY,
                active TINYINT(1) NOT NULL DEFAULT 1,
                is_admin BOOLEAN NOT NULL DEFAULT 0,
                retry_count TINYINT NOT NULL DEFAULT 0
            );";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[Trait("Category", "AuditRegression")]
public class MySqlTinyint1ExtractorTests : IClassFixture<MySqlTinyint1Fixture>
{
    private readonly MySqlTinyint1Fixture _fx;
    public MySqlTinyint1ExtractorTests(MySqlTinyint1Fixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Tinyint1AndBooleanColumns_CapturePrecisionOne_PlainTinyintDoesNot()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var staffLike = schema.Tables["staff_like"];

        // TINYINT(1) and its BOOLEAN/BOOL DDL synonym (the server desugars
        // BOOLEAN to tinyint(1) at parse time) must both surface the
        // display-width-1 signal via Precision, so DialectMapper can route
        // them to "bool" instead of the corrupting "short" mapping.
        Assert.Equal(1, staffLike.Columns["active"].Precision);
        Assert.Equal(1, staffLike.Columns["is_admin"].Precision);

        // A plain, unspecified-width TINYINT must NOT be mistaken for
        // TINYINT(1) -- MySQL 8's default display width for an unspecified
        // TINYINT is not 1, and this column's legitimate range (e.g. a
        // retry counter that can legitimately hit 2, 3, ...) must keep
        // reading back correctly via the existing "short" mapping.
        Assert.NotEqual(1, staffLike.Columns["retry_count"].Precision);
    }
}
