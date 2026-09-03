using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// The generated output set survives a rebuild.
///
/// a consumer reported twice that a rebuild left them with no generated code at all —
/// every consumer file failing CS0246, no JNT diagnostic anywhere, and a clean rebuild
/// fixing it (<c>the consumer report</c>,
/// item 1). The cause is undiagnosed in both repos and this suite does not claim to have
/// found it: it cannot reach MSBuild's analyzer loading, the IDE's generator host, or a
/// stale <c>obj/</c>, which are the three places it most plausibly lives.
///
/// What it does cover is the layer that IS reachable from here and had no test at all —
/// the driver being run more than once. Every other generator test in this repo runs it
/// exactly once, so "the second run produces what the first did" was, until this file,
/// an untested property of a generator whose whole design is incremental caching.
///
/// The assertions compare hint names AND text. Names alone would pass a rebuild that
/// emitted the right set of files with empty bodies, which is nearer to the reported
/// symptom than a missing file is.
/// </summary>
[Trait("Category", "AuditRegression")]
public class IncrementalRebuildOutputTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""text"", ""isNullable"": false },
        ""price"": { ""name"": ""price"", ""dbType"": ""real"", ""isNullable"": false }
      }
    }
  }
}";

    private const string GetAllSql = "select id, name, price from widgets";
    private const string GetByNameSql = "select id, price from widgets where widgets.name = @name";

    private static GeneratorDriver CreateDriver(params AdditionalText[] texts)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator());
        driver = driver.AddAdditionalTexts(ImmutableArray.Create(texts));
        return driver.WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));
    }

    private static CSharpCompilation CreateCompilation(string source = "") =>
        CSharpCompilation.Create("RebuildTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>Hint name to generated text, for the whole run.</summary>
    private static SortedDictionary<string, string> Emitted(GeneratorDriver driver)
    {
        var map = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var result in driver.GetRunResult().Results)
            foreach (var source in result.GeneratedSources)
                map[source.HintName] = source.SourceText.ToString();
        return map;
    }

    private static AdditionalText[] Inputs() => new AdditionalText[]
    {
        new InMemoryAdditionalText("db/schema/jaunty.schema.json", SchemaJson),
        new InMemoryAdditionalText("db/Widgets/GetAll.sql", GetAllSql),
        new InMemoryAdditionalText("db/Widgets/GetByName.sql", GetByNameSql),
    };

    private static void AssertSameOutput(
        SortedDictionary<string, string> first, SortedDictionary<string, string> second, string what)
    {
        Assert.True(first.Keys.SequenceEqual(second.Keys),
            what + ": the emitted file set changed. Only in the first run: "
            + string.Join(", ", first.Keys.Except(second.Keys)) + ". Only in the second: "
            + string.Join(", ", second.Keys.Except(first.Keys)) + ".");

        foreach (string hint in first.Keys)
            Assert.True(first[hint] == second[hint],
                what + ": '" + hint + "' was emitted both times but its content changed.");
    }

    /// <summary>
    /// The vacuity guard. Every comparison below is between two runs, so two empty runs
    /// satisfy all of them — which is precisely the failure being tested for.
    /// </summary>
    [Fact]
    public void TheFirstRunEmitsARealSurface_SoTheComparisonsBelowAreNotBetweenTwoEmptySets()
    {
        var emitted = Emitted(CreateDriver(Inputs()).RunGenerators(CreateCompilation()));

        Assert.True(emitted.Count >= 3,
            "Only " + emitted.Count + " sources were generated on a clean run.");

        string all = string.Concat(emitted.Values);
        Assert.Contains("JauntyDb", all);
        Assert.Contains("GetAll", all);
        Assert.Contains("GetByName", all);
    }

    /// <summary>
    /// Two drivers, built from scratch, over identical inputs. This is the strong form
    /// and it exists because the same-driver reruns below turned out to be weaker than
    /// they look: with no input change Roslyn reuses the cached source-output result and
    /// never calls back into the emit step at all. A mutation that appended a per-run
    /// counter to every emitted file <b>survived</b> all three of them, because the
    /// counter was only ever incremented once. It is killed here.
    ///
    /// The property is also the one that matters for a real rebuild: a second MSBuild
    /// invocation is a new process with a new driver, so it shares nothing with the
    /// first except the files on disk.
    /// </summary>
    [Fact]
    public void TwoIndependentDrivers_ProduceByteIdenticalOutput()
    {
        var first = Emitted(CreateDriver(Inputs()).RunGenerators(CreateCompilation()));
        var second = Emitted(CreateDriver(Inputs()).RunGenerators(CreateCompilation()));

        AssertSameOutput(first, second, "two independent runs");
    }

    [Fact]
    public void RunningTheDriverASecondTimeWithNoChange_EmitsTheSameFiles()
    {
        // The reported symptom in its simplest form: nothing changed, run it again.
        var driver = CreateDriver(Inputs());
        var compilation = CreateCompilation();

        driver = driver.RunGenerators(compilation);
        var first = Emitted(driver);

        driver = driver.RunGenerators(compilation);
        AssertSameOutput(first, Emitted(driver), "a no-change rerun");
    }

    [Fact]
    public void ReplacingTheCompilation_LeavesTheGeneratedSurfaceIntact()
    {
        // A real rebuild does not reuse the first build's Compilation object; it parses
        // the consumer's source afresh. The additional texts are what JauntyQ reads, and
        // none of them changed, so neither may the output.
        var driver = CreateDriver(Inputs());

        driver = driver.RunGenerators(CreateCompilation("class Untouched { }"));
        var first = Emitted(driver);

        driver = driver.RunGenerators(CreateCompilation("class Untouched { }\nclass Added { }"));
        AssertSameOutput(first, Emitted(driver), "a rerun against a new compilation");
    }

    [Fact]
    public void AddingAFileThenRemovingIt_ReturnsToTheOriginalSurfaceExactly()
    {
        // The shape most likely to leave a stale or emptied cache node behind, and the
        // one an editor produces constantly.
        var inputs = Inputs();
        var driver = CreateDriver(inputs);
        var compilation = CreateCompilation();

        driver = driver.RunGenerators(compilation);
        var first = Emitted(driver);

        var extra = new InMemoryAdditionalText(
            "db/Widgets/GetCheap.sql", "select id from widgets where widgets.price < @price");

        driver = driver.AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(extra))
            .RunGenerators(compilation);
        var withExtra = Emitted(driver);
        Assert.True(withExtra.Count > first.Count,
            "Adding a .sql file emitted no extra source, so the removal below proves nothing.");

        driver = driver.RemoveAdditionalTexts(ImmutableArray.Create<AdditionalText>(extra))
            .RunGenerators(compilation);
        AssertSameOutput(first, Emitted(driver), "add-then-remove");
    }

    [Fact]
    public void ABodyOnlyEdit_ChangesThatOneFileAndLeavesEveryOtherByteAlone()
    {
        var schema = new InMemoryAdditionalText("db/schema/jaunty.schema.json", SchemaJson);
        var getAll = new InMemoryAdditionalText("db/Widgets/GetAll.sql", GetAllSql);
        var getByName = new InMemoryAdditionalText("db/Widgets/GetByName.sql", GetByNameSql);

        var driver = CreateDriver(schema, getAll, getByName);
        var compilation = CreateCompilation();

        driver = driver.RunGenerators(compilation);
        var first = Emitted(driver);

        var edited = new InMemoryAdditionalText(getByName.Path,
            "select id, price from widgets where widgets.name <> @name");
        driver = driver.ReplaceAdditionalText(getByName, edited).RunGenerators(compilation);
        var second = Emitted(driver);

        Assert.True(first.Keys.SequenceEqual(second.Keys),
            "A body-only edit changed the emitted file SET, not just one file's content.");

        var changed = first.Keys.Where(k => first[k] != second[k]).ToList();
        Assert.True(changed.Count == 1,
            "Expected one changed file after a body-only edit, got " + changed.Count
            + ": " + string.Join(", ", changed));
        Assert.Contains("GetByName", changed[0]);
    }
}
