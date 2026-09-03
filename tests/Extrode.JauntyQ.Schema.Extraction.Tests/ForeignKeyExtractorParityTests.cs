using System.Linq;
using Extrode.JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

// §2.7 extractor parity sibling-sweep (same declared mandate row as
// AUD-R10-01/02: fkSchema x 4 extractors). SqliteExtractor's FK extraction is
// already covered by ExtractorTests (self-join + multi-table); PostgresExtractor's
// composite-FK cross-product bug (a real, already-fixed defect from an earlier
// round) is covered by PostgresForeignKeyExtractorTests. This file closes out
// the remaining two cells: SqlServerExtractor and MySqlExtractor, both read in
// full and confirmed structurally sound for composite keys (sys.foreign_key_columns
// and INFORMATION_SCHEMA.KEY_COLUMN_USAGE both natively pair each FK column with
// its correct referenced column per-row -- neither has the "join loses ordinal
// correlation" hazard information_schema.constraint_column_usage had on
// Postgres), but neither had a live regression test proving it before now.
public sealed class SqlServerForeignKeyFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_foreign_key");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE fk_test_parent (a int NOT NULL, b int NOT NULL, PRIMARY KEY (a, b));
            CREATE TABLE fk_test_child (x int NOT NULL, y int NOT NULL, FOREIGN KEY (x, y) REFERENCES fk_test_parent(a, b));
            CREATE TABLE fk_test_simple_parent (id int NOT NULL IDENTITY PRIMARY KEY);
            CREATE TABLE fk_test_simple_child (parent_id int NULL REFERENCES fk_test_simple_parent(id));";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerForeignKeyExtractorTests : IClassFixture<SqlServerForeignKeyFixture>
{
    private readonly SqlServerForeignKeyFixture _fx;
    public SqlServerForeignKeyExtractorTests(SqlServerForeignKeyFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CompositeForeignKey_ExtractsExactlyTwoCorrectlyPairedColumns()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        var fks = schema.ForeignKeys
            .Where(fk => fk.FromTable == "fk_test_child")
            .OrderBy(fk => fk.FromColumn)
            .ToList();

        Assert.Equal(2, fks.Count);
        Assert.Equal(("fk_test_child", "x", "fk_test_parent", "a"),
            (fks[0].FromTable, fks[0].FromColumn, fks[0].ToTable, fks[0].ToColumn));
        Assert.Equal(("fk_test_child", "y", "fk_test_parent", "b"),
            (fks[1].FromTable, fks[1].FromColumn, fks[1].ToTable, fks[1].ToColumn));
    }

    [SkippableFact]
    public async Task SingleColumnForeignKey_StillExtractsExactlyOne()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        var fk = Assert.Single(schema.ForeignKeys, fk => fk.FromTable == "fk_test_simple_child");
        Assert.Equal("parent_id", fk.FromColumn);
        Assert.Equal("fk_test_simple_parent", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }
}

public sealed class MySqlForeignKeyFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_foreign_key");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE fk_test_parent (a int NOT NULL, b int NOT NULL, PRIMARY KEY (a, b));
            CREATE TABLE fk_test_child (x int NOT NULL, y int NOT NULL, FOREIGN KEY (x, y) REFERENCES fk_test_parent(a, b));
            CREATE TABLE fk_test_simple_parent (id int NOT NULL AUTO_INCREMENT PRIMARY KEY);
            CREATE TABLE fk_test_simple_child (parent_id int NULL, FOREIGN KEY (parent_id) REFERENCES fk_test_simple_parent(id));";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlForeignKeyExtractorTests : IClassFixture<MySqlForeignKeyFixture>
{
    private readonly MySqlForeignKeyFixture _fx;
    public MySqlForeignKeyExtractorTests(MySqlForeignKeyFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CompositeForeignKey_ExtractsExactlyTwoCorrectlyPairedColumns()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        var fks = schema.ForeignKeys
            .Where(fk => fk.FromTable == "fk_test_child")
            .OrderBy(fk => fk.FromColumn)
            .ToList();

        Assert.Equal(2, fks.Count);
        Assert.Equal(("fk_test_child", "x", "fk_test_parent", "a"),
            (fks[0].FromTable, fks[0].FromColumn, fks[0].ToTable, fks[0].ToColumn));
        Assert.Equal(("fk_test_child", "y", "fk_test_parent", "b"),
            (fks[1].FromTable, fks[1].FromColumn, fks[1].ToTable, fks[1].ToColumn));
    }

    [SkippableFact]
    public async Task SingleColumnForeignKey_StillExtractsExactlyOne()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        var fk = Assert.Single(schema.ForeignKeys, fk => fk.FromTable == "fk_test_simple_child");
        Assert.Equal("parent_id", fk.FromColumn);
        Assert.Equal("fk_test_simple_parent", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }
}
