using Microsoft.Data.Sqlite;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// AUD-R86-04: drives the real <see cref="Program.Main"/> end to end against a
/// throwaway SQLite database, because the defect this class pins is invisible
/// to every unit test in the suite — <c>SchemaVerifyReporter</c> is tested with
/// injected writers, so nothing else asserts what the *process* writes to
/// stdout, which is the only thing a CI consumer of <c>--format json</c> reads.
///
/// `schema verify` itself cannot be driven here: it is entitlement-gated and no
/// entitling license can exist against the embedded key on a test machine, so
/// the verify half is pinned at <see cref="LiveSchema.ProgressStream"/>, the one
/// place the stream decision is made. The round-86 report records the manual
/// gate-removed repro that covers the composed verify stdout.
/// </summary>
[Trait("Category", "AuditRegression")]
[Collection("cli-console")]
public class SchemaVerifyStdoutTests : IDisposable
{
    private readonly string _relativeDir = "jq-verify-stdout-" + Guid.NewGuid().ToString("N");
    private readonly string _dbPath;
    private readonly string _snapshotPath;

    public SchemaVerifyStdoutTests()
    {
        Directory.CreateDirectory(_relativeDir);
        _dbPath = Path.Combine(Path.GetFullPath(_relativeDir), "probe.db");
        _snapshotPath = Path.Combine(_relativeDir, "jaunty.schema.json");

        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetFullPath(_relativeDir), recursive: true); } catch { /* best effort */ }
    }

    private string ConnectionString => $"Data Source={_dbPath}";

    private async Task<(int Code, string Out, string Err)> RunAsync(params string[] args)
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

    [Fact]
    public async Task SchemaPull_KeepsItsProgressLinesOnStdout()
    {
        // The opposite assertion to the verify case, and the reason the fix is
        // per-verb: for `pull` these lines are the verb's result, not a notice
        // ahead of a document, and no --format switch can make stdout
        // machine-readable here.
        var (code, outText, _) = await RunAsync(
            "schema", "pull", "--provider", "sqlite", "--connection", ConnectionString, "--output", _snapshotPath);

        Assert.Equal(0, code);
        Assert.Contains("Connecting to sqlite", outText);
        Assert.Contains("Extracted 1 table(s)", outText);
    }

    [Fact]
    public void ProgressStream_ForVerify_IsStderr_SoTheJsonDocumentStandsAlone()
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            LiveSchema.ProgressStream(verify: true).WriteLine("Connecting to sqlite...");
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }

        Assert.Contains("Connecting to sqlite", e.ToString());
        Assert.DoesNotContain("Connecting to sqlite", o.ToString());
    }

    [Fact]
    public void ProgressStream_ForPull_StaysOnStdout()
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            LiveSchema.ProgressStream(verify: false).WriteLine("Connecting to sqlite...");
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }

        Assert.Contains("Connecting to sqlite", o.ToString());
        Assert.DoesNotContain("Connecting to sqlite", e.ToString());
    }
}
