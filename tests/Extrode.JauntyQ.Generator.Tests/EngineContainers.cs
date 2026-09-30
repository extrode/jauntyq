using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Extrode.JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Extrode.JauntyQ.Generator.Tests;

public enum EngineKind { MsSql, Postgres, MySql }

public sealed record EngineHandle(EngineKind Kind, bool Available, string? SkipReason, string AdminConnectionString);

/// <summary>
/// One container per engine for the whole assembly, one database per fixture.
/// The same JAUNTYQ_TEST_ENGINE_* variables as Schema.Extraction.Tests point it
/// at long-lived servers (scripts/mutation-engines.sh), because every Stryker
/// test run otherwise starts its own containers.
/// </summary>
internal static class EngineContainers
{
    public static Task<EngineHandle> MsSql => _msSql.Value;
    public static Task<EngineHandle> Postgres => _postgres.Value;
    public static Task<EngineHandle> MySql => _mySql.Value;

    internal static readonly string RunToken = Guid.NewGuid().ToString("N").Substring(0, 8);

    private static int _databaseCounter;
    private static readonly ConcurrentBag<DotNet.Testcontainers.Containers.IDatabaseContainer> _started = new();
    private static readonly ConcurrentDictionary<EngineKind, bool> _externalKinds = new();
    private static readonly ConcurrentBag<(EngineHandle Engine, string Database)> _externalDatabases = new();

    public static async Task<string> CreateDatabaseAsync(EngineHandle engine, string databaseName)
    {
        databaseName = $"{databaseName}_{RunToken}_{Interlocked.Increment(ref _databaseCounter)}";
        if (_externalKinds.ContainsKey(engine.Kind))
            _externalDatabases.Add((engine, databaseName));
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
            default:
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

    internal static string ExternalEngineVariable(EngineKind kind) => kind switch
    {
        EngineKind.MsSql => "JAUNTYQ_TEST_ENGINE_SQLSERVER",
        EngineKind.Postgres => "JAUNTYQ_TEST_ENGINE_POSTGRES",
        EngineKind.MySql => "JAUNTYQ_TEST_ENGINE_MYSQL",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    internal static EngineHandle? ExternalEngine(EngineKind kind, Func<string, string?> getVariable)
    {
        string? connectionString = getVariable(ExternalEngineVariable(kind));
        return string.IsNullOrWhiteSpace(connectionString)
            ? null
            : new EngineHandle(kind, Available: true, SkipReason: null, connectionString);
    }

    internal static async Task DropDatabaseAsync(EngineHandle engine, string databaseName)
    {
        switch (engine.Kind)
        {
            case EngineKind.MsSql:
            {
                await using var conn = new SqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
                await cmd.ExecuteNonQueryAsync();
                return;
            }
            case EngineKind.Postgres:
            {
                await using var conn = new NpgsqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE \"{databaseName}\" WITH (FORCE)";
                await cmd.ExecuteNonQueryAsync();
                return;
            }
            default:
            {
                await using var conn = new MySqlConnection(engine.AdminConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE `{databaseName}`";
                await cmd.ExecuteNonQueryAsync();
                return;
            }
        }
    }

    internal static async Task DisposeAllAsync()
    {
        await Task.WhenAll(_externalDatabases.Select(async entry =>
        {
            try { await DropDatabaseAsync(entry.Engine, entry.Database); }
            catch { /* best effort at teardown */ }
        }));

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

    private static async Task<EngineHandle> StartEngineAsync(EngineKind kind)
    {
        var external = ExternalEngine(kind, Environment.GetEnvironmentVariable);
        if (external != null)
        {
            _externalKinds[kind] = true;
            return external;
        }

        DotNet.Testcontainers.Containers.IDatabaseContainer container;
        try
        {
            container = kind switch
            {
                EngineKind.MsSql => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build(),
                EngineKind.Postgres => new PostgreSqlBuilder("postgres:16-alpine").Build(),
                EngineKind.MySql => new MySqlBuilder("mysql:8.0").WithUsername("root").Build(),
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
