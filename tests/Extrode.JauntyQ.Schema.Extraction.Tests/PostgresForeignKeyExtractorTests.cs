using System.Linq;
using Extrode.JauntyQ.Schema.Extraction;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Boots a real PostgreSQL, creates a composite-key foreign key, and exposes
/// the connection string for the extractor. Soft-skips when Docker is
/// unavailable, matching the other Testcontainers-backed fixtures in this
/// project (see <see cref="PostgresSequenceFixture"/>).
/// </summary>
public sealed class PostgresForeignKeyFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_foreign_key");

        // Seed DDL runs outside the skip guard: a failure is a real bug, not a
        // Docker-absent skip.
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE fk_test_parent (a int, b int, PRIMARY KEY (a, b));
            CREATE TABLE fk_test_child (x int, y int, FOREIGN KEY (x, y) REFERENCES fk_test_parent(a, b));
            CREATE TABLE fk_test_simple_parent (id serial PRIMARY KEY);
            CREATE TABLE fk_test_simple_child (parent_id int REFERENCES fk_test_simple_parent(id));";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// information_schema.constraint_column_usage carries no ordinal correlation
/// to key_column_usage, so joining the two on constraint_name alone
/// cross-products every FK column against every referenced column of a
/// composite key. Confirmed empirically against a live Postgres 16 container
/// during the audit that found this: a 2-column FK produced 4 rows instead
/// of the correct 2, pairing e.g. child.x against both parent.a and
/// parent.b. These tests pin the corrected extraction (via
/// referential_constraints + position_in_unique_constraint) for both a
/// composite FK and the ordinary single-column case.
/// </summary>
public class PostgresForeignKeyExtractorTests : IClassFixture<PostgresForeignKeyFixture>
{
    private readonly PostgresForeignKeyFixture _fx;
    public PostgresForeignKeyExtractorTests(PostgresForeignKeyFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CompositeForeignKey_ExtractsExactlyTwoCorrectlyPairedColumns()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

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

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        var fk = Assert.Single(schema.ForeignKeys, fk => fk.FromTable == "fk_test_simple_child");
        Assert.Equal("parent_id", fk.FromColumn);
        Assert.Equal("fk_test_simple_parent", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }
}
