using JauntyQ.TestInfra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JauntyQ.Conduit.Postgres.Tests.Http;

/// <summary>
/// One PostgreSQL server per test ASSEMBLY, joined by every HTTP class fixture
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
    private static PostgreSqlContainer? _started;
    private static Exception? _failure;

    internal static async Task<PostgreSqlContainer> StartOrJoinAsync()
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

            var container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
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
/// Boots the real ASP.NET Core host (Program.cs) against a real PostgreSQL
/// database -- separate from ConduitPostgresFixture
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
        PostgreSqlContainer container;
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
        // Testcontainers' postgres user is the superuser, so no second identity
        // is needed here as it is on the MySQL siblings.
        _adminConnectionString = container.GetConnectionString();
        _database = "conduit_http_" + Guid.NewGuid().ToString("N")[..12];

        await using (var server = new NpgsqlConnection(_adminConnectionString))
        {
            await server.OpenAsync();
            // CREATE DATABASE cannot run inside a transaction block, which is
            // why this is a bare command rather than part of the DDL batch below.
            await using var create = server.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{_database}\"";
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _database,
        }.ConnectionString;

        // schema.postgres.ddl.sql is a DDL-only copy of schema.postgres.sql (no seed rows):
        // HTTP tests create every user/article through the API itself, and would
        // otherwise collide with schema.postgres.sql's seeded usernames/emails
        // (jane/bob/carol/dave etc.) meant for ConduitPostgresFixture's direct-repository tests.
        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.postgres.ddl.sql"));

        await using var conn = new NpgsqlConnection(ConnectionString);
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
                // WITH (FORCE) because the host's Npgsql pool still holds idle
                // connections to this database; a plain DROP would be refused.
                // It terminates backends on THIS database only, which is why
                // it is used in preference to NpgsqlConnection.ClearAllPools() --
                // that would also drain the five sibling fixtures still running.
                await using var server = new NpgsqlConnection(_adminConnectionString);
                await server.OpenAsync();
                await using var drop = server.CreateCommand();
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
                await drop.ExecuteNonQueryAsync();
            }
            catch { /* the server is going away with the process regardless */ }
        }

        await base.DisposeAsync();
    }
}
