using Xunit;

namespace JauntyQ.Cli.Tests;

/// <summary>
/// AUD-R88-02. The usage block is the only documentation a user reaches without
/// leaving the terminal, and it is prose with nothing checking it against the
/// arg switches it describes.
///
/// These pin the free tool's block: the verb it carries, the premium verbs it
/// names as living elsewhere, and the env-var and provider names, not the
/// wording around them, which is free to change.
/// </summary>
[Collection("cli-console")]
[Trait("Category", "AuditRegression")]
public class CliUsageTextTests
{
    private static string Usage()
    {
        var saved = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            int exit = Program.Main(Array.Empty<string>()).GetAwaiter().GetResult();
            Assert.Equal(1, exit);
        }
        finally
        {
            Console.SetOut(saved);
        }
        return writer.ToString();
    }

    [Fact]
    public void TheFreeVerb_HasAUsageLine() =>
        Assert.Contains("  jauntyq schema pull", Usage());

    [Theory]
    [InlineData("jauntyq schema verify")]
    [InlineData("jauntyq explain")]
    [InlineData("jauntyq registry")]
    [InlineData("jauntyq activate")]
    [InlineData("jauntyq license")]
    public void PremiumVerbs_HaveNoUsageLine_InTheFreeTool(string verb) =>
        Assert.DoesNotContain(verb, Usage());

    [Fact]
    public void TheRetiredCommandName_IsNeverPrinted() =>
        Assert.DoesNotContain("jaunty ", Usage());

    [Fact]
    public void EnvironmentVariables_AreAdvertisedUnderTheNamesTheCodeReads()
    {
        string usage = Usage();
        Assert.Contains($"{CliEnvironment.VerboseEnvVar}={CliEnvironment.VerboseOptIn}", usage);
        Assert.Contains(CliEnvironment.ConnectionEnvVar, usage);
    }

    private static string PremiumParagraph()
    {
        string usage = Usage();
        int start = usage.IndexOf("Premium commands require", StringComparison.Ordinal);
        Assert.True(start >= 0, "usage block no longer names the premium commands at all");
        int end = usage.IndexOf("Everything else", start, StringComparison.Ordinal);
        Assert.True(end > start, "premium list no longer ends with the statement of what is free");
        return usage.Substring(start, end - start);
    }

    [Theory]
    [InlineData("migrate impact")]
    [InlineData("schema verify")]
    [InlineData("schema verify --service")]
    [InlineData("explain")]
    [InlineData("registry")]
    [InlineData("usage export")]
    public void EveryGatedCommand_IsNamedAsPremium(string command) =>
        Assert.Contains(command, PremiumParagraph());

    [Fact]
    public void ThePremiumParagraph_NamesThePackageAndTheInstallLine()
    {
        string paragraph = PremiumParagraph();
        Assert.Contains(CliHost.PremiumPackageId, paragraph);
        Assert.Contains($"dotnet tool install --global {CliHost.PremiumPackageId}", paragraph);
    }

    [Theory]
    [InlineData("schema pull")]
    [InlineData("activate")]
    public void UngatedCommands_AreNotNamedAsPremium(string command) =>
        Assert.DoesNotContain(command, PremiumParagraph());

    [Fact]
    public void EverySupportedProvider_AppearsInTheProviderList()
    {
        string usage = Usage();
        foreach (var provider in DialectAliases.SupportedProviders.Split(", "))
            Assert.Contains(provider, usage);
    }
}
