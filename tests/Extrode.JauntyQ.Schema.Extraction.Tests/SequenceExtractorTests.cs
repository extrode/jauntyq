using Extrode.JauntyQ.Schema.Extraction;
using Extrode.JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Boots a real SQL Server, creates a sequence, and exposes the connection
/// string so the extractor can be run against it. Soft-skips (via
/// <see cref="Available"/>) when Docker is unavailable, matching the sample
/// fixtures' pattern so local `dotnet test` stays green without a daemon.
/// </summary>
public sealed class SqlServerSequenceFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        // Only container/Docker bring-up is infra: a failure here soft-skips
        // locally and (via FixtureGate) rethrows under CI.
        var engine = await EngineContainers.MsSql;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_sequence");

        // Seed DDL runs OUTSIDE the skip guard: a CREATE SEQUENCE failure is a
        // real bug (bad DDL / server misconfig), and must fail the suite loudly
        // rather than be silently reclassified as "Docker unavailable".
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE SEQUENCE test_seq START WITH 100 INCREMENT BY 5;";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Boots a real PostgreSQL, creates a sequence, and exposes the connection
/// string for the extractor. Soft-skips when Docker is unavailable.
/// </summary>
public sealed class PostgresSequenceFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_sequence");

        // Seed DDL runs outside the skip guard (see the SQL Server fixture above).
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE SEQUENCE test_seq START 100 INCREMENT 5;";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// §4.4 sibling-sweep of AUD-R10-01 (same 2.7 row: sequenceSchema x 4
/// extractors): real/Oracle MySQL has no native sequence concept at all, so
/// MySqlExtractor never querying for one is a legitimate N/A for that
/// dialect. But MariaDB -- which JauntyQ intentionally maps to the SAME
/// "mysql" dialect string (see DialectMapper.KnownDialects's own doc
/// comment: MariaDB is wire/SQL-compatible for everything the generator
/// emits) and which has its own row in the sample test matrix -- has
/// supported native CREATE SEQUENCE since 10.3, specifically BECAUSE of
/// behavioral divergences like this one. Boots a real MariaDB, creates a
/// sequence, and exposes the connection string for MySqlExtractor. Uses
/// MySqlConnector/MySqlConnection throughout (matching
/// SakilaMariaDbFixture's established pattern) since MariaDB is wire-
/// protocol-compatible with MySQL. Soft-skips when Docker is unavailable.
/// </summary>
public sealed class MariaDbSequenceFixture : IAsyncLifetime
{
    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var engine = await EngineContainers.MariaDb;
        if (!engine.Available)
        {
            SkipReason = engine.SkipReason;
            return;
        }
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_sequence");

        // Seed DDL runs outside the skip guard (see the SQL Server fixture above).
        // MariaDB's native (non-Oracle-mode) sequence DDL syntax.
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE SEQUENCE test_seq START WITH 100 INCREMENT BY 5;";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Boots a real (Oracle) MySQL -- no sequence concept at all, no seed DDL
/// needed -- to close the sequenceSchema x MySqlExtractor cell's other half
/// as a proven N/A: AUD-R10-02's fix (querying
/// INFORMATION_SCHEMA.TABLES for TABLE_TYPE = 'SEQUENCE') must keep
/// returning zero rows against real MySQL, not just against MariaDB.
/// </summary>
public sealed class MySqlNoSequenceFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_no_sequence");

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerSequenceExtractorTests : IClassFixture<SqlServerSequenceFixture>
{
    private readonly SqlServerSequenceFixture _fx;
    public SqlServerSequenceExtractorTests(SqlServerSequenceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsSequence_WithStartAndIncrement()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Sequences.ContainsKey("test_seq"));
        var seq = schema.Sequences["test_seq"];
        Assert.Equal(100, seq.StartValue);
        Assert.Equal(5, seq.Increment);
    }
}

public class PostgresSequenceExtractorTests : IClassFixture<PostgresSequenceFixture>
{
    private readonly PostgresSequenceFixture _fx;
    public PostgresSequenceExtractorTests(PostgresSequenceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsSequence_WithStartAndIncrement()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Sequences.ContainsKey("test_seq"));
        var seq = schema.Sequences["test_seq"];
        Assert.Equal(100, seq.StartValue);
        Assert.Equal(5, seq.Increment);
    }
}

[Trait("Category", "AuditRegression")]
public class MariaDbSequenceExtractorTests : IClassFixture<MariaDbSequenceFixture>
{
    private readonly MariaDbSequenceFixture _fx;
    public MariaDbSequenceExtractorTests(MariaDbSequenceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task ExtractsSequence_WithStartAndIncrement()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        // AUD-R10-02: before the fix, MySqlExtractor had zero
        // sequence-extraction code at all -- this assertion is the one that
        // failed pre-fix (confirmed: schema.Sequences was empty even though
        // test_seq genuinely existed server-side), a real information-loss
        // bug specific to the "mysql" dialect's MariaDB variant.
        Assert.True(schema.Sequences.ContainsKey("test_seq"));
        var seq = schema.Sequences["test_seq"];
        Assert.Equal(100, seq.StartValue);
        Assert.Equal(5, seq.Increment);
    }
}

public class MySqlNoSequenceExtractorTests : IClassFixture<MySqlNoSequenceFixture>
{
    private readonly MySqlNoSequenceFixture _fx;
    public MySqlNoSequenceExtractorTests(MySqlNoSequenceFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Sequences_AlwaysEmpty_NoSequenceConceptInRealMySql()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        // AUD-R10-02's fix (INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE =
        // 'SEQUENCE') must stay a correct no-op against real MySQL, which
        // never produces that TABLE_TYPE value.
        Assert.Empty(schema.Sequences);
    }
}
