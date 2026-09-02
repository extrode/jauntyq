using Xunit;

namespace JauntyQ.Cli.Tests;

[Collection("cli-console")]
public class CliHostTests
{
    private sealed class FakeVerb : IVerb
    {
        public FakeVerb(string name, int exit = 0) { Name = name; Exit = exit; }
        public string Name { get; }
        public int Exit { get; }
        public string[]? ReceivedArgs { get; private set; }
        public IReadOnlyList<string> Synopsis => new[] { $"  {CliHost.CommandName} {Name} [--flag]" };
        public IReadOnlyList<string> Help => new[] { $"  {Name}: fake" };
        public Task<int> RunAsync(string[] args, CliHost host)
        {
            ReceivedArgs = args;
            return Task.FromResult(Exit);
        }
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(IReadOnlyList<IVerb> verbs, params string[] args)
    {
        TextWriter savedOut = Console.Out, savedErr = Console.Error;
        var o = new StringWriter();
        var e = new StringWriter();
        try
        {
            Console.SetOut(o);
            Console.SetError(e);
            return (await CliHost.Run(args, verbs), o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }

    [Fact]
    public async Task TwoWordVerb_ReceivesOnlyTheArgumentsAfterItsName()
    {
        var pull = new FakeVerb("schema pull", exit: 7);

        var (code, _, _) = await RunAsync(new[] { pull }, "schema", "pull", "--provider", "sqlite");

        Assert.Equal(7, code);
        Assert.Equal(["--provider", "sqlite"], pull.ReceivedArgs!);
    }

    [Fact]
    public async Task OneWordVerb_ReceivesItsSubcommandAsAnArgument()
    {
        var registry = new FakeVerb("registry");

        await RunAsync(new[] { registry }, "registry", "list");

        Assert.Equal(["list"], registry.ReceivedArgs!);
    }

    [Fact]
    public async Task TheLongestMatchingName_Wins_RegardlessOfRegistrationOrder()
    {
        var one = new FakeVerb("schema", exit: 1);
        var two = new FakeVerb("schema pull", exit: 2);

        var (code, _, _) = await RunAsync(new IVerb[] { one, two }, "schema", "pull");
        Assert.Equal(2, code);

        var (reversed, _, _) = await RunAsync(new IVerb[] { two, one }, "schema", "pull");
        Assert.Equal(2, reversed);
    }

    [Fact]
    public async Task APartialVerb_IsAUsageError()
    {
        var pull = new FakeVerb("schema pull");

        var (code, outText, _) = await RunAsync(new[] { pull }, "schema");

        Assert.Equal(1, code);
        Assert.Null(pull.ReceivedArgs);
        Assert.Contains("Usage:", outText);
    }

    [Fact]
    public async Task NoArguments_PrintsUsage_ListingEveryVerbSynopsis()
    {
        var (code, outText, _) = await RunAsync(new IVerb[] { new FakeVerb("alpha"), new FakeVerb("beta gamma") });

        Assert.Equal(1, code);
        Assert.Contains("  jauntyq alpha [--flag]", outText);
        Assert.Contains("  jauntyq beta gamma [--flag]", outText);
        Assert.Contains("  alpha: fake", outText);
    }

    [Theory]
    [InlineData("schema verify", "schema", "verify")]
    [InlineData("explain", "explain")]
    [InlineData("license", "license", "status")]
    public async Task AKnownPremiumVerb_WithoutPremiumRegistered_ExitsThreeNamingTheInstall(string verbName, params string[] args)
    {
        var (code, outText, err) = await RunAsync(CoreVerbs.All, args);

        Assert.Equal(CliHost.PremiumOnlyExitCode, code);
        Assert.Contains(CliHost.PremiumPackageId, err);
        Assert.Contains($"dotnet tool install --global {CliHost.PremiumPackageId} --add-source {CliHost.PremiumFeed}", err);
        Assert.Contains($"'{CliHost.CommandName} {verbName}'", err);
        Assert.DoesNotContain("Usage:", outText);
    }

    [Fact]
    public async Task AKnownPremiumVerb_WithPremiumRegistered_IsDispatchedNotRefused()
    {
        var verify = new FakeVerb("schema verify", exit: 5);

        var (code, _, err) = await RunAsync(new[] { verify }, "schema", "verify");

        Assert.Equal(5, code);
        Assert.Empty(err);
    }

    [Fact]
    public void EveryPremiumVerbName_IsAbsentFromTheCoreList()
    {
        foreach (var verb in CoreVerbs.All)
            Assert.DoesNotContain(verb.Name, CliHost.PremiumVerbNames);
    }

    [Fact]
    public void ADuplicateVerbName_IsRejectedAtConstruction()
    {
        var ex = Assert.Throws<ArgumentException>(() => new CliHost(new IVerb[] { new FakeVerb("x"), new FakeVerb("x") }));

        Assert.Contains("'x'", ex.Message);
    }

    [Fact]
    public void FreeUsage_NamesThePremiumPackage_AndNeverTheRetiredCommand()
    {
        var writer = new StringWriter();
        new CliHost(CoreVerbs.All).PrintUsage(writer);
        string usage = writer.ToString();

        Assert.Contains(CliHost.PremiumPackageId, usage);
        Assert.Contains("jauntyq schema pull", usage);
        Assert.DoesNotContain("jaunty ", usage);
    }

    [Fact]
    public void SkipArgs_PastTheEnd_IsEmpty()
    {
        Assert.Empty(CliHost.SkipArgs(new[] { "a" }, 2));
        Assert.Equal(["b"], CliHost.SkipArgs(new[] { "a", "b" }, 1));
    }
}
