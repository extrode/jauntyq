using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Regression coverage for the 2026-07-11 round-2 (empirical/fuzzing) audit:
///   * JNT2008 — two files whose generated names collide case-insensitively
///     must fail with a diagnostic, not crash the whole generator (CS8785).
///   * JNT2004 — a filename that is a C# keyword must be rejected, not emitted
///     as `public class class`.
///   * JNT3009 — two projected columns folding to the same C# property must
///     fail with a diagnostic, not a CS0102 inside generated code.
///   * JNT1005 — pathologically deep parenthesis nesting must be refused fast,
///     not drive the parser into its O(n²) regime.
/// </summary>
public class Round2AuditTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""first_name"": { ""name"": ""first_name"", ""dbType"": ""text"", ""isNullable"": true, ""maxLength"": 50 },
        ""level"": { ""name"": ""level"", ""dbType"": ""integer"", ""isNullable"": false }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(params (string path, string sql)[] files)
    {
        var compilation = CSharpCompilation.Create("Round2TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.path, f.sql)).ToList();
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static ImmutableArray<Diagnostic> Diags(GeneratorDriverRunResult r) => r.Diagnostics;
    private static bool Emitted(GeneratorDriverRunResult r, string hint) =>
        r.Results[0].GeneratedSources.Any(s => s.HintName == hint);

    // ── JNT2008: case-insensitive generated-file collision ────────────────

    [Fact]
    public void CaseOnlyFilenameCollision_ReportsJNT2008_DoesNotCrashGenerator()
    {
        var result = Run(
            ("queries/getUsers.sql", "select id from users where id = @Id;"),
            ("queries/GetUsers.sql", "select id from users;"));

        // The bug was CS8785: AddSource throwing on the case-folded duplicate
        // hint name aborted the entire generator. It must now be a clean JNT2008.
        Assert.DoesNotContain(Diags(result), d => d.Id == "CS8785");
        Assert.Contains(Diags(result), d => d.Id == "JNT2008");
    }

    [Fact]
    public void CaseOnlyFilenameCollision_SuppressesCollidingEmission()
    {
        var result = Run(
            ("queries/getUsers.sql", "select id from users where id = @Id;"),
            ("queries/GetUsers.sql", "select id from users;"));

        // Neither colliding file is emitted (the build fails on JNT2008), so no
        // duplicate hint name ever reaches AddSource.
        int userQueryFiles = result.Results[0].GeneratedSources
            .Count(s => s.HintName.EndsWith("etUsers.g.cs", System.StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, userQueryFiles);
    }

    [Fact]
    public void SameBasenameDifferentFolders_NoCollision_BothEmit()
    {
        var result = Run(
            ("queries/A/GetUsers.sql", "select id from users where id = @Id;"),
            ("queries/B/GetUsers.sql", "select id from users;"));

        Assert.DoesNotContain(Diags(result), d => d.Id == "JNT2008");
        Assert.True(Emitted(result, "A.GetUsers.g.cs"));
        Assert.True(Emitted(result, "B.GetUsers.g.cs"));
    }

    // ── JNT2004: keyword filename ─────────────────────────────────────────

    [Theory]
    [InlineData("queries/class.sql")]
    [InlineData("queries/int.sql")]
    [InlineData("queries/for.sql")]
    public void KeywordFilename_ReportsJNT2004_NoEmit(string path)
    {
        var result = Run((path, "select id from users;"));

        Assert.DoesNotContain(Diags(result), d => d.Id.StartsWith("CS"));
        Assert.Contains(Diags(result), d => d.Id == "JNT2004");
        // Nothing but the always-present shape guard should be emitted.
        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName.Contains("class") || s.HintName.Contains("int") || s.HintName.Contains("for"));
    }

    [Fact]
    public void NonKeywordFilename_StillEmits()
    {
        var result = Run(("queries/GetUser.sql", "select id from users where id = @Id;"));
        Assert.DoesNotContain(Diags(result), d => d.Id == "JNT2004");
        Assert.True(Emitted(result, "Queries.GetUser.g.cs"));
    }

    // ── JNT3009: duplicate result column ──────────────────────────────────

    [Theory]
    [InlineData("select id as X, first_name as X from users;")]         // identical
    [InlineData("select id as x, first_name as X from users;")]         // case-only fold
    [InlineData("select id as my_col, first_name as myCol from users;")] // separator fold
    public void DuplicateResultColumn_ReportsJNT3009_NoEmit(string sql)
    {
        var result = Run(("queries/Dup.sql", sql));

        Assert.DoesNotContain(Diags(result), d => d.Id.StartsWith("CS"));
        Assert.Contains(Diags(result), d => d.Id == "JNT3009");
        Assert.False(Emitted(result, "Queries.Dup.g.cs"));
    }

    [Fact]
    public void DistinctResultColumns_NoJNT3009_Emits()
    {
        var result = Run(("queries/Ok.sql", "select id as A, first_name as B from users;"));

        Assert.DoesNotContain(Diags(result), d => d.Id == "JNT3009");
        Assert.True(Emitted(result, "Queries.Ok.g.cs"));
    }

    [Fact]
    public void DuplicateReturningColumn_ReportsJNT3009()
    {
        // RETURNING path shares the same duplicate-name rule as SELECT.
        var result = Run(("queries/Ins.sql",
            "insert into users (first_name, level) values (@Name, @Lvl) returning id as X, level as X;"));

        Assert.Contains(Diags(result), d => d.Id == "JNT3009");
    }

    // ── JNT1005: nesting depth cap ────────────────────────────────────────

    [Fact]
    public void DeeplyNestedParens_ReportsJNT1005_NoEmit()
    {
        string sql = "select " + new string('(', 5000) + "1" + new string(')', 5000)
                     + " as v -- @type int\nfrom users;";
        var result = Run(("queries/Deep.sql", sql));

        Assert.Contains(Diags(result), d => d.Id == "JNT1005");
        Assert.False(Emitted(result, "Queries.Deep.g.cs"));
    }

    [Fact]
    public void ModestlyNestedParens_NoJNT1005()
    {
        // Well within the cap: must parse normally, never trip JNT1005.
        string sql = "select " + new string('(', 20) + "level" + new string(')', 20)
                     + " as v -- @type int\nfrom users;";
        var result = Run(("queries/Shallow.sql", sql));

        Assert.DoesNotContain(Diags(result), d => d.Id == "JNT1005");
    }
}
