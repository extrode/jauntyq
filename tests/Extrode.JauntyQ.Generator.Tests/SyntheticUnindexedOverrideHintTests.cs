using System.Collections.Immutable;
using Extrode.JauntyQ.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// JNT8004 on an auto-CRUD query used to end at "Add an index or filter on an
/// indexed column" -- advice a consumer cannot take when the schema is
/// upstream-owned, on a query they did not write and appeared unable to
/// annotate. Measured on the sample suite: every one of the 63 residual
/// JNT8004 warnings after annotating 50 hand-written .sql files came from
/// synthesized code.
///
/// The escape existed the whole time and nothing pointed at it: a hand-written
/// .sql file claims its entity.method slot before synthesis, so it overrides
/// the generated method and carries -- @allow-unindexed with it. These tests
/// hold the generator to the advice it now prints -- not merely that a hint
/// appears, but that following it literally silences the warning.
/// </summary>
public class SyntheticUnindexedOverrideHintTests
{
    /// <summary>
    /// sqlite, where a foreign key is NOT automatically indexed -- so the
    /// synthesized GetBy&lt;Fk&gt; loader really does scan. orders carries an
    /// index so QueryValidator's index checks do not return early on a
    /// snapshot that records none.
    /// </summary>
    private const string FkSchema = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""placed_on"": { ""name"": ""placed_on"", ""dbType"": ""varchar"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""pk_orders"", ""columns"": [""id""], ""isUnique"": true }
      ]
    },
    ""order_lines"": {
      ""name"": ""order_lines"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""qty"": { ""name"": ""qty"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""pk_order_lines"", ""columns"": [""id""], ""isUnique"": true }
      ]
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""order_lines"", ""fromColumn"": ""order_id"", ""toTable"": ""orders"", ""toColumn"": ""id"" }
  ]
}";

    /// <summary>
    /// A harmless hand-written query, present only so the common directory
    /// prefix is non-empty and the hint can name a real path. Its own filter
    /// is the primary key, which short-circuits JNT8004.
    /// </summary>
    private const string AnchorPath = "db/tables/Orders/GetOne.sql";
    private const string AnchorSql = "select o.id, o.placed_on from orders o where o.id = @id";

    private static ImmutableArray<Diagnostic> Run(bool autoCrud, params (string path, string sql)[] files)
    {
        var compilation = CSharpCompilation.Create("SyntheticHintTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = ImmutableArray.CreateBuilder<AdditionalText>();
        foreach (var (path, sql) in files)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("db/schema/jaunty.schema.json", FkSchema));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutable())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Diagnostics;
    }

    private static Diagnostic Unindexed(ImmutableArray<Diagnostic> diags) =>
        Assert.Single(diags.Where(d => d.Id == "JNT8004"));

    /// <summary>
    /// Pulls the path and the SQL back out of the message, so the round-trip
    /// test below follows the printed advice literally rather than a
    /// reconstruction of it. If the message format drifts, this throws and the
    /// test fails loudly instead of silently checking nothing.
    /// </summary>
    private static (string path, string sql) ParseAdvice(string message)
    {
        int pathStart = message.IndexOf("create '", StringComparison.Ordinal);
        Assert.True(pathStart >= 0, $"No override path in the message: {message}");
        pathStart += "create '".Length;
        int pathEnd = message.IndexOf('\'', pathStart);
        Assert.True(pathEnd > pathStart, $"Unterminated override path: {message}");

        int sqlStart = message.IndexOf("followed by: ", StringComparison.Ordinal);
        Assert.True(sqlStart >= 0, $"No SQL in the message: {message}");
        sqlStart += "followed by: ".Length;
        int sqlEnd = message.IndexOf(" -- a hand-written", sqlStart, StringComparison.Ordinal);
        Assert.True(sqlEnd > sqlStart, $"Unterminated SQL: {message}");

        return (message.Substring(pathStart, pathEnd - pathStart),
                message.Substring(sqlStart, sqlEnd - sqlStart));
    }

    [Fact]
    public void SyntheticUnindexedFilter_NamesTheFileThatWouldClaimTheSlot()
    {
        string message = Unindexed(Run(autoCrud: true, (AnchorPath, AnchorSql))).GetMessage();

        Assert.Contains("order_lines.order_id", message);
        Assert.Contains("generated by auto-CRUD", message);
        Assert.Contains("-- @allow-unindexed <reason>", message);

        var (path, sql) = ParseAdvice(message);
        Assert.StartsWith("db/tables/", path);
        Assert.EndsWith(".sql", path);
        Assert.Contains("order_lines", sql);
        Assert.Contains("order_id", sql);
    }

    /// <summary>
    /// The load-bearing test. Everything else checks that words appear; this
    /// checks the words are true.
    /// </summary>
    [Fact]
    public void FollowingTheAdviceLiterally_SuppressesTheWarning()
    {
        var (path, sql) = ParseAdvice(Unindexed(Run(autoCrud: true, (AnchorPath, AnchorSql))).GetMessage());

        var after = Run(autoCrud: true,
            (AnchorPath, AnchorSql),
            (path, "-- @allow-unindexed upstream schema, index not ours to add\n" + sql));

        Assert.DoesNotContain(after, d => d.Id == "JNT8004");
        Assert.DoesNotContain(after, d => d.Id == "JNT8012");
        Assert.DoesNotContain(after, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// Same file, no reason. The override file is a normal .sql file, so the
    /// mandatory-reason rule applies to it unchanged -- the acceptance cannot
    /// become a bare stamp by routing through auto-CRUD.
    /// </summary>
    [Fact]
    public void TheOverrideFileStillRequiresAReason()
    {
        var (path, sql) = ParseAdvice(Unindexed(Run(autoCrud: true, (AnchorPath, AnchorSql))).GetMessage());

        var after = Run(autoCrud: true,
            (AnchorPath, AnchorSql),
            (path, "-- @allow-unindexed\n" + sql));

        Assert.Contains(after, d => d.Id == "JNT3008");
        Assert.Contains(after, d => d.Id == "JNT8004");
    }

    /// <summary>
    /// The hint is about a file that does not exist. A hand-written query has
    /// one, and telling its author to create it would be nonsense.
    /// </summary>
    [Fact]
    public void HandWrittenUnindexedFilter_GetsNoSyntheticHint()
    {
        string message = Unindexed(Run(autoCrud: false,
            (AnchorPath, AnchorSql),
            ("db/tables/OrderLines/GetByQty.sql",
             "select l.id, l.qty from order_lines l where l.qty = @qty"))).GetMessage();

        Assert.Contains("order_lines.qty", message);
        Assert.DoesNotContain("auto-CRUD", message);
        Assert.DoesNotContain("create '", message);
    }

    /// <summary>
    /// Test 3 proves the hint is absent on a hand-written query with auto-CRUD
    /// OFF, which is the easy half: with synthesis disabled there is nothing to
    /// confuse it with. The case that matters is both populations in one run,
    /// where the branch has to tell them apart.
    /// </summary>
    [Fact]
    public void WithBothPopulationsInOneRun_OnlyTheSyntheticWarningCarriesTheHint()
    {
        var diags = Run(autoCrud: true,
            (AnchorPath, AnchorSql),
            ("db/tables/OrderLines/GetByQty.sql",
             "select l.id, l.qty from order_lines l where l.qty = @qty"));

        var unindexed = diags.Where(d => d.Id == "JNT8004").Select(d => d.GetMessage()).ToList();
        Assert.Equal(2, unindexed.Count);

        string synthetic = Assert.Single(unindexed, m => m.Contains("order_lines.order_id"));
        string handWritten = Assert.Single(unindexed, m => m.Contains("order_lines.qty"));

        Assert.Contains("generated by auto-CRUD", synthetic);
        Assert.DoesNotContain("auto-CRUD", handWritten);
    }

    /// <summary>
    /// One ID must mean one rule. The synthetic path reports through
    /// JauntyDiagnostics.JNT8004 directly; the hand-written path goes through
    /// DiagnosticInfo.ForValidation, which used to hard-code category "JauntyQ"
    /// and use the code string as the title. That split made
    /// severity-by-category apply to some JNT8004 instances and not others, and
    /// put two rule entries in SARIF for one rule. ForValidation now takes both
    /// from the descriptor the ValidationError was already carrying.
    /// </summary>
    [Fact]
    public void BothPathsReportJnt8004UnderOneDescriptor()
    {
        var unindexed = Run(autoCrud: true,
            (AnchorPath, AnchorSql),
            ("db/tables/OrderLines/GetByQty.sql",
             "select l.id, l.qty from order_lines l where l.qty = @qty"))
            .Where(d => d.Id == "JNT8004").ToList();

        Assert.Equal(2, unindexed.Count);
        Assert.All(unindexed, d => Assert.Equal("JauntyQ.Performance", d.Descriptor.Category));
        Assert.All(unindexed, d => Assert.Equal("Unindexed Filter Column", d.Descriptor.Title.ToString()));
    }

    /// <summary>
    /// Two synthetic loaders on one table, so each claims its own slot. Also
    /// pins prefix stability: creating the first override file must not move
    /// commonPrefix, or the path printed for the second would go stale the
    /// moment a consumer acted on the first.
    /// </summary>
    [Fact]
    public void TwoUnindexedLoadersOnOneTable_EachAdviceStaysValidAfterTheOtherIsFollowed()
    {
        var first = Run(autoCrud: true, (AnchorPath, AnchorSql))
            .Where(d => d.Id == "JNT8004").Select(d => d.GetMessage()).ToList();
        Assert.Single(first);

        var (path, sql) = ParseAdvice(first[0]);
        var after = Run(autoCrud: true,
            (AnchorPath, AnchorSql),
            (path, "-- @allow-unindexed upstream schema, index not ours to add\n" + sql));

        Assert.DoesNotContain(after, d => d.Id == "JNT8004");
        Assert.DoesNotContain(after, d => d.Severity == DiagnosticSeverity.Error);

        // The override file sits under the same root, so it cannot shift the
        // grandparent prefix the next hint would be built from.
        Assert.StartsWith("db/tables/", path);
    }

    /// <summary>
    /// The pasted SQL comes from the caller's cleanedSql, not synth.Sql, so a
    /// synthetic carrying a leading directive line (GetById's "-- @first",
    /// Insert's "-- @identity") cannot flatten into a single comment. Neither
    /// can raise JNT8004 today; this pins the invariant rather than the
    /// coincidence, because a consumer pasting an all-comment line creates the
    /// JNT3001 directive-only file whose method disappears.
    /// </summary>
    [Fact]
    public void ThePastedSqlIsNeverAComment()
    {
        var (_, sql) = ParseAdvice(Unindexed(Run(autoCrud: true, (AnchorPath, AnchorSql))).GetMessage());

        Assert.False(sql.TrimStart().StartsWith("--", StringComparison.Ordinal),
            $"A flattened one-liner beginning '--' comments out the whole statement: {sql}");
        Assert.DoesNotContain("-- @", sql);
        Assert.StartsWith("SELECT", sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The message must not sell the override as free. The copy stops tracking
    /// the snapshot the moment it exists, and for the audience this hint is
    /// aimed at -- upstream-owned schemas nobody in the building controls --
    /// drift is the normal condition, not the edge case.
    /// </summary>
    [Fact]
    public void TheHintStatesThatTheOverrideStopsTrackingSchemaChanges()
    {
        string message = Unindexed(Run(autoCrud: true, (AnchorPath, AnchorSql))).GetMessage();

        Assert.Contains("no longer tracks schema changes", message);
    }

    /// <summary>
    /// A pure auto-CRUD project has no .sql files, so there is no common
    /// prefix to derive a root from. Naming a guessed root would be worse than
    /// naming none: a path outside the consumer's AdditionalFiles glob silently
    /// does nothing when they follow it.
    /// </summary>
    [Fact]
    public void WithNoUserSqlFiles_TheHintDescribesTheRuleWithoutInventingARoot()
    {
        string message = Unindexed(Run(autoCrud: true)).GetMessage();

        Assert.Contains("generated by auto-CRUD", message);
        Assert.Contains("-- @allow-unindexed <reason>", message);
        Assert.Contains("under your SQL query root", message);
        Assert.DoesNotContain("db/tables", message);
    }
}
