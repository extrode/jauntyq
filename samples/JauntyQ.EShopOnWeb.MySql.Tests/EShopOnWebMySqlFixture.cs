using MySqlConnector;
using Testcontainers.MySql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.EShopOnWeb.MySql.Tests;

/// <summary>
/// Boots a real MySQL instance via Testcontainers, applies the eShopOnWeb
/// data-layer schema + seed data, and exposes a JauntyDb over it. See
/// the test log at the repo root for the flattening/scope
/// decisions behind this schema. Soft-skips (via <see cref="Available"/>)
/// when Docker is unavailable.
/// </summary>
public sealed class EShopOnWebMySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container =
        new MySqlBuilder().Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();
    private MySqlConnection? _conn;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            string ddl = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "schema.mysql.sql"));

            await using (var seed = new MySqlConnection(_container.GetConnectionString()))
            {
                await seed.OpenAsync();
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = ddl;
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }

            _conn = new MySqlConnection(_container.GetConnectionString());
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
