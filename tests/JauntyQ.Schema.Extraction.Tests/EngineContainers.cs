using System;
using System.Threading;
using System.Threading.Tasks;
using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Testcontainers.MariaDb;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace JauntyQ.Schema.Extraction.Tests;

public enum EngineKind { MsSql, Postgres, MySql, MariaDb }

/// <summary>
/// The shared engine a fixture gets back from <see cref="EngineContainers"/>.
/// When <see cref="Available"/> is false, <see cref="SkipReason"/> carries the
/// classified "Docker unavailable" message every fixture on this engine must
/// report; when true, <see cref="AdminConnectionString"/> connects as a user
/// that may CREATE DATABASE.
/// </summary>
public sealed record EngineHandle(EngineKind Kind, bool Available, string? SkipReason, string AdminConnectionString);

/// <summary>
/// One Testcontainers container per database engine, shared by every fixture in
/// this assembly, with one database per fixture inside it. Before 2026-07-31
/// each of the ~22 fixtures here started its own container; a full-solution run
/// peaked at 17 concurrent containers, and the 4-collection cap that bounded it
/// (xunit.runner.json) cost 56s → 1m33s. Database-level isolation is sound
/// because every extractor scopes to the connected database (MySql:
/// TABLE_SCHEMA = @db; Postgres: table_schema/nspname within the connected DB;
/// SqlServer: the connection's catalog).
///
/// Deliberately a static lazy rather than an xUnit collection fixture: a
/// collection per engine would serialize every test class on that engine,
/// destroying exactly the parallelism the consolidation is meant to restore.
/// Disposal happens in EngineContainersTestFramework's assembly runner, at
/// BeforeTestAssemblyFinishedAsync, because nothing else works here: measured
/// on this host (2026-07-31), Testcontainers' Ryuk reaper never starts (no
/// ryuk container has ever existed in `docker ps -a`) and an
/// AppDomain.ProcessExit handler never ran either (a run with one still leaked
/// all four engines) — the per-fixture DisposeAsync calls the consolidation
/// removed were the only thing cleaning up before.
///
/// Bring-up failure keeps FixtureGate's semantics exactly: a genuinely absent
/// daemon yields a cached Available=false handle (every fixture on the engine
/// soft-skips with the same reason), while any other failure — and any failure
/// under CI — rethrows out of <see cref="FixtureGate.SkipReasonOrThrow"/>,
/// faulting the lazy task so every fixture on the engine fails loudly.
/// </summary>
public static class EngineContainers
{
    public static Task<EngineHandle> MsSql => _msSql.Value;
    public static Task<EngineHandle> Postgres => _postgres.Value;
    public static Task<EngineHandle> MySql => _mySql.Value;
    public static Task<EngineHandle> MariaDb => _mariaDb.Value;

    /// <summary>
    /// Creates the fixture's private database on the shared engine and returns
    /// a connection string pointing at it. The caller's name is a readable base;
    /// a process-wide counter is appended because IClassFixture instantiates a
    /// fixture once per test CLASS, not per type — a fixture type shared by two
    /// test classes (EngineContainersIsolationTests reuses two Postgres
    /// fixtures) runs InitializeAsync twice, and the second CREATE DATABASE of
    /// a bare deterministic name would fail. Failure here is a real bug, never
    /// a skip — it runs outside any skip classification, matching the
    /// seed-outside-the-catch policy FixtureContainerBuildSiteTests enforces.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(EngineHandle engine, string databaseName)
    {
        databaseName = $"{databaseName}_{Interlocked.Increment(ref _databaseCounter)}";
        switch (engine.Kind)
        {
            case EngineKind.MsSql:
            {
                await using var conn = new SqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{databaseName}]";
                await cmd.ExecuteNonQueryAsync();
                return new SqlConnectionStringBuilder(engine.AdminConnectionString) { InitialCatalog = databaseName }.ConnectionString;
            }
            case EngineKind.Postgres:
            {
                await using var conn = new NpgsqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
                await cmd.ExecuteNonQueryAsync();
                return new NpgsqlConnectionStringBuilder(engine.AdminConnectionString) { Database = databaseName }.ConnectionString;
            }
            default: // MySql and MariaDb share the provider and the quoting.
            {
                await using var conn = new MySqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE `{databaseName}`";
                await cmd.ExecuteNonQueryAsync();
                return new MySqlConnectionStringBuilder(engine.AdminConnectionString) { Database = databaseName }.ConnectionString;
            }
        }
    }

    private static int _databaseCounter;

    private static readonly System.Collections.Concurrent.ConcurrentBag<DotNet.Testcontainers.Containers.IDatabaseContainer> _started = new();

    /// <summary>
    /// Called once by EngineContainersTestFramework when the assembly run
    /// finishes. Best effort: a container that fails to stop has nowhere to
    /// report at teardown, and the run's verdict is already decided.
    /// </summary>
    internal static async Task DisposeAllAsync()
    {
        foreach (var container in _started)
        {
            try { await ((IAsyncDisposable)container).DisposeAsync(); }
            catch { /* best effort at teardown */ }
        }
    }

    private static readonly Lazy<Task<EngineHandle>> _msSql =
        new(() => StartEngineAsync(EngineKind.MsSql), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Task<EngineHandle>> _postgres =
        new(() => StartEngineAsync(EngineKind.Postgres), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Task<EngineHandle>> _mySql =
        new(() => StartEngineAsync(EngineKind.MySql), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Task<EngineHandle>> _mariaDb =
        new(() => StartEngineAsync(EngineKind.MariaDb), LazyThreadSafetyMode.ExecutionAndPublication);

    // Image pins are the union of what the per-fixture builders used before the
    // consolidation: MsSql and MySql defaults, postgres:16-alpine everywhere
    // Postgres appeared, mariadb:11 (the sequence fixture's pin; the upsert
    // probe ran the builder default and moves onto 11 with it). MySql/MariaDb
    // connect as root because the fixtures' databases are created at run time,
    // and the builders' default non-root user has rights only on its own
    // pre-declared database.
    private static async Task<EngineHandle> StartEngineAsync(EngineKind kind)
    {
        DotNet.Testcontainers.Containers.IDatabaseContainer container;
        try
        {
            container = kind switch
            {
                EngineKind.MsSql => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build(),
                EngineKind.Postgres => new PostgreSqlBuilder("postgres:16-alpine").Build(),
                EngineKind.MySql => new MySqlBuilder("mysql:8.0").WithUsername("root").Build(),
                EngineKind.MariaDb => new MariaDbBuilder("mariadb:11").WithUsername("root").Build(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            _started.Add(container);
            await ((DotNet.Testcontainers.Containers.IContainer)container).StartAsync();
        }
        catch (Exception ex)
        {
            return new EngineHandle(kind, Available: false, FixtureGate.SkipReasonOrThrow(ex), AdminConnectionString: "");
        }

        return new EngineHandle(kind, Available: true, SkipReason: null, container.GetConnectionString());
    }
}
