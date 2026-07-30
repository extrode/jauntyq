using JauntyQ.TestInfra;
using MySqlConnector;
using Testcontainers.MySql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.MySql.Tests;

/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed MySQL. IClassFixture is per-class, so the seven
/// classes here previously started seven containers; across the sixteen
/// container-backed assemblies that put ~107 databases on one Docker host and
/// starved the whole suite. Northwind has always done it this way.
/// </summary>
[CollectionDefinition("EShopOnWebMySql")]
public sealed class EShopOnWebMySqlCollection : ICollectionFixture<EShopOnWebMySqlFixture> { }

/// <summary>
/// Boots a real MySQL instance via Testcontainers, applies the eShopOnWeb
/// data-layer schema + seed data, and exposes a JauntyDb over it. See
/// the test log at the repo root for the flattening/scope
/// decisions behind this schema. Soft-skips (via <see cref="Available"/>)
/// when Docker is unavailable.
/// </summary>
public sealed class EShopOnWebMySqlFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(EShopOnWebMySqlFixture)).GetConnectionString();
    private MySqlConnection? _conn;

    public async Task InitializeAsync()
    {
        MySqlContainer container;
        try
        {
            container = new MySqlBuilder().Build();
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
            Path.Combine(AppContext.BaseDirectory, "schema.mysql.sql"));

        await using (var seed = new MySqlConnection(container.GetConnectionString()))
        {
            await seed.OpenAsync();
            await using var cmd = seed.CreateCommand();
            cmd.CommandText = ddl;
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }

        _conn = new MySqlConnection(container.GetConnectionString());
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
