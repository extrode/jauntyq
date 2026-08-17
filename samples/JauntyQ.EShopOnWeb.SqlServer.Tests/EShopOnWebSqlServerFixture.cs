using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.SqlServer.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed SQL Server. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("EShopOnWebSqlServer")]
public sealed class EShopOnWebSqlServerCollection : ICollectionFixture<EShopOnWebSqlServerFixture> { }

/// <summary>
/// Boots a real SQL Server instance via Testcontainers, applies the eShopOnWeb
/// data-layer schema + seed data, and exposes a JauntyDb over it. See
/// the test log at the repo root for the flattening/scope
/// decisions behind this schema. Soft-skips (via <see cref="Available"/>)
/// when Docker is unavailable.
/// </summary>
public sealed class EShopOnWebSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(EShopOnWebSqlServerFixture)).GetConnectionString();
    private SqlConnection? _conn;

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

        _conn = new SqlConnection(container.GetConnectionString());
        await _conn.OpenAsync();
        Db = new JauntyDb(_conn);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
