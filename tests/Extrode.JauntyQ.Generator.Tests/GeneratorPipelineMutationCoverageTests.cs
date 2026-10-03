using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class GeneratorPipelineMutationCoverageTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""text"", ""isNullable"": false }
      }
    }
  }
}";

    private sealed class NullTextAdditionalText : AdditionalText
    {
        public NullTextAdditionalText(string path) => Path = path;

        public override string Path { get; }

        public override SourceText? GetText(CancellationToken cancellationToken = default) => null;
    }

    private static GeneratorDriverRunResult Run(string? dialect, params AdditionalText[] files)
    {
        var compilation = CSharpCompilation.Create("PipelineTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new JauntyQGenerator().AsSourceGenerator() },
                driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true))
            .AddAdditionalTexts(files.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false, dialect));

        return driver.RunGenerators(compilation).GetRunResult();
    }

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files) =>
        Run(null, files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToArray());

    private static AdditionalText Text(string path, string text) => new InMemoryAdditionalText(path, text);

    [Fact]
    public void EveryPipelineNodeCarriesItsTrackingName()
    {
        var steps = Run(("schema/jaunty.schema.json", SchemaJson), ("db/Widgets/GetAll.sql", "select id from widgets"))
            .Results.Single().TrackedSteps.Keys;

        Assert.Subset(steps.ToHashSet(), new[]
        {
            "JauntyQ_SchemaCandidates", "JauntyQ_CommonPrefix", "JauntyQ_PerFile", "JauntyQ_HintCollisions",
            "JauntyQ_Fingerprints", "JauntyQ_NPlusOne", "JauntyQ_PredicateDrift", "JauntyQ_Summaries",
            "JauntyQ_AcceptCandidates", "JauntyQ_AggregateInput",
        }.ToHashSet());
    }

    [Fact]
    public void TwoSchemaSnapshots_JNT6002Message()
    {
        var result = Run(("schema/z.schema.json", SchemaJson), ("schema/a.schema.json", SchemaJson));

        Assert.Equal(
            "Found 2 schema snapshots (schema/a.schema.json, schema/z.schema.json); using 'schema/a.schema.json'. "
            + "Remove the extra *.schema.json files (or exclude them from AdditionalFiles) so the schema source is unambiguous.",
            Assert.Single(result.Diagnostics, d => d.Id == "JNT6002").GetMessage());
    }

    [Fact]
    public void TwoAcceptanceFiles_JNT6003Message()
    {
        var result = Run(
            ("schema/jaunty.schema.json", SchemaJson),
            ("db/z.accept.json", "{}"),
            ("db/a.accept.json", "{}"));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT6003" && d.GetMessage() ==
            "Found 2 acceptance files (db/a.accept.json, db/z.accept.json); using 'db/a.accept.json'. "
            + "Acceptances in the others are ignored entirely -- they are not merged. Keep one "
            + "*.accept.json (or exclude the extras from AdditionalFiles).");
    }

    [Fact]
    public void TwoSnapshotsAtTheSamePath_TheFirstListedWins()
    {
        var result = Run(
            ("schema/jaunty.schema.json", SchemaJson),
            ("schema/jaunty.schema.json", "{ not json"),
            ("db/Widgets/GetAll.sql", "select id from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT6001");
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Widgets.GetAll.g.cs"));
    }

    [Fact]
    public void NonSqlFilesUnderMigrationsAndDdl_AreNotApplied()
    {
        var result = Run("sqlite",
            Text("db/ddl/schema.sql", "create table widgets (id integer primary key, name text not null)"),
            Text("db/ddl/notes.txt", "this is not sql at all"),
            Text("db/migrations/notes.txt", "neither is this"),
            Text("db/Widgets/GetAll.sql", "select id, name from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("JNT9"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Widgets.GetAll.g.cs"));
    }

    [Fact]
    public void UnreadableMigrationAndDdlFiles_ReadAsEmpty()
    {
        var result = Run("sqlite",
            Text("db/ddl/schema.sql", "create table widgets (id integer primary key, name text not null)"),
            new NullTextAdditionalText("db/ddl/gone.sql"),
            new NullTextAdditionalText("db/migrations/V1__gone.sql"),
            Text("db/Widgets/GetAll.sql", "select id, name from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("JNT9"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Widgets.GetAll.g.cs"));
    }

    [Fact]
    public void AHintCollision_SuppressesOnlyTheCollidingFiles()
    {
        var result = Run(
            ("schema/jaunty.schema.json", SchemaJson),
            ("db/Widgets/getAll.sql", "select id from widgets"),
            ("db/Widgets/GetAll.sql", "select name from widgets"),
            ("db/Widgets/Names.sql", "select name from widgets where id = @id"));

        Assert.Equal(2, result.Diagnostics.Count(d => d.Id == "JNT2008"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("Widgets.Names.g.cs"));
    }

    [Fact]
    public void DuplicateQueries_JNT8005NamesMembersInOrderAndAnchorsOnTheFirst()
    {
        var result = Run(
            ("schema/jaunty.schema.json", SchemaJson),
            ("db/Widgets/Zed.sql", "select id from widgets"),
            ("db/Widgets/Alpha.sql", "SELECT  id FROM widgets"));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "JNT8005");
        Assert.Equal(
            "Queries Widgets.Alpha, Widgets.Zed compile to identical SQL; consolidate them to keep one plan and one maintenance point.",
            diagnostic.GetMessage());
        Assert.Equal("db/Widgets/Alpha.sql", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void NoSchema_CrossQueryAnalysesStandDown()
    {
        var result = Run(("db/Widgets/GetAll.sql", "select id from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
    }

    [Fact]
    public void NoFilesAndNoSnapshot_EmitsOnlyTheShapeGuard()
    {
        var result = Run();

        var source = Assert.Single(result.Results.Single().GeneratedSources);
        Assert.Equal("JauntyQShapeGuard.g.cs", source.HintName);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ComputeHintCollisions_GroupsCaseInsensitivelyAndSortsByPath()
    {
        var collisions = JauntyQGenerator.ComputeHintCollisions(ImmutableArray.Create<(string?, string?)>(
            ("B.g.cs", "y2.sql"),
            ("b.g.cs", "y1.sql"),
            ("A.g.cs", "x2.sql"),
            ("a.g.cs", "x1.sql"),
            ("C.g.cs", "c.sql"),
            (null, "n.sql"),
            ("C.g.cs", null),
            ("", "e.sql"),
            ("C.g.cs", "")));

        const string Tail = "Generated file names are compared case-insensitively, so these collide and would abort code generation. Rename one so the generated names differ by more than case.";
        Assert.Equal(new (string, string)[]
        {
            ("x1.sql", "SQL files [x1.sql, x2.sql] all generate the file 'a.g.cs'. " + Tail),
            ("x2.sql", "SQL files [x1.sql, x2.sql] all generate the file 'a.g.cs'. " + Tail),
            ("y1.sql", "SQL files [y1.sql, y2.sql] all generate the file 'b.g.cs'. " + Tail),
            ("y2.sql", "SQL files [y1.sql, y2.sql] all generate the file 'b.g.cs'. " + Tail),
        }, collisions.ToArray());
    }
}
