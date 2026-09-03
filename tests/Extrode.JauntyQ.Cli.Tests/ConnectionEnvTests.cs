using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// AUD-R89-01. <c>JAUNTYQ_CONNECTION</c> holds a database credential, and the
/// rule for when it is consulted — <c>--connection-env</c> names a variable and
/// overrides it, otherwise this is the default — was written out twice, once in
/// <c>Program.cs</c> for <c>schema pull/verify</c> and once in
/// <c>ExplainCli.cs</c>. Two copies of a decision about where a secret lives is
/// the same shape that drifted in AUD-R26-01, and the platform asymmetry from
/// AUD-R88-01 applies unchanged: a casing divergence would leave the two verbs
/// disagreeing on Linux and macOS while the Windows suite stayed green.
///
/// The copies are now one <see cref="CliEnvironment.ReadConnectionString"/>.
/// What is left to pin is the rule itself, because collapsing to one site is
/// also the point at which a changed rule reaches both verbs at once.
/// </summary>
[Collection("cli-console")]
[Trait("Category", "AuditRegression")]
public class ConnectionEnvTests : IDisposable
{
    private const string Alternate = "JAUNTYQ_TEST_ALTERNATE_CONNECTION";

    private readonly string? _savedDefault;
    private readonly string? _savedAlternate;

    public ConnectionEnvTests()
    {
        _savedDefault = Environment.GetEnvironmentVariable(CliEnvironment.ConnectionEnvVar);
        _savedAlternate = Environment.GetEnvironmentVariable(Alternate);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, _savedDefault);
        Environment.SetEnvironmentVariable(Alternate, _savedAlternate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithNoNamedVariable_TheDefaultIsRead(string? connectionEnv)
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, "Host=default");
        Assert.Equal("Host=default", CliEnvironment.ReadConnectionString(connectionEnv));
    }

    [Fact]
    public void ANamedVariable_WinsOverTheDefault()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, "Host=default");
        Environment.SetEnvironmentVariable(Alternate, "Host=named");
        Assert.Equal("Host=named", CliEnvironment.ReadConnectionString(Alternate));
    }

    /// <summary>
    /// The override is total, not the head of a fallback chain. A user who asks
    /// for a specific variable and has not set it must get "no connection"
    /// rather than silently connecting to whatever the default points at —
    /// which, for a credential, is the difference between a failed run and a
    /// run against the wrong database.
    /// </summary>
    [Fact]
    public void ANamedVariableThatIsUnset_DoesNotFallBackToTheDefault()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, "Host=default");
        Environment.SetEnvironmentVariable(Alternate, null);
        Assert.Null(CliEnvironment.ReadConnectionString(Alternate));
    }

    [Fact]
    public void WithNothingSet_TheResultIsNull()
    {
        Environment.SetEnvironmentVariable(CliEnvironment.ConnectionEnvVar, null);
        Assert.Null(CliEnvironment.ReadConnectionString(null));
    }

    /// <summary>
    /// The name is a published contract: <c>docs/06-reference/cli.md</c>,
    /// <c>SECURITY.md</c> and the getting-started guide all instruct users to
    /// export it under this exact spelling.
    /// </summary>
    [Fact]
    public void TheNameIsThePublishedOne() =>
        Assert.Equal("JAUNTYQ_CONNECTION", CliEnvironment.ConnectionEnvVar);
}
