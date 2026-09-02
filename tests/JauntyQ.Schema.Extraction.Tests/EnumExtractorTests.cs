using JauntyQ.Schema.Extraction;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Boots a real PostgreSQL carrying a native enum type, one shared by two
/// columns across two tables, one declared but referenced by nothing, and one
/// with members that do not fold cleanly. Soft-skips when Docker is
/// unavailable, matching the sequence fixtures' pattern.
/// </summary>
public sealed class PostgresEnumFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_enum");

        // Seed DDL runs outside the skip guard: a failure here is a real bug.
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TYPE order_status AS ENUM ('pending','shipped','canceled');
            CREATE TYPE awkward_kind AS ENUM ('in progress','2xl','plain');
            CREATE TYPE never_used AS ENUM ('a','b');

            CREATE TABLE orders (
                id serial PRIMARY KEY,
                status order_status NOT NULL,
                fallback_status order_status NULL,
                note text
            );

            CREATE TABLE shipments (
                id serial PRIMARY KEY,
                status order_status NOT NULL,
                kind awkward_kind NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class PostgresEnumExtractorTests : IClassFixture<PostgresEnumFixture>
{
    private readonly PostgresEnumFixture _fx;
    public PostgresEnumExtractorTests(PostgresEnumFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CapturesMembersInDeclarationOrder()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Enums.ContainsKey("order_status"));
        Assert.Equal(
            new[] { "pending", "shipped", "canceled" },
            schema.Enums["order_status"].Members.Select(m => m.Value));
    }

    [SkippableFact]
    public async Task TagsEveryColumnOfThatTypeAcrossEveryTable()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        // FR-013: one shared type, one entity, three columns pointing at it.
        Assert.Equal("order_status", schema.Tables["orders"].Columns["status"].EnumName);
        Assert.Equal("order_status", schema.Tables["orders"].Columns["fallback_status"].EnumName);
        Assert.Equal("order_status", schema.Tables["shipments"].Columns["status"].EnumName);
        Assert.Single(schema.Enums, e => e.Key == "order_status");
    }

    [SkippableFact]
    public async Task NonEnumColumnsAreLeftUntagged()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Null(schema.Tables["orders"].Columns["note"].EnumName);
        Assert.Null(schema.Tables["orders"].Columns["id"].EnumName);
    }

    [SkippableFact]
    public async Task ADeclaredButUnusedTypeIsStillCaptured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Decided in 013-plan.md: capture everything, emit only what a column
        // references. An unused type is still schema, and dropping it is still
        // drift.
        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.True(schema.Enums.ContainsKey("never_used"));
        Assert.DoesNotContain(schema.Tables.SelectMany(t => t.Value.Columns.Values),
                              c => c.EnumName == "never_used");
    }

    [SkippableFact]
    public async Task MembersCarryBothTheWireValueAndTheFoldedIdentifier()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new PostgresExtractor().ExtractAsync(_fx.ConnectionString);
        var members = schema.Enums["awkward_kind"].Members;

        Assert.Equal(new[] { "in progress", "2xl", "plain" }, members.Select(m => m.Value));
        Assert.Equal(new[] { "InProgress", "_2xl", "Plain" }, members.Select(m => m.CSharpName));
    }
}

/// <summary>
/// Boots a real MySQL with inline ENUM columns, a SET column that must stay
/// out, and an awkward member list. Soft-skips when Docker is unavailable.
/// </summary>
public sealed class MySqlEnumFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_enum");

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE orders (
                id int PRIMARY KEY,
                status ENUM('pending','shipped','canceled') NOT NULL,
                tags SET('red','green') NULL,
                note text
            );
            CREATE TABLE users (
                id int PRIMARY KEY,
                status ENUM('pending','shipped','canceled') NOT NULL
            );
            CREATE TABLE oddities (
                id int PRIMARY KEY,
                v ENUM('a,b','it''s','','in-progress') NOT NULL
            );";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MySqlEnumExtractorTests : IClassFixture<MySqlEnumFixture>
{
    private readonly MySqlEnumFixture _fx;
    public MySqlEnumExtractorTests(MySqlEnumFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CapturesAnInlineEnumNamedForItsTableAndColumn()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Equal("OrdersStatus", schema.Tables["orders"].Columns["status"].EnumName);
        Assert.Equal(
            new[] { "pending", "shipped", "canceled" },
            schema.Enums["OrdersStatus"].Members.Select(m => m.Value));
    }

    [SkippableFact]
    public async Task IdenticalMemberListsOnTwoTablesStayTwoEntities()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Equal("OrdersStatus", schema.Tables["orders"].Columns["status"].EnumName);
        Assert.Equal("UsersStatus", schema.Tables["users"].Columns["status"].EnumName);
        Assert.True(schema.Enums.ContainsKey("OrdersStatus"));
        Assert.True(schema.Enums.ContainsKey("UsersStatus"));
    }

    [SkippableFact]
    public async Task SetIsNotCapturedAsAnEnum()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // FR-011. The column must survive with its existing mapping intact.
        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Null(schema.Tables["orders"].Columns["tags"].EnumName);
        Assert.Equal("set", schema.Tables["orders"].Columns["tags"].DbType);
        Assert.DoesNotContain(schema.Enums.Keys, k => k.Contains("Tags"));
    }

    [SkippableFact]
    public async Task AwkwardMembersSurviveExtractionVerbatim()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // The commas, the doubled quote and the empty member are exactly what
        // a naive split on ',' destroys.
        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Equal(
            new[] { "a,b", "it's", "", "in-progress" },
            schema.Enums["OdditiesV"].Members.Select(m => m.Value));
        Assert.Equal(
            new[] { "AB", "ItS", "_", "InProgress" },
            schema.Enums["OdditiesV"].Members.Select(m => m.CSharpName));
    }

    [SkippableFact]
    public async Task NonEnumColumnsAreLeftUntagged()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var schema = await new MySqlExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Null(schema.Tables["orders"].Columns["note"].EnumName);
        Assert.Null(schema.Tables["orders"].Columns["id"].EnumName);
    }
}

/// <summary>
/// SQL Server has no native enum, so the dictionary must stay empty rather
/// than acquire anything incidental. The negative control for FR-015.
/// </summary>
public sealed class SqlServerNoEnumFixture : IAsyncLifetime
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
        ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_no_enum");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE orders (id int PRIMARY KEY, status varchar(20) NOT NULL CHECK (status IN ('pending','shipped')));";
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class SqlServerNoEnumExtractorTests : IClassFixture<SqlServerNoEnumFixture>
{
    private readonly SqlServerNoEnumFixture _fx;
    public SqlServerNoEnumExtractorTests(SqlServerNoEnumFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CheckConstraintPseudoEnumIsNotCaptured()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        // Explicitly out of scope for spec 013: recovering members from a
        // CHECK expression means parsing open-ended SQL. The column keeps its
        // varchar mapping.
        var schema = await new SqlServerExtractor().ExtractAsync(_fx.ConnectionString);

        Assert.Empty(schema.Enums);
        Assert.Null(schema.Tables["orders"].Columns["status"].EnumName);
    }
}
