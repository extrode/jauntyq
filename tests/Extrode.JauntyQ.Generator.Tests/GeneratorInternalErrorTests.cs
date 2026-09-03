using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// JNT0001, and the failure mode it exists to end.
///
/// Roslyn catches an exception escaping a source generator and reports CS8785 — a
/// <b>warning</b>. The build carries on, every type the generator would have emitted is
/// absent, and what the consumer sees is a screen of CS0246s with JauntyQ named in none
/// of them. a consumer hit that shape twice and could not tell from their end whether the
/// generator had run at all
/// .
///
/// Two separate properties are asserted here and they are not the same claim:
/// <list type="number">
/// <item>a throw is <b>reported</b>, at Error, naming JauntyQ and the stage; and</item>
/// <item>a per-file throw is <b>contained</b> — the other .sql files still generate,
/// so one bad file costs one file rather than the whole surface.</item>
/// </list>
/// </summary>
[Trait("Category", "AuditRegression")]
public class GeneratorInternalErrorTests
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

    /// <summary>
    /// An AdditionalText whose text cannot be read. Not a contrived shape: an IO error,
    /// a file deleted between enumeration and read, or a decoding failure all reach the
    /// generator exactly like this, and all three are transient — which is what makes
    /// them candidates for a failure that appears on one build and not the next.
    /// </summary>
    private sealed class ThrowingAdditionalText : AdditionalText
    {
        public ThrowingAdditionalText(string path) => Path = path;

        public override string Path { get; }

        public override SourceText? GetText(CancellationToken cancellationToken = default)
            => throw new IOException("the test made this file unreadable");
    }

    private static GeneratorDriverRunResult Run(params AdditionalText[] additionalFiles)
    {
        var compilation = CSharpCompilation.Create("InternalErrorTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(additionalFiles.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static GeneratorDriverRunResult RunWithOneUnreadableFile() => Run(
        new InMemoryAdditionalText("db/schema/jaunty.schema.json", SchemaJson),
        new InMemoryAdditionalText("db/Widgets/GetAll.sql", "select id, name from widgets"),
        new ThrowingAdditionalText("db/Widgets/Unreadable.sql"));

    [Fact]
    public void AnUnreadableSqlFile_ReportsJNT0001_AtError()
    {
        var result = RunWithOneUnreadableFile();

        Diagnostic reported = Assert.Single(result.Diagnostics, d => d.Id == "JNT0001");
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Equal("JauntyQ.Internal", reported.Descriptor.Category);
    }

    [Fact]
    public void TheMessageNamesTheFile_TheException_AndSaysTheFaultIsJauntyQs()
    {
        string message = Assert.Single(
            RunWithOneUnreadableFile().Diagnostics, d => d.Id == "JNT0001").GetMessage();

        Assert.Contains("db/Widgets/Unreadable.sql", message);
        Assert.Contains("System.IO.IOException", message);
        Assert.Contains("the test made this file unreadable", message);

        // The sentence that stops the consumer auditing their own SQL, and the one that
        // stops them investigating the CS0246 storm as separate faults. Both are the
        // reason the code exists rather than decoration.
        Assert.Contains("bug in JauntyQ", message);
        Assert.Contains("CS0246", message);
    }

    [Fact]
    public void TheOtherSqlFilesStillGenerate_SoOneBadFileCostsOneFile()
    {
        var result = RunWithOneUnreadableFile();

        string all = string.Concat(result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));

        Assert.Contains("GetAll", all);

        // The aggregate types are the ones a consumer lost wholesale, so assert them
        // by name rather than trusting that "something was emitted" covers it.
        Assert.Contains("JauntyDb", all);
    }

    [Fact]
    public void TheShapeGuardIsStillEmitted_WhichIsHowAConsumerKnowsTheGeneratorRan()
    {
        var result = RunWithOneUnreadableFile();

        Assert.Contains(result.Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName == "JauntyQShapeGuard.g.cs");
    }

    [Fact]
    public void AHealthyRun_ReportsNoJNT0001()
    {
        // The vacuity guard for the four above: if JNT0001 fired on every run they
        // would all pass while asserting nothing about the throw.
        var result = Run(
            new InMemoryAdditionalText("db/schema/jaunty.schema.json", SchemaJson),
            new InMemoryAdditionalText("db/Widgets/GetAll.sql", "select id, name from widgets"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
    }

    // ── The structural half ──────────────────────────────────────────────
    //
    // JNT0001's false-negative risk is by construction and is recorded as such in
    // the audit criteria Appendix B: it fires only from stages that were wrapped,
    // so a RegisterSourceOutput added later is silent again by default, and nothing
    // about that addition fails. This is the test that fails.

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Output nodes that need no guard, each with the reason it cannot throw. Both
    /// only join strings from an array that was already materialized upstream.
    /// </summary>
    private static readonly string[] Unguarded = { "schemaCandidates", "acceptCandidates" };

    [Fact]
    public void EveryOutputNodeIsGuarded_OrIsOnTheAllowlistWithAReason()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Extrode.JauntyQ.Generator", "JauntyQGenerator.cs"));

        const string call = "context.RegisterSourceOutput(";
        var starts = new System.Collections.Generic.List<int>();
        for (int i = source.IndexOf(call, StringComparison.Ordinal); i >= 0;
             i = source.IndexOf(call, i + 1, StringComparison.Ordinal))
            starts.Add(i);

        Assert.True(starts.Count >= 6,
            $"Only {starts.Count} RegisterSourceOutput calls were found in JauntyQGenerator.cs. "
            + "The scan has broken, so this test would pass by checking almost nothing.");

        var unguarded = new System.Collections.Generic.List<string>();
        for (int i = 0; i < starts.Count; i++)
        {
            int from = starts[i] + call.Length;
            int to = i + 1 < starts.Count ? starts[i + 1] : source.Length;
            string body = source.Substring(from, to - from);

            string node = Regex.Match(body, @"^[A-Za-z0-9_]+").Value;
            if (Array.IndexOf(Unguarded, node) >= 0)
                continue;

            if (!body.Contains("ReportInternalError") && !body.Contains("JNT0001"))
                unguarded.Add(node.Length == 0 ? $"(at offset {starts[i]})" : node);
        }

        Assert.True(unguarded.Count == 0,
            "These output nodes have no route to JNT0001: " + string.Join(", ", unguarded)
            + ". A throw out of one of them is caught by Roslyn, reported as a CS8785 "
            + "warning, and erases generated code silently. Wrap the body in try/catch and "
            + "call ReportInternalError, or add the node to Unguarded above with the reason "
            + "it cannot throw.");
    }
}
