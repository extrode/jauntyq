using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Pins the rule in <see cref="LiveServerGate"/> and the contract it holds with
/// <c>scripts/skip-audit.js</c>. Measured 2026-08-29: on a machine with no local Northwind the
/// SQL Server fixture skipped four tests with a reason skip-audit does not sanction, so
/// <c>scripts/test-all.sh</c> failed for an entirely expected condition — and the two remaining
/// fixtures skipped when their variable WAS set and the server was down, which is the silent
/// green the skip audit exists to prevent, one layer below where it can see it.
///
/// The rule is tested through the gate rather than by setting environment variables, because
/// the variables are process-wide and this suite runs its collections in parallel.
/// </summary>
[Trait("Category", "AuditRegression")]
public class LiveServerGateTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void NothingAskedFor_Skips()
    {
        string reason = LiveServerGate.SkipOrThrow(
            "JAUNTYQ_TEST_SQLSERVER_CONNECTION", null, "SQL Server", new Exception("no server"));

        Assert.Contains("JAUNTYQ_TEST_SQLSERVER_CONNECTION not set", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionStringWasGiven_AndTheServerIsDown_Fails()
    {
        Exception cause = new("connection refused");

        var thrown = Assert.Throws<InvalidOperationException>(() => LiveServerGate.SkipOrThrow(
            "JAUNTYQ_TEST_POSTGRES_CONNECTION", "Host=localhost;Port=1", "Postgres", cause));

        Assert.Contains("is set but the server did not accept a connection", thrown.Message, StringComparison.Ordinal);

        Assert.Same(cause, thrown.InnerException);
    }

    /// <summary>
    /// The reason a developer reads has to say which variable to set and which engine went
    /// unexercised; a bare "skipped" sends them to the source to find out what they missed.
    /// </summary>
    [Theory]
    [InlineData("JAUNTYQ_TEST_POSTGRES_CONNECTION", "Postgres")]
    [InlineData("JAUNTYQ_TEST_MYSQL_CONNECTION", "MySQL")]
    [InlineData("JAUNTYQ_TEST_SQLSERVER_CONNECTION", "SQL Server")]
    public void TheSkipReason_NamesTheVariableAndTheEngine(string variable, string engine)
    {
        string reason = LiveServerGate.NotSet(variable, engine);

        Assert.Contains(variable + " not set", reason, StringComparison.Ordinal);
        Assert.Contains(engine, reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wording is a contract with another language. skip-audit.js fails any run carrying a
    /// skip whose reason it cannot match, so this reads its allowlist regex out of the script
    /// and applies it to what the gate actually produces — rather than restating the pattern
    /// here, where the two could agree with each other and disagree with the script.
    /// </summary>
    [Theory]
    [InlineData("JAUNTYQ_TEST_POSTGRES_CONNECTION", "Postgres")]
    [InlineData("JAUNTYQ_TEST_MYSQL_CONNECTION", "MySQL")]
    [InlineData("JAUNTYQ_TEST_SQLSERVER_CONNECTION", "SQL Server")]
    public void EverySkipReason_IsOneSkipAuditSanctions(string variable, string engine)
    {
        string script = File.ReadAllText(
            Path.Combine(RepoRoot(), "scripts", "skip-audit.js")).Replace("\r\n", "\n");

        Match declared = Regex.Match(script, @"pattern: /(?<pattern>JAUNTYQ_TEST[^/]*)/");
        Assert.True(declared.Success,
            "scripts/skip-audit.js no longer declares an env-gated-live-database pattern, so " +
            "this test is asserting nothing and the skip reasons below are unguarded.");

        string reason = LiveServerGate.NotSet(variable, engine);

        Assert.Matches(declared.Groups["pattern"].Value, reason);
    }
}
