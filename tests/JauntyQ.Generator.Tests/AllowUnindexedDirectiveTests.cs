using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 T5: <c>-- @allow-unindexed &lt;reason&gt;</c> accepts a query's
/// unindexed filters deliberately, so JNT8004 can be satisfied without running
/// a migration against a shared database.
///
/// The three properties that make it an accepted-scan marker rather than a
/// silencer are all pinned here: it suppresses ONLY JNT8004, the reason is
/// mandatory, and a directive that suppresses nothing is reported (JNT8012).
/// </summary>
public class AllowUnindexedDirectiveTests
{
    // messages.status has no index; messages.user_id does. Both on one table so
    // a query can be built that is partly covered and partly not.
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""messages"": {
      ""name"": ""messages"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""status"": { ""name"": ""status"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 20 },
        ""subject"": { ""name"": ""subject"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""ix_messages_user"", ""columns"": [""user_id""], ""isUnique"": false }
      ]
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql)
    {
        var compilation = CSharpCompilation.Create("AllowUnindexedTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Messages/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private const string UnindexedQuery =
        "select messages.id, messages.subject\n" +
        "from messages\n" +
        "where messages.status = @status";

    // ── The directive parses at all ────────────────────────────────────────

    [Fact]
    public void Directive_WithHyphenInTheName_IsParsed()
    {
        // The name scanner took letters only, so this used to read as "@allow"
        // with the value "-unindexed ...", match nothing, and be dropped.
        var (directives, _) = DirectiveParser.Parse(
            "-- @allow-unindexed status index lands in migration 0042\nselect 1");

        Assert.Equal("status index lands in migration 0042", directives.AllowUnindexedReason);
    }

    [Fact]
    public void Directive_LineIsStrippedFromTheCleanedSql()
    {
        var (_, cleaned) = DirectiveParser.Parse(
            "-- @allow-unindexed because reasons\nselect 1");

        Assert.DoesNotContain("@allow-unindexed", cleaned);
    }

    [Fact]
    public void WideningTheNameClass_DoesNotTurnAHyphenatedCommentIntoADirective()
    {
        // "@first-thing" matched nothing before the widening and must match
        // nothing after it, or the change would have moved existing comments
        // into directive position.
        var (directives, _) = DirectiveParser.Parse("-- @first-thing\nselect 1");

        Assert.False(directives.IsFirst);
    }

    // ── R9/R11: suppresses JNT8004, and only JNT8004 ───────────────────────

    [Fact]
    public void WithoutTheDirective_UnindexedFilter_RaisesJNT8004()
    {
        var result = Run(UnindexedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void WithTheDirective_JNT8004_IsSuppressed()
    {
        var result = Run("-- @allow-unindexed status index ships in migration 0042\n" + UnindexedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void WithTheDirective_OtherDiagnosticsStillFire()
    {
        // SELECT * is JNT3002 and has nothing to do with indexing. If the
        // directive silenced this, it would be a NoWarn with better marketing.
        var result = Run(
            "-- @allow-unindexed status index ships in migration 0042\n" +
            "select *\n" +
            "from messages\n" +
            "where messages.status = @status");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3002");
    }

    // ── R10: the reason is mandatory ───────────────────────────────────────

    [Fact]
    public void BareDirective_SetsNoReason()
    {
        var (directives, _) = DirectiveParser.Parse("-- @allow-unindexed\nselect 1");

        Assert.Null(directives.AllowUnindexedReason);
    }

    [Fact]
    public void BareDirective_IsReportedAsJNT3008_AndSuppressesNothing()
    {
        var result = Run("-- @allow-unindexed\n" + UnindexedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004");
    }

    // ── R12: a suppression that suppresses nothing is reported ─────────────

    [Fact]
    public void DirectiveOnAFullyIndexedQuery_IsReportedAsJNT8012()
    {
        var result = Run(
            "-- @allow-unindexed left over from before the index landed\n" +
            "select messages.id, messages.subject\n" +
            "from messages\n" +
            "where messages.user_id = @userId");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8012");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        // The stated reason is echoed so the author can judge whether it still holds.
        Assert.Contains("left over from before the index landed", diag.GetMessage());
    }

    [Fact]
    public void DirectiveThatActuallySuppresses_IsNotReportedAsUnnecessary()
    {
        var result = Run("-- @allow-unindexed status index ships in migration 0042\n" + UnindexedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    [Fact]
    public void NoDirectiveAtAll_NeverReportsJNT8012()
    {
        var result = Run(UnindexedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    /// <summary>
    /// The near-miss the FP boundary names: a query filtering on one covered
    /// and one uncovered column. One real suppression is enough to make the
    /// directive live, and firing JNT8012 here would punish correct use.
    /// </summary>
    [Fact]
    public void PartiallyIndexedQuery_SuppressesAndIsNotReportedAsUnnecessary()
    {
        var result = Run(
            "-- @allow-unindexed status index ships in migration 0042\n" +
            "select messages.id, messages.subject\n" +
            "from messages\n" +
            "where messages.user_id = @userId and messages.status = @status");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }
}
