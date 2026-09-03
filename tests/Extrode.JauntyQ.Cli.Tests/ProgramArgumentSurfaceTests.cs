using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

[Trait("Category", "AuditRegression")]
[Collection("cli-console")]
public class ProgramArgumentSurfaceTests : IDisposable
{
    private const string ConnectionEnvName = "JQ_ARG_SURFACE_CONNECTION";

    private readonly string _relativeDir = "jq-arg-surface-" + Guid.NewGuid().ToString("N");
    private readonly string _dbPath;
    private readonly string _snapshotPath;
    private readonly string? _savedConnection = Environment.GetEnvironmentVariable(CliEnvironment.ConnectionEnvVar);
    private readonly string? _savedVerbose = Environment.GetEnvironmentVariable(CliEnvironment.VerboseEnvVar);

    public ProgramArgumentSurfaceTests()
    {
        Directory.CreateDirectory(_relativeDir);
        _dbPath = Path.Combine(Path.GetFullPath(_relativeDir), "probe.db");
        _snapshotPath = Path.Combine(_relativeDir, "jaunty.schema.json");

        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, null);
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, null);
        Environment.SetEnvironmentVariable(ConnectionEnvName, null);

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, _savedConnection);
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, _savedVerbose);
        Environment.SetEnvironmentVariable(ConnectionEnvName, null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetFullPath(_relativeDir), recursive: true); } catch { /* best effort */ }
    }

    private string ConnectionString => $"Data Source={_dbPath}";

    private static async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            int code = await Program.Main(args);
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    private string[] PullArgs(params string[] extra)
    {
        var head = new[]
        {
            "schema", "pull", "--provider", "sqlite", "--connection", ConnectionString,
            "--output", _snapshotPath,
        };
        var all = new string[head.Length + extra.Length];
        head.CopyTo(all, 0);
        extra.CopyTo(all, head.Length);
        return all;
    }

    [Theory]
    [InlineData("--nope")]
    [InlineData("extra-positional")]
    [InlineData("--format")]
    [InlineData("--fail-on")]
    [InlineData("--service")]
    [InlineData("--registry")]
    [InlineData("--usage")]
    public async Task UnknownArgument_IsNamedBackAndStopsThePull(string argument)
    {
        var (code, outText, err) = await RunAsync(PullArgs(argument));

        Assert.Equal(1, code);
        Assert.Contains($"Unknown argument: {argument}", err);
        Assert.Contains("JauntyQ CLI", outText);
        Assert.False(File.Exists(_snapshotPath));
    }

    [Theory]
    [InlineData("--provider")]
    [InlineData("--connection")]
    [InlineData("--connection-env")]
    [InlineData("--output")]
    public async Task ValueFlagWithNoValue_FallsToTheUnknownArgumentArm(string flag)
    {
        var (code, _, err) = await RunAsync(PullArgs(flag));

        Assert.Equal(1, code);
        Assert.Contains($"Unknown argument: {flag}", err);
    }

    [Fact]
    public async Task ConnectionEnv_NamesTheVariableTheCredentialIsReadFrom()
    {
        Environment.SetEnvironmentVariable(ConnectionEnvName, ConnectionString);

        var (code, outText, _) = await RunAsync(
            "schema", "pull", "--provider", "sqlite",
            "--connection-env", ConnectionEnvName, "--output", _snapshotPath);

        Assert.Equal(0, code);
        Assert.Contains("Extracted 1 table(s)", outText);
        Assert.True(File.Exists(_snapshotPath));
    }

    [Fact]
    public async Task DefaultConnectionEnv_IsUsedWhenNeitherFlagIsGiven()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, ConnectionString);

        var (code, outText, _) = await RunAsync(
            "schema", "pull", "--provider", "sqlite", "--output", _snapshotPath);

        Assert.Equal(0, code);
        Assert.Contains("Extracted 1 table(s)", outText);
    }

    [Fact]
    public async Task ConnectionEnv_PointingAtAnUnsetVariable_ReportsNoConnectionString()
    {
        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", "sqlite",
            "--connection-env", ConnectionEnvName, "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains("no connection string", err);
        Assert.Contains(CliEnvironment.ConnectionEnvVar, err);
    }

    [Fact]
    public async Task NoConnectionAnywhere_ReportsNoConnectionString()
    {
        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", "sqlite", "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains("no connection string", err);
    }

    [Theory]
    [InlineData("oracle")]
    [InlineData("db2")]
    [InlineData("SQLITE3")]
    public async Task UnknownProvider_IsNamedBackWithTheSupportedList(string provider)
    {
        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", provider, "--connection", ConnectionString,
            "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains($"unknown provider '{provider}'", err);
        Assert.Contains(DialectAliases.SupportedProviders, err);
    }

    [Fact]
    public async Task OutputInAMissingDirectory_CreatesItRatherThanFailing()
    {
        string nested = Path.Combine(_relativeDir, "nested", "deeper", "jaunty.schema.json");
        Assert.False(Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(nested))!));

        var (code, _, _) = await RunAsync(
            "schema", "pull", "--provider", "sqlite", "--connection", ConnectionString,
            "--output", nested);

        Assert.Equal(0, code);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public async Task ExtractionFailure_WithoutVerbose_WithholdsTheProviderMessage()
    {
        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", "sqlite",
            "--connection", "Data Source=:memory:;NotAKeyword=1", "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains("schema pull failed", err);
        Assert.Contains($"{CliEnvironment.VerboseEnvVar}={CliEnvironment.VerboseOptIn}", err);
        Assert.DoesNotContain("NotAKeyword", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractionFailure_WithVerbose_IncludesTheProviderMessage()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, CliEnvironment.VerboseOptIn);

        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", "sqlite",
            "--connection", "Data Source=:memory:;NotAKeyword=1", "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains("schema pull failed", err);
        Assert.Contains("NotAKeyword", err, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("postgres", "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=d;Timeout=1")]
    [InlineData("postgresql", "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=d;Timeout=1")]
    [InlineData("sqlserver", "Server=127.0.0.1,1;Database=d;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true")]
    [InlineData("mssql", "Server=127.0.0.1,1;Database=d;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true")]
    [InlineData("mysql", "Server=127.0.0.1;Port=1;Database=d;Uid=u;Pwd=p;ConnectionTimeout=1")]
    [InlineData("mariadb", "Server=127.0.0.1;Port=1;Database=d;Uid=u;Pwd=p;ConnectionTimeout=1")]
    public async Task EveryAliasSelectsItsOwnExtractor_NotTheSqliteFallback(
        string provider, string connection)
    {
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, CliEnvironment.VerboseOptIn);

        var (code, _, err) = await RunAsync(
            "schema", "pull", "--provider", provider, "--connection", connection,
            "--output", _snapshotPath);

        Assert.Equal(1, code);
        Assert.Contains("schema pull failed", err);
        Assert.DoesNotContain("is not supported", err);
        Assert.False(File.Exists(_snapshotPath));
    }
}
