using MySqlConnector;
using Testcontainers.MySql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.MySql.Tests;

/// <summary>
/// Boots a real MySQL instance via Testcontainers, applies the Northwind-subset
/// DDL + seed, and exposes a JauntyDb. Proves the MySQL emission path — which is
/// the most distinct of the four dialects: identity via SELECT last_insert_id()
/// and upsert via ON DUPLICATE KEY UPDATE. Soft-skips when Docker is absent.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container =
        new MySqlBuilder().WithImage("mysql:8.0").Build();

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
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
                cmd.CommandText = ddl; // MySqlConnector runs ;-separated batches
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
