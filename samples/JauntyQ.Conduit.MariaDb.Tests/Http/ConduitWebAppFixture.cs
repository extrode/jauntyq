using JauntyQ.TestInfra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Testcontainers.MariaDb;
using Xunit;

namespace JauntyQ.Conduit.MariaDb.Tests.Http;

/// <summary>
/// Boots the real ASP.NET Core host (Program.cs) against a real MariaDB
/// instance started via Testcontainers -- separate from ConduitMariaDbFixture
/// so the direct-repository tests and the HTTP black-box tests each get their
/// own isolated database. Overrides the "ConnectionString" configuration key
/// via ConfigureWebHost, matching what Program.cs already reads, so no
/// production code needs to change to support this test seam.
///
/// WebApplicationFactory's constructor is synchronous and can't start a
/// container cleanly, so this class also implements IAsyncLifetime (xUnit's
/// IClassFixture honors it for setup/teardown): the container starts and the
/// DDL-only schema is applied in InitializeAsync. Soft-skips (via
/// <see cref="Available"/>) when Docker is unavailable -- every HTTP test
/// method guards on it.
/// </summary>
public sealed class ConduitWebAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private MariaDbContainer? _container;

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
        MariaDbContainer container;
        try
        {
            container = new MariaDbBuilder().WithImage("mariadb:11").Build();
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

        // schema.mariadb.ddl.sql is a DDL-only copy of schema.mariadb.sql (no seed rows):
        // HTTP tests create every user/article through the API itself, and would
        // otherwise collide with schema.mariadb.sql's seeded usernames/emails
        // (jane/bob/carol/dave etc.) meant for ConduitMariaDbFixture's direct-repository tests.
        string ddl = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "schema.mariadb.ddl.sql"));

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
        if (_container is not null)
            try { await _container.DisposeAsync(); } catch { /* nothing started */ }
        await base.DisposeAsync();
    }
}
