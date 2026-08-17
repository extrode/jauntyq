using JauntyQ.TestInfra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace JauntyQ.Conduit.SqlServer.Tests.Http;

/// <summary>
/// One SQL Server instance per test ASSEMBLY, joined by every HTTP class fixture
/// below rather than started once per class. Worth the most here of anywhere:
/// SQL Server is the heaviest image in the matrix at ~2 GB resident.
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
    private static MsSqlContainer? _started;
    private static Exception? _failure;

    internal static async Task<MsSqlContainer> StartOrJoinAsync()
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

            var container = new MsSqlBuilder().Build();
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
/// Boots the real ASP.NET Core host (Program.cs) against a real SQL Server
/// database -- separate from ConduitSqlServerFixture
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
        MsSqlContainer container;
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

        // A catalogue per test class, not an instance per test class. The six
        // HTTP classes run in parallel and each registers its own users through
        // the API, so they must not see each other's rows -- but that is an
        // argument for separate databases, which cost nothing, rather than
        // separate containers, which cost a ~2 GB SQL Server bring-up each.
        // Testcontainers connects as sa, which is sysadmin.
        _adminConnectionString = container.GetConnectionString();
        _database = "conduit_http_" + Guid.NewGuid().ToString("N")[..12];

        await using (var server = new SqlConnection(_adminConnectionString))
        {
            await server.OpenAsync();
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{_database}]";
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new SqlConnectionStringBuilder(_adminConnectionString)
        {
            InitialCatalog = _database,
        }.ConnectionString;

        // schema.mssql.ddl.sql is a DDL-only copy of schema.mssql.sql (no seed rows):
        // HTTP tests create every user/article through the API itself, and would
        // otherwise collide with schema.mssql.sql's seeded usernames/emails
        // (jane/bob/carol/dave etc.) meant for ConduitSqlServerFixture's direct-repository tests.
        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mssql.ddl.sql"));

        await using var conn = new SqlConnection(ConnectionString);
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
        // The catalogue is this fixture's to drop; the instance is not this
        // fixture's to stop, because the other five class fixtures may still be
        // using it. See ConduitHttpServer for what removes the container.
        if (_database.Length > 0)
        {
            try
            {
                await using var server = new SqlConnection(_adminConnectionString);
                await server.OpenAsync();
                await using var drop = server.CreateCommand();
                // SINGLE_USER WITH ROLLBACK IMMEDIATE because the host's
                // SqlClient pool still holds idle connections to this database
                // and a plain DROP would be refused. It touches THIS database
                // only, so the five sibling fixtures still running are unaffected.
                drop.CommandText = $"""
                    IF DB_ID('{_database}') IS NOT NULL
                    BEGIN
                        ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        DROP DATABASE [{_database}];
                    END
                    """;
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* the instance is going away with the process regardless */ }
        }

        await base.DisposeAsync();
    }
}
