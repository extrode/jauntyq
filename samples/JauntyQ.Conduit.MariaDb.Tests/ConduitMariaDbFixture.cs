using JauntyQ.TestInfra;
using MySqlConnector;
using Testcontainers.MariaDb;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.MariaDb.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed MariaDB. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("ConduitMariaDb")]
public sealed class ConduitMariaDbCollection : ICollectionFixture<ConduitMariaDbFixture> { }

/// <summary>
/// Boots a real MariaDB instance via Testcontainers, applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. Soft-skips (via <see cref="Available"/>) when
/// Docker is unavailable -- every test method guards on it. MariaDB always
/// enforces foreign keys (InnoDB), so no per-connection PRAGMA is needed
/// (unlike the SQLite sibling); unlike SQL Server, MariaDB has no
/// multiple-cascade-path restriction, so the `follows` FKs keep ON DELETE
/// CASCADE, see schema.mariadb.sql. The seed batch runs with CommandTimeout=300
/// because MySqlConnector's default 30s is too short for a large combined
/// schema+seed batch (known finding from an earlier part of this torture test).
/// </summary>
public sealed class ConduitMariaDbFixture : IAsyncLifetime
{
    private MariaDbContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(ConduitMariaDbFixture)).GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public MySqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        MariaDbContainer container;
        try
        {
            container = new MariaDbBuilder("mariadb:11").Build();
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
            Path.Combine(AppContext.BaseDirectory, "schema.mariadb.sql"));

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
