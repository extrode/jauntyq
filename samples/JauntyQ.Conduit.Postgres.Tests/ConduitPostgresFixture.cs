using JauntyQ.TestInfra;
using Npgsql;
using Testcontainers.PostgreSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed PostgreSQL. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("ConduitPostgres")]
public sealed class ConduitPostgresCollection : ICollectionFixture<ConduitPostgresFixture> { }

/// <summary>
/// Boots a real PostgreSQL instance via Testcontainers, applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. Soft-skips (via <see cref="Available"/>) when
/// Docker is unavailable -- every test method guards on it. Postgres always
/// enforces foreign keys, so no per-connection PRAGMA is needed (unlike the
/// SQLite sibling); unlike SQL Server, Postgres has no multiple-cascade-path
/// restriction, so the `follows` FKs keep ON DELETE CASCADE, see
/// schema.postgres.sql.
/// </summary>
public sealed class ConduitPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(ConduitPostgresFixture)).GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public NpgsqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        PostgreSqlContainer container;
        try
        {
            container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
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
            Path.Combine(AppContext.BaseDirectory, "schema.postgres.sql"));

        await using (var seed = new NpgsqlConnection(container.GetConnectionString()))
        {
            await seed.OpenAsync();
            await using var cmd = seed.CreateCommand();
            cmd.CommandText = ddl;
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }

        Connection = new NpgsqlConnection(container.GetConnectionString());
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
