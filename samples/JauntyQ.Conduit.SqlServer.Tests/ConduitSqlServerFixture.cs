using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.SqlServer.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed SQL Server. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("ConduitSqlServer")]
public sealed class ConduitSqlServerCollection : ICollectionFixture<ConduitSqlServerFixture> { }

/// <summary>
/// Boots a real SQL Server instance via Testcontainers, applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. Soft-skips (via <see cref="Available"/>) when
/// Docker is unavailable -- every test method guards on it. SQL Server always
/// enforces foreign keys, so no per-connection PRAGMA is needed (unlike the
/// SQLite sibling); the `follows` FKs omit ON DELETE CASCADE to avoid SQL
/// Server's multiple-cascade-path error (1785), see schema.mssql.sql.
/// </summary>
public sealed class ConduitSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(ConduitSqlServerFixture)).GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public SqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        MsSqlContainer container;
        try
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            _container = container;
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mssql.sql"));

        await using (var seed = new SqlConnection(container.GetConnectionString()))
        {
            await seed.OpenAsync();
            await using var cmd = seed.CreateCommand();
            cmd.CommandText = ddl;
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }

        Connection = new SqlConnection(container.GetConnectionString());
        await Connection.OpenAsync();
        Db = new JauntyDb(Connection);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (Connection != null)
            await Connection.DisposeAsync();
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
