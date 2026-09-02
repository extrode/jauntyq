using System.Linq;
using JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// A filtered/partial unique index (SQL Server: CREATE UNIQUE INDEX ... WHERE
/// ...; Postgres: same syntax) only enforces uniqueness among the rows
/// matching its predicate, not the whole table. Confirmed live (Testcontainers
/// SQL Server and Postgres) that both extractors reported such an index as
/// IsUnique=true with no signal at all that it was conditional -- which would
/// let UpsertKeyResolver/NPlusOneAnalyzer treat the column as a safe
/// globally-unique upsert/scoping key when two "soft-deleted" rows could
/// legitimately share it. Postgres additionally silently truncated a
/// composite expression index (customer_id, lower(email)) down to
/// cols=[customer_id] while still reporting IsUnique=true -- dangerously
/// wrong, since customer_id alone is never unique on its own. These tests
/// pin the corrected extraction for both hazards.
/// </summary>
public sealed class SqlServerFilteredIndexFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_filtered_index");

        // Seed DDL runs outside the skip guard: a failure is a real bug, not a
        // Docker-absent skip.
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            create table users (id int not null primary key, email nvarchar(100) null, deleted_at datetime null);
            create unique index ux_users_email_active on users(email) where deleted_at is null;
            create table plain_users (id int not null primary key, email nvarchar(100) not null);
            create unique index ux_plain_users_email on plain_users(email);";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerFilteredIndexExtractorTests : IClassFixture<SqlServerFilteredIndexFixture>
{
    private readonly SqlServerFilteredIndexFixture _fx;
    public SqlServerFilteredIndexExtractorTests(SqlServerFilteredIndexFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task FilteredUniqueIndex_IsNotReportedAsGloballyUnique()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_email_active");

        Assert.False(ix.IsUnique);
        Assert.Equal(new[] { "email" }, ix.Columns);
    }

    [SkippableFact]
    public async Task OrdinaryUniqueIndex_StillReportedAsUnique()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["plain_users"].Indexes, i => i.Name == "ux_plain_users_email");

        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "email" }, ix.Columns);
    }
}

public sealed class PostgresPartialIndexFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_partial_index");

        // Seed DDL runs outside the skip guard: a failure is a real bug, not a
        // Docker-absent skip.
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            create table users (id serial primary key, email varchar(100), deleted_at timestamp, customer_id int not null);
            create unique index ux_users_email_active on users(email) where deleted_at is null;
            create unique index ux_users_customer_lower_email on users(customer_id, lower(email));
            create unique index ux_users_lower_email_only on users(lower(email));
            create table plain_users (id serial primary key, email varchar(100) not null);
            create unique index ux_plain_users_email on plain_users(email);";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class PostgresPartialIndexExtractorTests : IClassFixture<PostgresPartialIndexFixture>
{
    private readonly PostgresPartialIndexFixture _fx;
    public PostgresPartialIndexExtractorTests(PostgresPartialIndexFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task PartialUniqueIndex_IsNotReportedAsGloballyUnique()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_email_active");

        Assert.False(ix.IsUnique);
        Assert.Equal(new[] { "email" }, ix.Columns);
    }

    [SkippableFact]
    public async Task CompositeExpressionIndex_IsRepresentedFlagged_NotTruncated()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        // The original bug returned this as a single-column IsUnique=true
        // index on customer_id alone -- actively dangerous, since customer_id
        // is never unique by itself. The first fix excluded the index, which
        // left a UNIQUE expression index invisible as a competing constraint;
        // since 2026-07-30 it is captured flagged with the real-column subset.
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_customer_lower_email");
        Assert.True(ix.HasExpressionKeyPart);
        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "customer_id" }, ix.Columns);

        Assert.DoesNotContain(schema.Tables["users"].Indexes,
            i => i.Columns.Count == 1 && i.Columns[0] == "customer_id" && i.IsUnique && !i.HasExpressionKeyPart);
    }

    [SkippableFact]
    public async Task AllExpressionUniqueIndex_IsCapturedFlagged_WithEmptyColumns()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_lower_email_only");

        Assert.True(ix.HasExpressionKeyPart);
        Assert.True(ix.IsUnique);
        Assert.Empty(ix.Columns);
    }

    [SkippableFact]
    public async Task OrdinaryUniqueIndex_StillReportedAsUnique()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["plain_users"].Indexes, i => i.Name == "ux_plain_users_email");

        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "email" }, ix.Columns);
    }
}

/// <summary>
/// A MySQL 8.0.13+ functional index key part (CREATE INDEX ... ON t (col,
/// (lower(other_col)))) has no real column: INFORMATION_SCHEMA.STATISTICS
/// .COLUMN_NAME is NULL for that position. Confirmed live (Testcontainers
/// MySQL) that the old extractor read COLUMN_NAME unconditionally and threw
/// InvalidCastException the moment ANY table in the schema had such an
/// index -- a hard crash that took down the entire extraction, not just a
/// wrong answer for that one index.
/// </summary>
public sealed class MySqlFunctionalIndexFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_functional_index");

        // Seed DDL runs outside the skip guard: a failure is a real bug, not a
        // Docker-absent skip.
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            create table users (id int not null primary key auto_increment, email varchar(100), customer_id int not null);
            create unique index ux_users_customer_lower_email on users(customer_id, (lower(email)));
            create unique index ux_users_lower_email_only on users((lower(email)));
            create table plain_users (id int not null primary key auto_increment, email varchar(100) not null);
            create unique index ux_plain_users_email on plain_users(email);";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlFunctionalIndexExtractorTests : IClassFixture<MySqlFunctionalIndexFixture>
{
    private readonly MySqlFunctionalIndexFixture _fx;
    public MySqlFunctionalIndexExtractorTests(MySqlFunctionalIndexFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task FunctionalIndex_DoesNotCrashExtraction_AndIsRepresentedFlagged()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Extraction completing at all is still half the point (the original
        // defect was an InvalidCastException that killed the whole run). The
        // index is now captured flagged with the real-column subset rather
        // than excluded (pre-2026-07-30), so a UNIQUE one stays visible as a
        // competing constraint -- and never as an unflagged unique singleton.
        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_customer_lower_email");
        Assert.True(ix.HasExpressionKeyPart);
        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "customer_id" }, ix.Columns);

        Assert.DoesNotContain(schema.Tables["users"].Indexes,
            i => i.Columns.Count == 1 && i.Columns[0] == "customer_id" && i.IsUnique && !i.HasExpressionKeyPart);
    }

    [SkippableFact]
    public async Task AllExpressionUniqueIndex_IsCapturedFlagged_WithEmptyColumns()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "ux_users_lower_email_only");

        Assert.True(ix.HasExpressionKeyPart);
        Assert.True(ix.IsUnique);
        Assert.Empty(ix.Columns);
    }

    [SkippableFact]
    public async Task OrdinaryUniqueIndex_StillReportedAsUnique()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["plain_users"].Indexes, i => i.Name == "ux_plain_users_email");

        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "email" }, ix.Columns);
    }

    [SkippableFact]
    public async Task PrimaryKeyIndex_StillCapturedOnTableWithFunctionalIndex()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);
        var ix = Assert.Single(schema.Tables["users"].Indexes, i => i.Name == "PRIMARY");

        Assert.True(ix.IsUnique);
        Assert.Equal(new[] { "id" }, ix.Columns);
    }
}
