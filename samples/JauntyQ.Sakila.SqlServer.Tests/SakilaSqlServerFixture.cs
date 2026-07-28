using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Sakila.SqlServer.Tests;

/// <summary>
/// Boots a real SQL Server instance via Testcontainers, applies the trimmed
/// Sakila/Pagila schema + curated seed data, and exposes a JauntyDb over it.
/// See the test log at the repo root for what "trimmed" means and
/// why. Soft-skips (via <see cref="Available"/>) when Docker is unavailable.
/// </summary>
public sealed class SakilaSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }

    // Throws an informative InvalidOperationException (naming the real
    // SkipReason) instead of a bare NullReferenceException when the container
    // never came up -- see FixtureGate.RequireAvailable for why. Does not
    // change pass/fail outcomes: still fails when unavailable, just legibly.
    public JauntyDb Db => FixtureGate.RequireAvailable(_db, Available, SkipReason, nameof(SakilaSqlServerFixture));
    private JauntyDb? _db;
    private SqlConnection? _conn;

    public async Task InitializeAsync()
    {
        try
        {
            MsSqlContainer container = new MsSqlBuilder().Build();
            _container = container;
            await container.StartAsync();

            string ddl = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "schema.mssql.sql"));

            await using (var seed = new SqlConnection(container.GetConnectionString()))
            {
                await seed.OpenAsync();
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = ddl;
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }

            _conn = new SqlConnection(container.GetConnectionString());
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
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
