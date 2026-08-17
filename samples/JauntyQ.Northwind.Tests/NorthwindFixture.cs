using System.Text;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using JauntyQ.Generated;
using JauntyQ.TestInfra;
using Xunit;

namespace JauntyQ.Northwind.Tests;

/// <summary>
/// Shared collection so every Northwind test class runs against ONE
/// Testcontainers-managed SQL Server (spinning a container per class would be
/// prohibitive).
/// </summary>
[CollectionDefinition("Northwind")]
public sealed class NorthwindCollection : ICollectionFixture<NorthwindFixture> { }

/// <summary>
/// Boots a real SQL Server via Testcontainers and applies schema.sqlserver.sql
/// (Northwind schema generated from the committed snapshot + canonical seed +
/// PascalCase views). Exposes a JauntyDb for the fixture-injecting test classes,
/// plus static ConnectionString/IsAvailable for the tests that open their own
/// fresh connections (Tier1/2/3, transactional CRUD). Soft-skips when Docker is
/// unavailable so local `dotnet test` stays green while CI exercises everything.
/// </summary>
public sealed class NorthwindFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private SqlConnection? _conn;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;

    // Fresh-connection tests read these statics instead of holding the instance.
    public static string? ConnectionString { get; private set; }
    public static bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        // ONLY container/Docker bring-up is treated as infra. A failure here
        // soft-skips locally; under CI (GITHUB_ACTIONS) FixtureGate rethrows so
        // a broken runner fails loudly instead of going silently green.
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            IsAvailable = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        // Everything below runs OUTSIDE the skip guard: applying the seed script
        // and connecting exercise product/seed code, so a failure here is a real
        // bug and MUST throw and fail the suite — never be reclassified as a
        // "Docker unavailable" skip (which previously masked seed-script bugs).
        string script = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.sqlserver.sql"));

        await using (var seed = new SqlConnection(_container.GetConnectionString()))
        {
            await seed.OpenAsync();
            foreach (var batch in SplitBatches(script))
            {
                await using var cmd = seed.CreateCommand();
                cmd.CommandText = batch;
                // CommandTimeout is per batch, not per script. The Orders batch is 830
                // INSERTs in 303,884 bytes and takes ~4.4s idle, ~17.7s under a full
                // solution run; SqlClient's 30s default left no headroom, and the whole
                // assembly failed 69/69 with "Execution Timeout Expired". 300 matches the
                // other seed-applying fixtures.
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        ConnectionString = _container.GetConnectionString();
        _conn = new SqlConnection(ConnectionString);
        await _conn.OpenAsync();
        Db = new JauntyDb(_conn);
        Available = true;
        IsAvailable = true;
    }

    // schema.sqlserver.sql separates batches with a bare "GO"; CREATE VIEW and
    // SET IDENTITY_INSERT require their own batch, and Microsoft.Data.SqlClient
    // does not understand GO, so we split on GO lines and run each batch. Compare
    // trimmed lines (not a regex) so CR/LF line endings can't leak a trailing \r.
    private static IEnumerable<string> SplitBatches(string script)
    {
        var sb = new StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = sb.ToString();
                if (!string.IsNullOrWhiteSpace(batch))
                    yield return batch;
                sb.Clear();
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }
        var tail = sb.ToString();
        if (!string.IsNullOrWhiteSpace(tail))
            yield return tail;
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        if (_container != null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
