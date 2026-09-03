using Extrode.JauntyQ.TestInfra;
using MySqlConnector;
using Testcontainers.MySql;
using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.MySql.Tests;


/// <summary>
/// Shared collection so every test class in this assembly runs against ONE
/// Testcontainers-managed MySQL. IClassFixture is per-class, so this
/// assembly used to start one container per test class; across the nine
/// container-backed assemblies retrofitted here that put ~100 databases on
/// one Docker host and starved the suite into timeouts. Northwind has always
/// done it this way.
/// </summary>
[CollectionDefinition("MySql")]
public sealed class MySqlCollection : ICollectionFixture<MySqlFixture> { }

/// <summary>
/// Boots a real MySQL instance via Testcontainers, applies the Northwind-subset
/// DDL + seed, and exposes a JauntyDb. Proves the MySQL emission path — which is
/// the most distinct of the four dialects: identity via SELECT last_insert_id()
/// and upsert via ON DUPLICATE KEY UPDATE. Soft-skips when Docker is absent.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime
{
    private MySqlContainer? _container;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public JauntyDb Db { get; private set; } = null!;
    private MySqlConnection? _conn;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MySqlBuilder("mysql:8.0")
                // MySqlBulkCopy issues LOAD DATA LOCAL INFILE, which the server
                // rejects unless local_infile is enabled. The client half of the
                // handshake (AllowLoadLocalInfile=true) is added below when building
                // the runtime connection string.
                .WithCommand("--local-infile=1")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mysql.sql"));

        // The CREATE PROCEDURE body contains internal ';' that would confuse
        // the ;-batch splitter, so it lives after a marker and runs as one
        // statement.
        const string marker = "-- @@PROC@@";
        int mi = ddl.IndexOf(marker, StringComparison.Ordinal);
        string tablesDdl = mi >= 0 ? ddl.Substring(0, mi) : ddl;
        string procDdl = mi >= 0 ? ddl.Substring(mi + marker.Length) : "";

        await using (var seed = new MySqlConnection(_container.GetConnectionString()))
        {
            await seed.OpenAsync();
            await using (var cmd = seed.CreateCommand())
            {
                cmd.CommandText = tablesDdl; // MySqlConnector runs ;-separated batches
                cmd.CommandTimeout = 300;
                await cmd.ExecuteNonQueryAsync();
            }
            if (!string.IsNullOrWhiteSpace(procDdl))
            {
                await using var procCmd = seed.CreateCommand();
                procCmd.CommandText = procDdl.Trim().TrimEnd(';');
                procCmd.CommandTimeout = 300;
                await procCmd.ExecuteNonQueryAsync();
            }
        }

        // MySqlBulkCopy (the BulkInsert fast path) requires the client to
        // opt into LOAD DATA LOCAL INFILE. This is the consumer's
        // responsibility — JauntyQ's generated code cannot set it — so it
        // must be on the connection string the JauntyDb wraps.
        var csb = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            AllowLoadLocalInfile = true
        };
        _conn = new MySqlConnection(csb.ConnectionString);
        await _conn.OpenAsync();
        Db = new JauntyDb(_conn);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_conn != null)
            await _conn.DisposeAsync();
        if (_container != null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
    }
}
