using JauntyQ.TestInfra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace JauntyQ.Conduit.MySql.Tests.Http;

/// <summary>
/// One MySQL server per test ASSEMBLY, joined by every HTTP class fixture
/// below rather than started once per class.
///
/// The 2026-07-31 shared-engine-container work converted every direct-repository
/// fixture to a collection fixture for exactly this reason, and did not reach
/// this directory. Measured 2026-08-17 on the MariaDB sibling: one such assembly
/// alone put SEVEN containers on the daemon at once -- the direct-repository
/// fixture's one, plus one per HTTP test class. Four such assemblies under
/// `-m:4` is ~28, and at that point the failure is not the database. It is
/// Docker's own named pipe: NamedPipeClientStream.ConnectInternal timing out
/// inside Docker.DotNet, which surfaces as bare "The operation has timed out."
/// failures in whichever class lost the race.
///
/// xUnit v2 cannot inject a collection fixture into a class fixture, so the
/// sharing is a static rather than an ICollectionFixture. The container is
/// deliberately never disposed here: Testcontainers' resource reaper removes it
/// when the test process exits, which is the mechanism it exists for, and it is
/// the only teardown point that outlives all six class fixtures.
/// </summary>
internal static class ConduitHttpServer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static MySqlContainer? _started;
    private static Exception? _failure;

    internal static async Task<MySqlContainer> StartOrJoinAsync()
    {
        await Gate.WaitAsync();
        try
        {
            // Cached so a machine without Docker pays one bring-up timeout for
            // the assembly instead of one per class.
            if (_failure is not null)
                throw _failure;
            if (_started is not null)
                return _started;

            var container = new MySqlBuilder().Build();
            try
            {
                await container.StartAsync();
            }
            catch (Exception ex)
            {
                _failure = ex;
                throw;
            }

            _started = container;
            return container;
        }
        finally
        {
            Gate.Release();
        }
    }
}

/// <summary>
/// Boots the real ASP.NET Core host (Program.cs) against a real MySQL
/// database -- separate from ConduitMySqlFixture
/// so the direct-repository tests and the HTTP black-box tests each get their
/// own isolated database. Overrides the "ConnectionString" configuration key
/// via ConfigureWebHost, matching what Program.cs already reads, so no
/// production code needs to change to support this test seam.
///
/// WebApplicationFactory's constructor is synchronous and can't join a
/// container cleanly, so this class also implements IAsyncLifetime (xUnit's
/// IClassFixture honors it for setup/teardown): the shared server is joined and
/// a fresh catalogue carrying the DDL-only schema is created in InitializeAsync.
/// Soft-skips (via <see cref="Available"/>) when Docker is unavailable -- every
/// HTTP test method guards on it.
/// </summary>
public sealed class ConduitWebAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;
    private string _database = string.Empty;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;

    public ConduitWebAppFixture()
    {
        // This project has no .sln, and Program.cs (the TEntryPoint) lives in the test
        // assembly itself rather than a referenced web project, so WebApplicationFactory's
        // default content-root discovery (WebApplicationFactoryContentRootAttribute lookup,
        // then solution-relative-to-*.sln fallback) can't resolve anything and throws.
        // WebApplicationFactory checks a "TEST_CONTENTROOT_<ASSEMBLY_NAME>" IWebHostBuilder
        // setting first, which -- via WebHostBuilderBase's config (AddEnvironmentVariables
        // with an "ASPNETCORE_" prefix that gets stripped) -- is satisfied by this env var.
        // The value just needs to be a resolvable directory; nothing here serves static
        // files/views, so the bin output dir is fine.
        string assemblyKey = typeof(Program).Assembly.GetName().Name!.ToUpperInvariant().Replace(".", "_");
        Environment.SetEnvironmentVariable($"ASPNETCORE_TEST_CONTENTROOT_{assemblyKey}", AppContext.BaseDirectory);
    }

    public async Task InitializeAsync()
    {
        MySqlContainer container;
        try
        {
            container = await ConduitHttpServer.StartOrJoinAsync();
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = FixtureGate.SkipReasonOrThrow(ex);
            return;
        }

        // A catalogue per test class, not a server per test class. The six HTTP
        // classes run in parallel and each registers its own users through the
        // API, so they must not see each other's rows -- but that is an argument
        // for separate databases, which cost nothing, rather than separate
        // containers, which cost a Docker bring-up each.
        // The container's own user is not granted CREATE DATABASE; root is, and
        // Testcontainers gives it the same password.
        _adminConnectionString = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            UserID = "root",
        }.ConnectionString;
        _database = "conduit_http_" + Guid.NewGuid().ToString("N")[..12];

        await using (var server = new MySqlConnection(_adminConnectionString))
        {
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE `{_database}`";
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new MySqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _database,
        }.ConnectionString;

        // schema.mysql.ddl.sql is a DDL-only copy of schema.mysql.sql (no seed rows):
        // HTTP tests create every user/article through the API itself, and would
        // otherwise collide with schema.mysql.sql's seeded usernames/emails
        // (jane/bob/carol/dave etc.) meant for ConduitMySqlFixture's direct-repository tests.
        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mysql.ddl.sql"));

        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync();

        Available = true;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = ConnectionString,
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // The catalogue is this fixture's to drop; the server is not this
        // fixture's to stop, because the other five class fixtures may still be
        // using it. See ConduitHttpServer for what removes the container.
        if (_database.Length > 0)
        {
            try
            {
                await using var server = new MySqlConnection(_adminConnectionString);
                await server.OpenAsync();
                await using var drop = server.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* the server is going away with the process regardless */ }
        }

        await base.DisposeAsync();
    }
}
