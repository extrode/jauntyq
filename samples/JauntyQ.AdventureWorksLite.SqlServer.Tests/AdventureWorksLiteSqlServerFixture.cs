using JauntyQ.TestInfra;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using Xunit;

namespace JauntyQ.AdventureWorksLite.SqlServer.Tests;

/// <summary>
/// Boots a real SQL Server instance via Testcontainers and applies
/// schema.mssql.sql — a trimmed, multi-schema (Person/HumanResources/
/// Production/Purchasing/Sales) port of Microsoft's AdventureWorks sample.
/// Soft-skips (via <see cref="Available"/>) when Docker is unavailable.
/// </summary>
public sealed class AdventureWorksLiteSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;
    private SqlConnection? _conn;

    public async Task InitializeAsync()
    {
        MsSqlContainer container;
        try
        {
            container = new MsSqlBuilder().Build();
            _container = container;
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        ConnectionString = container.GetConnectionString();

        string script = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mssql.sql"));

        await using (var seed = new SqlConnection(ConnectionString))
        {
            await seed.OpenAsync();
            foreach (var batch in SplitBatches(script))
            {
                if (string.IsNullOrWhiteSpace(batch))
                    continue;
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = batch;
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        _conn = new SqlConnection(ConnectionString);
        await _conn.OpenAsync();
        Db = new JauntyDb(_conn);
        Available = true;
    }

    // schema.mssql.sql separates batches with a bare "GO"; CREATE SCHEMA and
    // CREATE VIEW must each be the sole statement in their batch, and a raw
    // SqlCommand does not understand GO, so we split on GO lines and run each
    // batch. Same pattern as samples/JauntyQ.Northwind.Tests/NorthwindFixture.cs.
    private static IEnumerable<string> SplitBatches(string script)
    {
        var current = new System.Text.StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.AppendLine(line);
            }
        }
        yield return current.ToString();
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
