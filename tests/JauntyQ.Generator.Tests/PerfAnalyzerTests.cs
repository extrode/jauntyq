using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Tier 4 performance analyzer: JNT8001 cartesian product, JNT8002
/// non-sargable predicate, JNT8003 leading-wildcard LIKE, JNT8004 filter on
/// a column no index covers, JNT8005 duplicate queries. All warnings - the
/// query stays compiled; the developer just learns it will not be fast.
/// </summary>
public class PerfAnalyzerTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""launched_at"": { ""name"": ""launched_at"", ""dbType"": ""datetime"", ""isNullable"": true }
      },
      ""indexes"": [
        { ""name"": ""ix_products_category"", ""columns"": [""category_id""], ""isUnique"": false },
        { ""name"": ""ix_products_composite"", ""columns"": [""launched_at"", ""product_name""], ""isUnique"": false }
      ]
    },
    ""categories"": {
      ""name"": ""categories"",
      ""columns"": {
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""category_name"": { ""name"": ""category_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 30 }
      },
      ""indexes"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("PerfTestAssembly",
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

    private static GeneratorDriverRunResult RunOne(string sql) =>
        Run(("db/Products/TestQuery.sql", sql));

    // ── JNT8001: cartesian product ──────────────────────────────────────

    [Fact]
    public void CrossJoinWithoutCondition_JNT8001()
    {
        var result = RunOne("select p.product_id, c.category_name\nfrom products p\ncross join categories c");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8001");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        // warnings never block: the query still compiled
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Products.TestQuery.g.cs");
    }

    [Fact]
    public void ProperJoin_NoCartesianWarning()
    {
        var result = RunOne("select p.product_id, c.category_name\nfrom products p\njoin categories c on p.category_id = c.category_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8001");
    }

    // ── JNT8002: non-sargable predicate ─────────────────────────────────

    [Fact]
    public void FunctionWrappedColumn_JNT8002()
    {
        var result = RunOne("select product_id\nfrom products\nwhere upper(products.product_name) = @product_name");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8002");
        Assert.Contains("upper(products.product_name)", diag.GetMessage());
        Assert.Contains("parameter side", diag.GetMessage());
    }

    [Fact]
    public void CastOnColumn_JNT8002()
    {
        var result = RunOne("select product_id\nfrom products\nwhere cast(products.launched_at as date) = @launched_at\n-- @params launched_at:System.DateTime");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8002");
    }

    [Fact]
    public void FunctionOnValueSide_NotFlagged()
    {
        var result = RunOne("select product_id\nfrom products\nwhere products.launched_at = coalesce(@launched_at, @fallback)\n-- @params launched_at:System.DateTime\n-- @params fallback:System.DateTime");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8002");
    }

    [Fact]
    public void FunctionInSelectList_NotFlagged()
    {
        var result = RunOne("select product_id, upper(product_name) as loud_name\nfrom products\nwhere products.product_id = @product_id\n-- @result (int ProductId, string LoudName)");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8002");
    }

    // ── JNT8003: leading-wildcard LIKE ──────────────────────────────────

    [Fact]
    public void LeadingWildcard_JNT8003_TrailingWildcard_Fine()
    {
        var leading = RunOne("select product_id\nfrom products\nwhere products.product_name like '%chai'");
        var diag = Assert.Single(leading.Diagnostics, d => d.Id == "JNT8003");
        Assert.Contains("%chai", diag.GetMessage());

        var trailing = RunOne("select product_id\nfrom products\nwhere products.product_name like 'chai%'");
        Assert.DoesNotContain(trailing.Diagnostics, d => d.Id == "JNT8003");
    }

    // ── JNT8004: unindexed filter column ────────────────────────────────

    [Fact]
    public void FilterOnUnindexedColumn_JNT8004()
    {
        var result = RunOne("select product_id\nfrom products\nwhere products.product_name = @product_name");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.Contains("products.product_name", diag.GetMessage());
    }

    [Fact]
    public void FilterOnIndexedOrKeyColumn_NoWarning()
    {
        // primary key
        var pk = RunOne("select product_id\nfrom products\nwhere products.product_id = @product_id");
        Assert.DoesNotContain(pk.Diagnostics, d => d.Id == "JNT8004");

        // leading column of an index
        var indexed = RunOne("select product_id\nfrom products\nwhere products.category_id = @category_id");
        Assert.DoesNotContain(indexed.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void NonLeadingIndexColumn_StillWarns()
    {
        // product_name is the SECOND column of ix_products_composite:
        // a filter on it alone cannot seek that index.
        var result = RunOne("select product_id\nfrom products\nwhere products.product_name = @product_name");

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void CompositeIndex_BothColumnsFiltered_NoWarning()
    {
        // Consumer-gaps-report gap #6: launched_at is the leading column of
        // ix_products_composite; product_name is the second. Filtering on
        // both together is fully covered by the composite index, even
        // though product_name alone (see NonLeadingIndexColumn_StillWarns)
        // is not seekable.
        var result = RunOne(
            "select product_id\nfrom products\n" +
            "where products.launched_at = @launched_at and products.product_name = @product_name\n" +
            "-- @params launched_at:System.DateTime, product_name:string");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void CompositeIndex_OnlyNonLeadingColumnFiltered_StillWarns()
    {
        // Same composite index, but the leading column (launched_at) is NOT
        // also filtered -- product_name alone still cannot seek it.
        var result = RunOne(
            "select product_id\nfrom products\nwhere products.product_name = @product_name\n" +
            "and products.category_id = @category_id");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("product_name"));
    }

    [Fact]
    public void SnapshotWithoutIndexMetadata_JNT8004Suppressed()
    {
        // strip all index arrays: old-snapshot compatibility
        string oldSchema = SchemaJson
            .Replace(@"""indexes"": [
        { ""name"": ""ix_products_category"", ""columns"": [""category_id""], ""isUnique"": false },
        { ""name"": ""ix_products_composite"", ""columns"": [""launched_at"", ""product_name""], ""isUnique"": false }
      ]", @"""indexes"": []");

        var compilation = CSharpCompilation.Create("PerfTestAssembly2",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", oldSchema),
                new InMemoryAdditionalText("db/Products/TestQuery.sql",
                    "select product_id\nfrom products\nwhere products.product_name = @product_name")))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        Assert.DoesNotContain(driver.GetRunResult().Diagnostics, d => d.Id == "JNT8004");
    }

    // ── JNT8004 inside predicate subqueries (EXISTS/IN correlations) ────

    [Fact]
    public void CorrelatedExistsSubquery_UnindexedOuterColumn_JNT8004()
    {
        // The subquery's own WHERE compares its local alias `c.category_id`
        // against the outer statement's alias `p` — a correlation, not a
        // JOIN...ON or a @param binding. category_id IS indexed (leading
        // column of ix_products_category), so only the outer, unindexed side
        // (categories.category_id is the PK — indexed; use category_name,
        // which has no index, to force a genuine miss on the correlation).
        var result = RunOne(
            "select p.product_id\nfrom products p\n" +
            "where exists (\n" +
            "  select 1 from categories c\n" +
            "  where c.category_name = p.product_name\n" +
            ")");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("categories.category_name"));
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("products.product_name"));
    }

    [Fact]
    public void CorrelatedExistsSubquery_IndexedOuterColumn_NoWarning()
    {
        // Same correlation shape, but both sides are covered (category_id is
        // indexed on products, primary key on categories) — no false positive.
        var result = RunOne(
            "select p.product_id\nfrom products p\n" +
            "where exists (\n" +
            "  select 1 from categories c\n" +
            "  where c.category_id = p.category_id\n" +
            ")");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
    }

    [Fact]
    public void ExplicitJoinOnColumn_StillWarnsOnlyOnce_NoDuplicateFromCorrelationScan()
    {
        // ON-clause columns sit before WHERE and are already checked via
        // query.Joins; the new WHERE-scoped correlation scan must not also
        // re-flag them as a duplicate warning.
        var result = RunOne(
            "select p.product_id\nfrom products p\n" +
            "join categories c on c.category_name = p.product_name\n" +
            "where p.product_id = @product_id");

        var matches = result.Diagnostics.Where(d => d.Id == "JNT8004" && d.GetMessage().Contains("categories.category_name")).ToList();
        Assert.Single(matches);
    }

    // ── JNT8005: duplicate queries ──────────────────────────────────────

    [Fact]
    public void IdenticalQueries_DifferentFormattingAndCase_JNT8005()
    {
        var result = Run(
            ("db/Products/GetByCategory.sql", "select product_id, product_name\nfrom products\nwhere products.category_id = @category_id"),
            ("db/Products/FetchForCategory.sql", "SELECT   product_id,\n         product_name\nFROM products\nWHERE PRODUCTS.category_id = @category_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8005");
        Assert.Contains("Products.FetchForCategory", diag.GetMessage());
        Assert.Contains("Products.GetByCategory", diag.GetMessage());
    }

    [Fact]
    public void DifferentQueries_NoDuplicateWarning()
    {
        var result = Run(
            ("db/Products/GetByCategory.sql", "select product_id\nfrom products\nwhere products.category_id = @category_id"),
            ("db/Products/GetById.sql", "-- @first\nselect product_id\nfrom products\nwhere products.product_id = @product_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8005");
    }
}
