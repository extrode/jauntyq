using Npgsql;
using Testcontainers.PostgreSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests;

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
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => _container.GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public NpgsqlConnection Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            string ddl = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "schema.postgres.sql"));

            await using (var seed = new NpgsqlConnection(_container.GetConnectionString()))
            {
                await seed.OpenAsync();
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = ddl;
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }

            Connection = new NpgsqlConnection(_container.GetConnectionString());
            await Connection.OpenAsync();
            Db = new JauntyDb(Connection);
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"Docker unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection != null)
            await Connection.DisposeAsync();
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
