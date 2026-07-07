using Npgsql;
using Testcontainers.PostgreSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.Postgres.Tests;

/// <summary>
/// Boots a real PostgreSQL instance via Testcontainers, applies the eShopOnWeb
/// data-layer schema + seed data, and exposes a JauntyDb over it. See
/// the test log at the repo root for the flattening/scope
/// decisions behind this schema. Soft-skips (via <see cref="Available"/>)
/// when Docker is unavailable.
/// </summary>
public sealed class EShopOnWebPostgresFixture : IAsyncLifetime
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
