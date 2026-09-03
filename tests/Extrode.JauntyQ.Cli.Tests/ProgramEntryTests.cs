using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

/// <summary>
/// Drives the real CLI entry point (<see cref="Program.Main"/>, exposed via
/// InternalsVisibleTo) to cover the argument router and its private
/// <c>PrintUsage</c> / <c>TryResolveOutputPath</c> helpers — all offline: these
/// paths return before any database connection is attempted.
/// </summary>
[Collection("cli-console")]
public class ProgramEntryTests
{
    /// <summary>Runs Main with Console redirected to keep test output clean.</summary>
    private static async Task<int> RunQuiet(params string[] args)
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            return await Program.Main(args);
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    [Fact]
    public async Task NoArguments_PrintsUsage_AndReturnsError()
    {
        Assert.Equal(1, await RunQuiet());
    }

    [Fact]
    public async Task UnknownCommand_PrintsUsage_AndReturnsError()
    {
        Assert.Equal(1, await RunQuiet("wat", "--nonsense"));
    }

    [Theory]
    [InlineData("schema", "verify")]
    [InlineData("registry", "list")]
    [InlineData("usage", "export")]
    [InlineData("migrate", "impact")]
    [InlineData("explain")]
    [InlineData("activate")]
    [InlineData("license", "status")]
    public async Task PremiumVerb_InTheFreeTool_ExitsWithThePremiumOnlyCode(params string[] args)
    {
        Assert.Equal(CliHost.PremiumOnlyExitCode, await RunQuiet(args));
    }

    [Fact]
    public async Task SchemaPull_RootedOutputPath_Rejected()
    {
        string rooted = Path.Combine(Path.GetTempPath(), "jauntyq-out.json");
        Assert.True(Path.IsPathRooted(rooted));

        int exit = await RunQuiet("schema", "pull",
            "--provider", "sqlite", "--connection", "ignored",
            "--output", rooted);

        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task SchemaPull_EscapingOutputPath_Rejected()
    {
        int exit = await RunQuiet("schema", "pull",
            "--provider", "sqlite", "--connection", "ignored",
            "--output", "../escapes-project.json");

        Assert.Equal(1, exit);
    }
}
