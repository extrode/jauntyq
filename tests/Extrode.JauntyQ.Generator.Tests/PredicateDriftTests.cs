using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// JNT8011 predicate drift and JNT3010 un-comparable pairing, both driven by
/// <c>-- @mirrors</c>. A count query that filters differently from the list it
/// sizes returns a total the page contents contradict; the pairing cannot be
/// inferred from filenames, so it is declared, and a declared pairing that
/// cannot be compared is reported rather than dropped.
/// </summary>
public class PredicateDriftTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""bookmarks"": {
      ""name"": ""bookmarks"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""bigint"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""bigint"", ""isNullable"": false },
        ""title"": { ""name"": ""title"", ""dbType"": ""text"", ""isNullable"": false },
        ""views"": { ""name"": ""views"", ""dbType"": ""integer"", ""isNullable"": false },
        ""status"": { ""name"": ""status"", ""dbType"": ""integer"", ""isNullable"": false },
        ""deleted_at"": { ""name"": ""deleted_at"", ""dbType"": ""timestamp"", ""isNullable"": true },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ix_bookmarks_user"", ""columns"": [""user_id""], ""isUnique"": false }
      ]
    },
    ""bookmark_tags"": {
      ""name"": ""bookmark_tags"",
      ""columns"": {
        ""bookmark_id"": { ""name"": ""bookmark_id"", ""dbType"": ""bigint"", ""isNullable"": false },
        ""tag_name"": { ""name"": ""tag_name"", ""dbType"": ""text"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ix_bookmark_tags_bookmark"", ""columns"": [""bookmark_id""], ""isUnique"": false }
      ]
    }
  },
  ""foreignKeys"": []
}";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("PredicateDriftTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // ── Drift detected (JNT8011) ────────────────────────────────────────

    [Fact]
    public void CountMissingSoftDeleteFilter_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id, bookmarks.title\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.deleted_at is null\n" +
                "order by bookmarks.id\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);

        string message = diag.GetMessage();
        Assert.Contains("Bookmarks.CountForUser", message);
        Assert.Contains("ListPage", message);
        // The soft-delete predicate is the whole finding, and it is named on
        // the side that HAS it -- the count is the side that lacks it.
        Assert.Contains("only in ListPage", message);
        Assert.Contains("bookmarks.deleted_at IS NULL", message);
        Assert.DoesNotContain("only in CountForUser", message);

        // A warning never blocks: both queries still compiled.
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Bookmarks.ListPage.g.cs");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Bookmarks.CountForUser.g.cs");
    }

    [Fact]
    public void CountWithExtraPredicate_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id, bookmarks.title\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.status = 1\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
        string message = diag.GetMessage();
        Assert.Contains("only in CountForUser", message);
        Assert.Contains("bookmarks.status = 1", message);
        Assert.DoesNotContain("only in ListPage", message);
    }

    [Fact]
    public void DifferingLiteral_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.status = 2\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
        string message = diag.GetMessage();
        Assert.Contains("bookmarks.status = 2", message);
        Assert.Contains("bookmarks.status = 1", message);
    }

    [Fact]
    public void DifferingOperator_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.views >= @min\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.views > @min\n"));

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
    }

    [Fact]
    public void ExistsVersusNotExists_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\n" +
                "from bookmarks\n" +
                "where exists (select 1 from bookmark_tags where bookmark_tags.bookmark_id = bookmarks.id)\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "from bookmarks\n" +
                "where not exists (select 1 from bookmark_tags where bookmark_tags.bookmark_id = bookmarks.id)\n"));

        // The NOT is only visible because atoms are taken BEFORE the subquery
        // lift, which strips it along with the span it removes.
        Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
    }

    [Fact]
    public void ListPaginatesInCte_FlatCount_DriftDetected_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", CtePagedList(cteWhere:
                "where bookmarks.user_id = @userId and bookmarks.deleted_at is null")),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        // The list's WHERE lives inside the `page` CTE and the count is flat:
        // comparing top-level WHERE clauses only would be silent here, which is
        // the exact shape this diagnostic exists for.
        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
        Assert.Contains("bookmarks.deleted_at IS NULL", diag.GetMessage());
    }

    // ── Silent ──────────────────────────────────────────────────────────

    [Fact]
    public void DifferentQualificationAndOrder_Silent()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select b.id\n" +
                "from bookmarks b\n" +
                "where b.user_id = @userId and b.deleted_at is null\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "from bookmarks\n" +
                "where deleted_at is null and user_id = @userId\n"));

        // Same two predicates: aliased vs bare, and in the opposite order.
        // Columns canonicalize to table.column and atoms compare as a set.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");
    }

    [Fact]
    public void CteListMatchingCount_Silent()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql", CtePagedList(cteWhere:
                "where bookmarks.user_id = @userId and bookmarks.deleted_at is null")),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId and bookmarks.deleted_at is null\n"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");
    }

    [Fact]
    public void NoDirective_Silent()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.user_id = @userId and bookmarks.deleted_at is null\n"),
            ("db/Bookmarks/CountForUser.sql",
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        // Drifted, but nobody declared the pairing. Opt-in means opt-in.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");
    }

    [Fact]
    public void TopLevelOrWrittenInDifferentCase_Silent()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\n" +
                "from bookmarks\n" +
                "where bookmarks.user_id = @userId or bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\n" +
                "FROM bookmarks\n" +
                "WHERE bookmarks.USER_ID = @userId OR bookmarks.Status = 1\n"));

        // A top-level OR collapses the whole region to ONE atom rather than
        // being split into conjuncts that mean nothing. Written with different
        // casing throughout, it still matches.
        //
        // Case-insensitivity here is delivered by ResolveColumn returning the
        // schema's canonical column name and by the tokenizer upper-casing
        // keywords -- neither of which is a line of this change, which is why
        // no perturbation of the atom code reddens this test. It is a guard
        // against a future change that stops resolving, not a proof of one.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");
    }

    [Fact]
    public void DriftInsideATopLevelOr_JNT8011()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.user_id = @userId or bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId or bookmarks.status = 2\n"));

        // Collapsing to one atom still detects drift inside the disjunction.
        Assert.Single(result.Diagnostics, d => d.Id == "JNT8011");
    }

    // ── Declared but not comparable (JNT3010) ───────────────────────────

    [Fact]
    public void UnknownTarget_JNT3010()
    {
        var result = Run(
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPageTypo\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3010");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("no query by that name was compiled", diag.GetMessage());
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011");
    }

    [Fact]
    public void AmbiguousBareMethod_JNT3010()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"),
            ("db/Public/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3010");
        string message = diag.GetMessage();
        Assert.Contains("ambiguous", message);
        Assert.Contains("Bookmarks.ListPage", message);
        Assert.Contains("Public.ListPage", message);
        // Guessing which was meant is the silent wrong answer this prevents.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011");
    }

    [Fact]
    public void QualifiedTargetDisambiguates_Silent()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"),
            ("db/Public/ListPage.sql",
                "select bookmarks.id\nfrom bookmarks\nwhere bookmarks.status = 1\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors Bookmarks.ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011" || d.Id == "JNT3010");
    }

    [Fact]
    public void SelfReference_JNT3010()
    {
        var result = Run(
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors CountForUser\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3010");
        Assert.Contains("this same query", diag.GetMessage());
    }

    [Fact]
    public void TargetProducedNoModel_JNT3010()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "select nosuchtable.id\nfrom nosuchtable\nwhere nosuchtable.user_id = @userId\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3010");
        Assert.Contains("produced no query model", diag.GetMessage());
    }

    [Fact]
    public void UnresolvableWhereColumn_JNT3010()
    {
        var result = Run(
            ("db/Bookmarks/ListPage.sql",
                "-- @params userId:long, since:DateTime\n" +
                "with page as (\n" +
                "    select bookmarks.id, bookmarks.created_at\n" +
                "    from bookmarks\n" +
                "    where bookmarks.user_id = @userId\n" +
                ")\n" +
                "select page.id\nfrom page\nwhere page.created_at > @since\n"),
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        // `page.created_at` is a CTE virtual column: it validates, but it does
        // not resolve to a schema column, so the atom cannot be canonicalized.
        // Falling back to raw text would manufacture a drift claim out of a
        // spelling difference, so the pairing is declared uncomparable instead.
        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3010");
        string message = diag.GetMessage();
        Assert.Contains("does not resolve to a schema column", message);
        Assert.Contains("Bookmarks.ListPage references", message);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8011");
    }

    // ── Directive hygiene (JNT3008) ─────────────────────────────────────

    [Fact]
    public void BareMirrors_JNT3008()
    {
        var result = Run(
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrors\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains("-- @mirrors requires a value", diag.GetMessage());
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3010");
    }

    [Fact]
    public void MisspelledMirrors_JNT3008()
    {
        var result = Run(
            ("db/Bookmarks/CountForUser.sql",
                "-- @mirrrors ListPage\n" +
                "select count(*) as total\nfrom bookmarks\nwhere bookmarks.user_id = @userId\n"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains("did you mean '-- @mirrors'", diag.GetMessage());
    }

    /// <summary>
    /// The motivating shape: pagination inside a `page` CTE, so the filter the
    /// count must match lives in the CTE body rather than at the top level.
    /// The ORDER BY carries the primary key deliberately, so JNT8009 stays out
    /// of these tests' way.
    /// </summary>
    private static string CtePagedList(string cteWhere) =>
        "-- @params userId:long\n" +
        "with page as (\n" +
        "    select bookmarks.id, bookmarks.title, bookmarks.created_at\n" +
        "    from bookmarks\n" +
        "    " + cteWhere + "\n" +
        "    order by bookmarks.created_at desc, bookmarks.id desc\n" +
        "    limit 20 offset 40\n" +
        ")\n" +
        "select page.id, page.title, page.created_at\n" +
        "from page\n";
}
