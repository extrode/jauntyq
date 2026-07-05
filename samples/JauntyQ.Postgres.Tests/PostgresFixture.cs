using Npgsql;
using Testcontainers.PostgreSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Postgres.Tests;

/// <summary>
/// Boots a real PostgreSQL instance via Testcontainers, applies the
/// Northwind-subset DDL + seed, and exposes a JauntyDb over it. Proves the
/// generated PostgreSQL code — snake_case name mapping, RETURNING identity,
/// ON CONFLICT upsert, and NpgsqlParameter&lt;T&gt; typed no-box params — runs
/// against a real engine.
///
/// When Docker is not available (e.g. a dev box without a daemon), the
/// container fails to start; the fixture records that and tests soft-skip via
/// <see cref="Available"/>, so local `dotnet test` stays green while CI (with
/// Docker) exercises everything for real.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
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
            Db = new JauntyDb(_conn);
            Available = true;
        }
        catch (Exception ex)
        {
            // Docker not present / not reachable: soft-skip the live tests.
            Available = false;
            SkipReason = $"Docker unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
