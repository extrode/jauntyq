using JauntyQ.TestInfra;
using MySqlConnector;
using Testcontainers.MariaDb;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.MariaDb.Tests;

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
    private readonly MariaDbContainer _container =
        new MariaDbBuilder().WithImage("mariadb:11").Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _container.GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public MySqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            string ddl = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "schema.mariadb.sql"));

            await using (var seed = new MySqlConnection(_container.GetConnectionString()))
            {
                await seed.OpenAsync();
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = ddl;
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }

            Connection = new MySqlConnection(_container.GetConnectionString());
            await Connection.OpenAsync();
            Db = new JauntyDb(Connection);
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection != null)
            await Connection.DisposeAsync();
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
