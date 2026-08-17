using JauntyQ.TestInfra;
using MySqlConnector;
using Testcontainers.MySql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.MySql.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed MySQL. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("ConduitMySql")]
public sealed class ConduitMySqlCollection : ICollectionFixture<ConduitMySqlFixture> { }

/// <summary>
/// Boots a real MySQL instance via Testcontainers, applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. Soft-skips (via <see cref="Available"/>) when
/// Docker is unavailable -- every test method guards on it. MySQL always
/// enforces foreign keys (InnoDB), so no per-connection PRAGMA is needed
/// (unlike the SQLite sibling); unlike SQL Server, MySQL has no
/// multiple-cascade-path restriction, so the `follows` FKs keep ON DELETE
/// CASCADE, see schema.mysql.sql. The seed batch runs with CommandTimeout=300
/// because MySqlConnector's default 30s is too short for a large combined
/// schema+seed batch (known finding from an earlier part of this torture test).
/// </summary>
public sealed class ConduitMySqlFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(ConduitMySqlFixture)).GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public MySqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        MySqlContainer container;
        try
        {
            container = new MySqlBuilder("mysql:8.0").Build();
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

        Connection = new MySqlConnection(container.GetConnectionString());
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
