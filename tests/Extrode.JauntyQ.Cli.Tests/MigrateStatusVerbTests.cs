using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

[Collection("cli-console")]
public class MigrateStatusVerbTests : IDisposable
{
    private readonly string _dir = Path.GetFullPath("jq-migrate-status-" + Guid.NewGuid().ToString("N"));
    private readonly string _migrations;
    private readonly string _dbPath;
    private readonly string? _savedConnection = Environment.GetEnvironmentVariable(CliEnvironment.ConnectionEnvVar);
    private readonly string? _savedVerbose = Environment.GetEnvironmentVariable(CliEnvironment.VerboseEnvVar);

    public MigrateStatusVerbTests()
    {
        _migrations = Path.Combine(_dir, "migrations");
        Directory.CreateDirectory(_migrations);
        _dbPath = Path.Combine(_dir, "app.db");
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, null);
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, _savedConnection);
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, _savedVerbose);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Connection => $"Data Source={_dbPath}";

    private void Execute(string sql)
    {
        using var conn = new SqliteConnection(Connection);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Record(params string[] versions)
    {
        Execute("CREATE TABLE IF NOT EXISTS schema_migrations (version TEXT NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)");
        foreach (var v in versions)
            Execute($"INSERT INTO schema_migrations (version) VALUES ('{v}')");
    }

    private void Files(params string[] names)
    {
        foreach (var name in names)
            File.WriteAllText(Path.Combine(_migrations, name), "select 1;");
    }

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] extra)
    {
        var args = new List<string> { "migrate", "status", "--provider", "sqlite", "--connection", Connection, "--migrations", _migrations };
        args.AddRange(extra);
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            int code = await Program.Main(args.ToArray());
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    [Fact]
    public async Task NoTable_ListsEveryFileAsPending_AndExitsZero()
    {
        Execute("CREATE TABLE products (id INTEGER PRIMARY KEY)");
        Files("0002_b.sql", "0001_a.sql", "notes.txt");

        var (code, output, err) = await RunAsync();

        Assert.Equal(0, code);
        Assert.Empty(err);
        Assert.Contains("Migration files in " + _migrations + ": 2", output);
        Assert.Contains("No schema_migrations table: nothing is recorded as applied.", output);
        Assert.Contains("Pending, in apply order (2):" + Environment.NewLine + "  0001_a.sql" + Environment.NewLine + "  0002_b.sql", output);
    }

    [Fact]
    public async Task EverythingApplied_IsUpToDate()
    {
        Record("0001_a.sql");

        var (code, output, _) = await RunAsync();

        Assert.Equal(0, code);
        Assert.Contains("Recorded in schema_migrations: 1", output);
        Assert.Contains("Up to date: nothing pending.", output);
    }

    [Fact]
    public async Task Pending_ExitsZero_UnlessFailOnPending()
    {
        Record("0001_a.sql");
        Files("0002_b.sql");

        Assert.Equal(0, (await RunAsync()).Code);
        Assert.Equal(2, (await RunAsync("--fail-on", "pending")).Code);
    }

    [Fact]
    public async Task AnAppliedFileStillPresent_ExitsTwo()
    {
        Record("0001_a.sql");
        Files("0001_a.sql");

        var (code, output, _) = await RunAsync();

        Assert.Equal(2, code);
        Assert.Contains("Applied but still in " + _migrations + " (1).", output);
    }

    [Fact]
    public async Task APendingFileBeforeTheLatestApplied_ExitsTwo()
    {
        Record("V1__a.sql", "V10__c.sql");
        Files("V2__b.sql");

        var (code, output, _) = await RunAsync();

        Assert.Equal(2, code);
        Assert.Contains("Out of order (1). These sort before V10__c.sql", output);
    }

    [Fact]
    public async Task AMissingMigrationsDirectory_ExitsOne()
    {
        var (code, output, err) = await RunAsync("--migrations", Path.Combine(_dir, "absent"));

        Assert.Equal(1, code);
        Assert.Equal($"Error: migrations directory '{Path.Combine(_dir, "absent")}' does not exist.", err.TrimEnd());
        Assert.Empty(output);
    }

    [Fact]
    public async Task AnUnknownFailOnValue_IsAnUnknownArgument()
    {
        var (code, output, err) = await RunAsync("--fail-on", "drift");

        Assert.Equal(1, code);
        Assert.StartsWith("Unknown argument: --fail-on", err);
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task AConnectionFailure_IsReportedWithoutItsMessage()
    {
        var (code, output, err) = await RunAsync("--connection", $"Data Source={Path.Combine(_dir, "none.db")};Mode=ReadOnly");

        Assert.Equal(1, code);
        Assert.Empty(output);
        Assert.Equal("Error: migrate status failed (SqliteException). Set JAUNTYQ_VERBOSE=1 for detail.", err.TrimEnd());
    }

    [Fact]
    public async Task NoProvider_IsTheSharedUsageError()
    {
        var (code, _, err) = await RunAsync("--provider", "");

        Assert.Equal(1, code);
        Assert.StartsWith("Error: --provider is required", err);
    }
}
