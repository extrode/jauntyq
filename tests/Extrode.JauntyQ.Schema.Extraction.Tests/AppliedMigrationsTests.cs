using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class AppliedMigrationsTests
{
    public static TheoryData<EngineKind> Engines => new()
    {
        EngineKind.MsSql, EngineKind.Postgres, EngineKind.MySql, EngineKind.MariaDb,
    };

    private static Task<EngineHandle> Engine(EngineKind kind) => kind switch
    {
        EngineKind.MsSql => EngineContainers.MsSql,
        EngineKind.Postgres => EngineContainers.Postgres,
        EngineKind.MySql => EngineContainers.MySql,
        _ => EngineContainers.MariaDb,
    };

    private static string Dialect(EngineKind kind) => kind switch
    {
        EngineKind.MsSql => "sqlserver",
        EngineKind.Postgres => "postgres",
        _ => "mysql",
    };

    private static DbConnection Connect(EngineKind kind, string connectionString) => kind switch
    {
        EngineKind.MsSql => new SqlConnection(connectionString),
        EngineKind.Postgres => new NpgsqlConnection(connectionString),
        _ => new MySqlConnection(connectionString),
    };

    private static string CreateTable(EngineKind kind) => kind switch
    {
        EngineKind.MsSql => "CREATE TABLE schema_migrations (version nvarchar(255) NOT NULL PRIMARY KEY, applied_at datetime2 NOT NULL DEFAULT sysutcdatetime())",
        EngineKind.Postgres => "CREATE TABLE schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())",
        _ => "CREATE TABLE schema_migrations (version varchar(255) NOT NULL PRIMARY KEY, applied_at datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6))",
    };

    private static async Task ExecuteAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [SkippableTheory]
    [MemberData(nameof(Engines))]
    public async Task ADatabaseWithNoTable_ReadsAsNull(EngineKind kind)
    {
        var engine = await Engine(kind);
        Skip.IfNot(engine.Available, engine.SkipReason);
        string connection = await EngineContainers.CreateDatabaseAsync(engine, "fx_applied_none");

        Assert.Null(await AppliedMigrations.ReadAsync(Dialect(kind), connection));
    }

    [SkippableTheory]
    [MemberData(nameof(Engines))]
    public async Task EveryRecordedVersion_IsRead(EngineKind kind)
    {
        var engine = await Engine(kind);
        Skip.IfNot(engine.Available, engine.SkipReason);
        string connection = await EngineContainers.CreateDatabaseAsync(engine, "fx_applied_rows");
        await using (var conn = Connect(kind, connection))
        {
            await conn.OpenAsync();
            await ExecuteAsync(conn, CreateTable(kind));
            await ExecuteAsync(conn, "INSERT INTO schema_migrations (version) VALUES ('0001_init.sql')");
            await ExecuteAsync(conn, "INSERT INTO schema_migrations (version) VALUES ('0002_add_col.sql')");
        }

        var versions = await AppliedMigrations.ReadAsync(Dialect(kind), connection);

        Assert.Equal(new[] { "0001_init.sql", "0002_add_col.sql" }, versions!.OrderBy(v => v, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Sqlite_NoTableIsNull_ThenEveryRowIsRead()
    {
        string path = Path.Combine(Path.GetTempPath(), "jq-applied-" + Guid.NewGuid().ToString("N") + ".db");
        string connection = $"Data Source={path}";
        try
        {
            await using (var conn = new SqliteConnection(connection))
            {
                await conn.OpenAsync();
                await ExecuteAsync(conn, "CREATE TABLE schema_migrations_old (version TEXT)");
            }
            Assert.Null(await AppliedMigrations.ReadAsync("sqlite", connection));

            await using (var conn = new SqliteConnection(connection))
            {
                await conn.OpenAsync();
                await ExecuteAsync(conn, "CREATE TABLE schema_migrations (version TEXT NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)");
                await ExecuteAsync(conn, "INSERT INTO schema_migrations (version) VALUES ('V2__b.sql'), ('V1__a.sql')");
            }
            var versions = await AppliedMigrations.ReadAsync("sqlite", connection);

            Assert.Equal(new[] { "V1__a.sql", "V2__b.sql" }, versions!.OrderBy(v => v, StringComparer.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnUnknownDialect_Throws()
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AppliedMigrations.ReadAsync("oracle", "x"));

        Assert.Equal("dialect", ex.ParamName);
    }
}
