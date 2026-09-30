using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class EngineContainersExternalEngineTests
{
    [Theory]
    [InlineData(EngineKind.MsSql, "JAUNTYQ_TEST_ENGINE_SQLSERVER")]
    [InlineData(EngineKind.Postgres, "JAUNTYQ_TEST_ENGINE_POSTGRES")]
    [InlineData(EngineKind.MySql, "JAUNTYQ_TEST_ENGINE_MYSQL")]
    [InlineData(EngineKind.MariaDb, "JAUNTYQ_TEST_ENGINE_MARIADB")]
    public void ExternalEngine_UsesTheKindsVariable(EngineKind kind, string variable)
    {
        var values = new Dictionary<string, string?> { [variable] = "Server=elsewhere" };

        var handle = EngineContainers.ExternalEngine(kind, name => values.TryGetValue(name, out var v) ? v : null);

        Assert.Equal(new EngineHandle(kind, Available: true, SkipReason: null, "Server=elsewhere"), handle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExternalEngine_UnsetOrBlank_FallsBackToAContainer(string? value)
    {
        Assert.Null(EngineContainers.ExternalEngine(EngineKind.Postgres, _ => value));
    }

    [Fact]
    public void ExternalEngine_IgnoresOtherKindsVariables()
    {
        var values = new Dictionary<string, string?> { ["JAUNTYQ_TEST_ENGINE_POSTGRES"] = "Host=elsewhere" };

        Assert.Null(EngineContainers.ExternalEngine(EngineKind.MsSql, name => values.TryGetValue(name, out var v) ? v : null));
    }

    [Fact]
    public void ExternalEngineVariable_RejectsAnUnknownKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EngineContainers.ExternalEngineVariable((EngineKind)99));
    }

    [Fact]
    public void RunToken_IsEightHexCharacters()
    {
        Assert.Matches("^[0-9a-f]{8}$", EngineContainers.RunToken);
    }

    [SkippableFact]
    public async Task CreateDatabase_NamesCarryTheRunToken()
    {
        var engine = await EngineContainers.Postgres;
        Skip.IfNot(engine.Available, engine.SkipReason);

        var connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_run_token");

        Assert.StartsWith($"fx_run_token_{EngineContainers.RunToken}_", new NpgsqlConnectionStringBuilder(connectionString).Database);
    }

    [SkippableFact]
    public async Task DropDatabase_MsSql_RemovesItDespiteAPooledConnection()
    {
        var engine = await EngineContainers.MsSql;
        Skip.IfNot(engine.Available, engine.SkipReason);
        var connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_drop_mssql");
        string database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        await using (var used = new SqlConnection(connectionString))
            await used.OpenAsync();

        await EngineContainers.DropDatabaseAsync(engine, database);

        await using var admin = new SqlConnection(engine.AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @n";
        cmd.Parameters.AddWithValue("@n", database);
        Assert.Equal(0, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [SkippableFact]
    public async Task DropDatabase_Postgres_RemovesItDespiteAPooledConnection()
    {
        var engine = await EngineContainers.Postgres;
        Skip.IfNot(engine.Available, engine.SkipReason);
        var connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_drop_pg");
        string database = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        await using (var used = new NpgsqlConnection(connectionString))
            await used.OpenAsync();

        await EngineContainers.DropDatabaseAsync(engine, database);

        await using var admin = new NpgsqlConnection(engine.AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pg_database WHERE datname = @n";
        cmd.Parameters.AddWithValue("n", database);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [SkippableFact]
    public async Task DropDatabase_MySql_RemovesItDespiteAPooledConnection()
    {
        var engine = await EngineContainers.MySql;
        Skip.IfNot(engine.Available, engine.SkipReason);
        var connectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_drop_mysql");
        string database = new MySqlConnectionStringBuilder(connectionString).Database;
        await using (var used = new MySqlConnection(connectionString))
            await used.OpenAsync();

        await EngineContainers.DropDatabaseAsync(engine, database);

        await using var admin = new MySqlConnection(engine.AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @n";
        cmd.Parameters.AddWithValue("@n", database);
        Assert.Equal(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
    }
}
