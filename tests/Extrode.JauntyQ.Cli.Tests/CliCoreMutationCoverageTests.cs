using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

[Collection("cli-console")]
public class CliCoreMutationCoverageTests : IDisposable
{
    private const string UnsetConnectionEnv = "JQ_CLI_CORE_MUTATION_UNSET";

    private readonly string _relativeDir = "jq-cli-core-mutation-" + Guid.NewGuid().ToString("N");
    private readonly string? _savedConnection = Environment.GetEnvironmentVariable(CliEnvironment.ConnectionEnvVar);

    public CliCoreMutationCoverageTests()
    {
        Directory.CreateDirectory(_relativeDir);
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, null);
        Environment.SetEnvironmentVariable(UnsetConnectionEnv, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, _savedConnection);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetFullPath(_relativeDir), recursive: true); } catch { }
    }

    private sealed class NamedVerb : IVerb
    {
        public NamedVerb(string name) => Name = name;
        public string Name { get; }
        public IReadOnlyList<string> Synopsis => new[] { $"  jauntyq {Name}" };
        public IReadOnlyList<string> Help => new[] { $"  {Name}: help" };
        public Task<int> RunAsync(string[] args, CliHost host) => Task.FromResult(0);
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static readonly string[] PullUsage =
    {
        "Usage:",
        "  jauntyq schema pull   --provider <provider> --connection <connstr> [--output <path>]",
    };

    private static readonly string[] PullHelp =
    {
        "  schema pull:    reflect the live database into the snapshot at --output",
        "                  (tables, columns, indexes, foreign keys, procedures, sequences).",
    };

    private static readonly string[] Trailer =
    {
        "",
        "Providers:",
        "  postgres    PostgreSQL (Npgsql)",
        "  sqlserver   SQL Server (Microsoft.Data.SqlClient)",
        "  mysql       MySQL (MySqlConnector)",
        "  mariadb     MariaDB (MySqlConnector; snapshot declares the 'mysql' dialect)",
        "  sqlite      SQLite (Microsoft.Data.Sqlite)",
        "",
        "Connection (most secure first):",
        "  --connection-env <VAR>  Read the connection string from environment variable VAR",
        "  JAUNTYQ_CONNECTION      Default env var used when neither flag is given",
        "  --connection <connstr>  Inline (avoid: visible in process list, shell history, CI logs)",
        "",
        "Options:",
        "  --output    Output path, relative to the current directory (default: schema/jaunty.schema.json)",
        "  JAUNTYQ_VERBOSE=1  Print full error detail on failure",
    };

    private static string UsageOf(CliHost host)
    {
        var writer = new StringWriter();
        host.PrintUsage(writer);
        return writer.ToString();
    }

    [Fact]
    public void FreeUsage_IsPrintedLineForLine()
    {
        var expected = new List<string> { "JauntyQ CLI (Extrode.JauntyQ.Cli): schema extraction tool", "" };
        expected.AddRange(PullUsage);
        expected.Add("");
        expected.AddRange(PullHelp);
        expected.Add("");
        expected.AddRange(new[]
        {
            "  Premium commands require Extrode.JauntyQ.Cli.Premium and an entitling license:",
            "    migrate impact, schema verify (and schema verify --service), explain,",
            "    registry, usage export. This tool answers them with exit 3 and the",
            "    install line:",
            "    dotnet tool install --global Extrode.JauntyQ.Cli.Premium --add-source https://nuget.pkg.github.com/extrode/index.json",
            "  Everything else is free and ungated, including schema pull, the source generator",
            "  and every JNTxxxx build diagnostic.",
        });
        expected.AddRange(Trailer);

        Assert.Equal(Lines(expected.ToArray()), UsageOf(new CliHost(CoreVerbs.All)));
    }

    [Fact]
    public void PremiumUsage_IsPrintedLineForLine()
    {
        var host = new CliHost(new IVerb[] { new SchemaPullVerb(), new NamedVerb("schema verify") });

        var expected = new List<string>
        {
            "JauntyQ CLI (Extrode.JauntyQ.Cli.Premium): schema extraction, contract testing and analysis",
            "",
        };
        expected.AddRange(PullUsage);
        expected.Add("  jauntyq schema verify");
        expected.Add("");
        expected.AddRange(PullHelp);
        expected.Add("  schema verify: help");
        expected.Add("");
        expected.AddRange(new[]
        {
            "  Premium commands require an entitling license; without one they exit 3:",
            "    migrate impact, schema verify (and schema verify --service), explain,",
            "    registry, usage export.",
            "  Everything else is free and ungated, including schema pull, the source generator",
            "  and every JNTxxxx build diagnostic.",
            "  A lapsed license still runs them (with a warning).",
            "  Set JAUNTYQ_LICENSE to a license path for CI instead of running 'activate'.",
        });
        expected.AddRange(Trailer);

        Assert.True(host.HasPremiumVerbs);
        Assert.Equal(Lines(expected.ToArray()), UsageOf(host));
    }

    [Fact]
    public async Task PremiumOnlyReply_IsPrintedLineForLine()
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var err = new StringWriter();
        int code;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(err);
            code = await CliHost.Run(new[] { "explain" }, CoreVerbs.All);
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }

        Assert.Equal(CliHost.PremiumOnlyExitCode, code);
        Assert.Equal(Lines(
            "Error: 'jauntyq explain' is part of Extrode.JauntyQ.Cli.Premium, which is not installed.",
            "It replaces this tool and keeps every free verb. Install it with an entitling license:",
            "  dotnet tool install --global Extrode.JauntyQ.Cli.Premium --add-source https://nuget.pkg.github.com/extrode/index.json",
            "Pricing and licenses: https://extrode.com/jauntyq/pricing"), err.ToString());
    }

    private static (bool Ok, string Out, string Err, string Dialect, string Connection, string Output) Prepare(LiveSchemaOptions options)
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            bool ok = LiveSchema.TryPrepare(options, new CliHost(CoreVerbs.All),
                out _, out string dialect, out string connection, out string output);
            return (ok, o.ToString(), e.ToString(), dialect, connection, output);
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    private static void AssertRefusedWithEmptyOuts((bool Ok, string Out, string Err, string Dialect, string Connection, string Output) r)
    {
        Assert.False(r.Ok);
        Assert.Equal("", r.Dialect);
        Assert.Equal("", r.Connection);
        Assert.Equal("", r.Output);
    }

    [Fact]
    public void Prepare_WithoutProvider_RefusesAndPrintsUsage()
    {
        var r = Prepare(new LiveSchemaOptions { Connection = "Data Source=x.db" });

        AssertRefusedWithEmptyOuts(r);
        Assert.Equal(Lines("Error: --provider is required"), r.Err);
        Assert.Equal(UsageOf(new CliHost(CoreVerbs.All)), r.Out);
    }

    [Fact]
    public void Prepare_WithoutConnection_RefusesAndPrintsUsage()
    {
        var r = Prepare(new LiveSchemaOptions { Provider = "sqlite", ConnectionEnv = UnsetConnectionEnv });

        AssertRefusedWithEmptyOuts(r);
        Assert.StartsWith("Error: no connection string.", r.Err);
        Assert.Equal(UsageOf(new CliHost(CoreVerbs.All)), r.Out);
    }

    [Fact]
    public void Prepare_WithEscapingOutput_RefusesNamingTheOutputFlag()
    {
        var r = Prepare(new LiveSchemaOptions { Provider = "sqlite", Connection = "Data Source=x.db", Output = "../out.json" });

        AssertRefusedWithEmptyOuts(r);
        Assert.Equal(Lines("Error: --output '../out.json' escapes the project directory."), r.Err);
        Assert.Equal("", r.Out);
    }

    [Fact]
    public void Prepare_WithUnknownProvider_Refuses()
    {
        var r = Prepare(new LiveSchemaOptions { Provider = "oracle", Connection = "Data Source=x.db" });

        AssertRefusedWithEmptyOuts(r);
        Assert.Equal(Lines(DialectAliases.UnknownProviderMessage("oracle")), r.Err);
    }

    [Fact]
    public void PathSafety_RootedInput_IsRefusedWithItsReason()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "x.json");

        bool ok = PathSafety.TryResolveWithinCurrentDirectory(rooted, "--output", out string resolved, out string? error);

        Assert.False(ok);
        Assert.Equal("", resolved);
        Assert.Equal($"--output must be a relative path inside the project (got rooted path '{rooted}').", error);
    }

    [Fact]
    public void PathSafety_EscapingInput_IsRefusedWithItsReason()
    {
        bool ok = PathSafety.TryResolveWithinCurrentDirectory("../x.json", "--output", out string resolved, out string? error);

        Assert.False(ok);
        Assert.Equal("", resolved);
        Assert.Equal("--output '../x.json' escapes the project directory.", error);
    }

    [Fact]
    public void PathSafety_ASiblingDirectorySharingTheCwdPrefix_IsRefused()
    {
        string cwd = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar);
        string sibling = Path.Combine("..", Path.GetFileName(cwd) + "-sibling", "x.json");

        Assert.False(PathSafety.TryResolveWithinCurrentDirectory(sibling, "--output", out _, out _));
    }

    [Fact]
    public void PathSafety_AtAFilesystemRoot_AcceptsAFileDirectlyUnderIt()
    {
        string saved = Directory.GetCurrentDirectory();
        string root = Path.GetPathRoot(Path.GetFullPath(saved))!;
        try
        {
            Directory.SetCurrentDirectory(root);

            bool ok = PathSafety.TryResolveWithinCurrentDirectory("x.json", "--output", out string resolved, out _);

            Assert.True(ok);
            Assert.Equal(Path.Combine(root, "x.json"), resolved);
        }
        finally
        {
            Directory.SetCurrentDirectory(saved);
        }
    }

    [Fact]
    public async Task SchemaPull_Success_ReportsTheResolvedPathWritten()
    {
        string dbPath = Path.Combine(Path.GetFullPath(_relativeDir), "probe.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY);";
            cmd.ExecuteNonQuery();
        }
        string output = Path.Combine(_relativeDir, "snap.json");

        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        int code;
        try
        {
            Console.SetOut(o);
            Console.SetError(TextWriter.Null);
            code = await CliHost.Run(new[]
            {
                "schema", "pull", "--provider", "sqlite", "--connection", $"Data Source={dbPath}", "--output", output,
            }, CoreVerbs.All);
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }

        Assert.Equal(0, code);
        Assert.EndsWith(Lines($"Schema written to {Path.GetFullPath(output)}"), o.ToString());
    }
}
