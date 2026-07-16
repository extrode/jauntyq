using JauntyQ.TestInfra;
using Npgsql;
using Testcontainers.PostgreSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sakila.Postgres.Tests;

/// <summary>
/// Boots a real PostgreSQL instance via Testcontainers, applies the trimmed
/// Sakila/Pagila schema + curated seed data, and exposes a JauntyDb over it.
/// See the test log at the repo root for what "trimmed" means and
/// why. Soft-skips (via <see cref="Available"/>) when Docker is unavailable.
/// </summary>
public sealed class SakilaPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }

    // Throws an informative InvalidOperationException (naming the real
    // SkipReason) instead of a bare NullReferenceException when the container
    // never came up -- see FixtureGate.RequireAvailable for why. Does not
    // change pass/fail outcomes: still fails when unavailable, just legibly.
    public JauntyDb Db => FixtureGate.RequireAvailable(_db, Available, SkipReason, nameof(SakilaPostgresFixture));
    private JauntyDb? _db;
    private NpgsqlConnection? _conn;

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
                await cmd.ExecuteNonQueryAsync();
            }

            _conn = new NpgsqlConnection(_container.GetConnectionString());
            await _conn.OpenAsync();
            _db = new JauntyDb(_conn);
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
        if (_conn != null)
            await _conn.DisposeAsync();
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
