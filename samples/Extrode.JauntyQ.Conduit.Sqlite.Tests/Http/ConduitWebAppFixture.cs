using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Conduit.Sqlite.Tests.Http;

/// <summary>
/// Boots the real ASP.NET Core host (Program.cs) against its own
/// uniquely-named shared in-memory SQLite database -- separate from
/// ConduitSqliteFixture so the existing direct-repository tests don't pay
/// for host startup. Overrides the "ConnectionString" configuration key via
/// ConfigureWebHost, matching what Program.cs already reads, so no
/// production code needs to change to support this test seam.
/// </summary>
public sealed class ConduitWebAppFixture : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }

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

        string dbName = "jauntyq_conduit_http_" + Guid.NewGuid().ToString("N");
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using (var pragma = _keepAlive.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
        }

        // schema.sqlite.ddl.sql is a DDL-only copy of schema.sqlite.sql (no seed rows):
        // HTTP tests create every user/article through the API itself, and would
        // otherwise collide with schema.sqlite.sql's seeded usernames/emails
        // (jane/bob/carol/dave etc.) meant for ConduitSqliteFixture's direct-repository tests.
        string ddlPath = Path.Combine(AppContext.BaseDirectory, "schema.sqlite.ddl.sql");
        string ddl = File.ReadAllText(ddlPath);
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
