using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.Conduit.SqlServer.Tests;

/// <summary>
/// Boots a real SQL Server instance via Testcontainers, applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. Soft-skips (via <see cref="Available"/>) when
/// Docker is unavailable -- every test method guards on it. SQL Server always
/// enforces foreign keys, so no per-connection PRAGMA is needed (unlike the
/// SQLite sibling); the `follows` FKs omit ON DELETE CASCADE to avoid SQL
/// Server's multiple-cascade-path error (1785), see schema.mssql.sql.
/// </summary>
public sealed class ConduitSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString => FixtureGate.RequireAvailable(_container, Available, SkipReason, nameof(ConduitSqlServerFixture)).GetConnectionString();
    public JauntyDb Db { get; private set; } = null!;
    public SqlConnection Connection { get; private set; } = null!;

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

            Connection = new SqlConnection(container.GetConnectionString());
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
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
