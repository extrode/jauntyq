using Xunit;

namespace JauntyQ.Cli.Tests;

/// <summary>
/// AUD-R88-01. <c>JAUNTYQ_VERBOSE</c> gates whether a provider exception's raw
/// message — which can echo host and credential fragments out of a connection
/// string (CWE-532) — reaches the log. It was read as a bare literal at three
/// unrelated sites, each with its own second literal <c>"1"</c>; all three now
/// go through <see cref="CliEnvironment"/>.
///
/// The structural half of the fix is what closes the reported defect: one
/// definition cannot diverge from itself, so the cross-platform casing hazard
/// (<c>GetEnvironmentVariable</c> is case-insensitive on Windows and
/// case-sensitive on Linux and macOS) has nowhere left to bite. What is left to
/// test is the reading itself, because the collapse to one site is also the
/// moment a subtly different reading would propagate to all three at once.
/// </summary>
[Collection("cli-console")]
[Trait("Category", "AuditRegression")]
public class CliEnvironmentTests : IDisposable
{
    private readonly string? _saved;

    public CliEnvironmentTests() => _saved = Environment.GetEnvironmentVariable(CliEnvironment.VerboseEnvVar);

    public void Dispose() => Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, _saved);

    [Fact]
    public void OptInValue_EnablesVerboseErrors()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, CliEnvironment.VerboseOptIn);
        Assert.True(CliEnvironment.VerboseErrors);
    }

    /// <summary>
    /// Every one of these was off before the constant was introduced, and must
    /// stay off. <c>"true"</c> and <c>"yes"</c> are the readings a permissive
    /// parse would add; they are refused deliberately, because guessing at
    /// intent in the direction of printing more of an exception is the wrong
    /// direction to be generous in. <c>" 1"</c> and <c>"1\n"</c> cover the shell
    /// that exports a value with stray whitespace.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("11")]
    [InlineData("2")]
    public void EveryOtherValue_LeavesVerboseErrorsOff(string? value)
    {
        Environment.SetEnvironmentVariable(CliEnvironment.VerboseEnvVar, value);
        Assert.False(CliEnvironment.VerboseErrors);
    }

    /// <summary>
    /// The variable's name is a published contract: it appears in the CLI's own
    /// help output, in <c>docs/06-reference/cli.md</c>, in <c>SECURITY.md</c> and
    /// in the constitution. Renaming the constant would compile everywhere and
    /// break every one of those documents silently.
    /// </summary>
    [Fact]
    public void TheNameAndOptInValue_AreThePublishedOnes()
    {
        Assert.Equal("JAUNTYQ_VERBOSE", CliEnvironment.VerboseEnvVar);
        Assert.Equal("1", CliEnvironment.VerboseOptIn);
    }
}
