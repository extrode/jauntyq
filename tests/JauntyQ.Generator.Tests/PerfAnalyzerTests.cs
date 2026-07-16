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

    private static GeneratorDriverRunResult Run(string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("PerfTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files) =>
        Run(SchemaJson, files);

    private static GeneratorDriverRunResult RunOne(string sql) =>
        Run(("db/Products/TestQuery.sql", sql));

    private static GeneratorDriverRunResult RunOne(string sql, string schemaJson) =>
        Run(schemaJson, ("db/Products/TestQuery.sql", sql));

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

    // ── AUD-R11 (§2.1-r23/24/25): dialect invariance ────────────────────
    // ExtractPerfHints (JNT8002 non-sargable), the leading-wildcard LIKE
    // detector (JNT8003), and the cartesian-join detector (JNT8001) are all
    // purely token-level/structural: none of them ever branches on
    // schema.Dialect anywhere in SqlParser.Part4.cs or the perf analyzer.
    // Every other test in this file runs under the SchemaJson fixture's
    // hardcoded "sqlserver" dialect, so that invariant was never actually
    // exercised live under the other three dialects -- only inferred from
    // reading the source. This proves it holds at runtime, across all four
    // canonical dialects, not just by code trace.
    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    [InlineData("sqlserver")]
    public void CartesianNonSargableAndLeadingWildcard_FireIdenticallyAcrossDialects(string dialect)
    {
        string schema = SchemaJson.Replace(@"""dialect"": ""sqlserver""", $@"""dialect"": ""{dialect}""");

        var cartesian = RunOne(
            "select p.product_id, c.category_name\nfrom products p\ncross join categories c", schema);
        Assert.Single(cartesian.Diagnostics, d => d.Id == "JNT8001");

        var nonSargable = RunOne(
            "select product_id\nfrom products\nwhere upper(products.product_name) = @product_name", schema);
        Assert.Single(nonSargable.Diagnostics, d => d.Id == "JNT8002");

        var leadingWildcard = RunOne(
            "select product_id\nfrom products\nwhere products.product_name like '%chai'", schema);
        Assert.Single(leadingWildcard.Diagnostics, d => d.Id == "JNT8003");
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

    [Fact]
    public void SelfJoin_OtherAliasUnfiltered_StillWarns()
    {
        // AUD-R8-02: a self-join gives two aliases (p1/p2) of the SAME table.
        // filterColumns used to be keyed only by canonical table name, so
        // filtering p1.launched_at (the composite index's leading column)
        // wrongly made p2.product_name look "covered" too, even though p2's
        // own row instance was never filtered on launched_at at all -- no
        // real index can seek p2 that way. Each alias must be judged on its
        // own bound predicates.
        var result = RunOne(
            "select p1.product_id\nfrom products p1\n" +
            "join products p2 on p1.product_id = p2.product_id\n" +
            "where p1.launched_at = @launchedAt and p2.product_name = @productName\n" +
            "-- @params launchedAt:System.DateTime, productName:string");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("products.product_name"));
    }

    [Fact]
    public void SelfJoin_SameAliasBothColumnsFiltered_NoWarning()
    {
        // Contrast case: when the SAME alias supplies both the leading and
        // non-leading composite columns, the index genuinely is usable for
        // that alias's own row instance -- still no warning.
        var result = RunOne(
            "select p1.product_id\nfrom products p1\n" +
            "join products p2 on p1.product_id = p2.product_id\n" +
            "where p1.launched_at = @launchedAt and p1.product_name = @productName\n" +
            "-- @params launchedAt:System.DateTime, productName:string");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004" && d.GetMessage().Contains("product_name"));
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

    // ── JNT8006: join columns form no declared foreign key ──────────────

    // Same products/categories shape as SchemaJson, plus a declared FK
    // products.category_id -> categories.category_id.
    private const string SchemaWithFkJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""category_id"": { ""name"": ""category_id"", ""dbType"": ""int"", ""isNullable"": true }
      },
      ""indexes"": [
        { ""name"": ""ix_products_category"", ""columns"": [""category_id""], ""isUnique"": false }
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
  },
  ""foreignKeys"": [
    { ""fromTable"": ""products"", ""fromColumn"": ""category_id"", ""toTable"": ""categories"", ""toColumn"": ""category_id"" }
  ]
}";

    private static GeneratorDriverRunResult RunFk(string sql)
    {
        var compilation = CSharpCompilation.Create("PerfFkTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaWithFkJson),
                new InMemoryAdditionalText("db/Products/TestQuery.sql", sql)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void JoinOnNonForeignKeyColumns_JNT8006()
    {
        // product_name = category_name: both columns exist, but the declared FK
        // is category_id -> category_id, so this join is not a real FK.
        var result = RunFk("select p.product_id, c.category_name\nfrom products p\njoin categories c on p.product_name = c.category_name");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8006");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("products.product_name", diag.GetMessage());
        Assert.Contains("categories.category_name", diag.GetMessage());
        // warnings never block: the query still compiled
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Products.TestQuery.g.cs");
    }

    [Fact]
    public void JoinOnDeclaredForeignKey_NoWarning()
    {
        var result = RunFk("select p.product_id, c.category_name\nfrom products p\njoin categories c on p.category_id = c.category_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8006");
    }

    [Fact]
    public void JoinOnForeignKeyReversedDirection_NoWarning()
    {
        // ON written with the FK's target table first; the match is direction-agnostic.
        var result = RunFk("select p.product_id, c.category_name\nfrom products p\njoin categories c on c.category_id = p.category_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8006");
    }

    [Fact]
    public void JoinToCteSide_Unresolvable_NoWarning()
    {
        // The join's right side resolves to a CTE (`cat`), not a snapshot table,
        // so the FK graph cannot judge it — never guessed, never warned.
        var result = RunFk(
            "with cat as (select category_id, category_name from categories)\n" +
            "select p.product_id, cat.category_name\n" +
            "from products p\n" +
            "join cat on p.category_id = cat.category_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8006");
    }

    [Fact]
    public void SnapshotWithoutForeignKeyMetadata_JNT8006Suppressed()
    {
        // SchemaJson carries no foreignKeys: the check cannot fire even on a
        // clearly-non-FK join.
        var result = RunOne("select p.product_id, c.category_name\nfrom products p\njoin categories c on p.product_name = c.category_name");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8006");
    }

    // ── JNT8007: ORDER BY column has no supporting index ─────────────────

    [Fact]
    public void OrderByUnindexedColumn_JNT8007()
    {
        // product_name is the SECOND column of ix_products_composite; with the
        // leading column (launched_at) not constrained, an index can't order it.
        var result = RunOne("select product_id\nfrom products\norder by product_name");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8007");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("products.product_name", diag.GetMessage());
        // warnings never block: the query still compiled
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Products.TestQuery.g.cs");
    }

    [Fact]
    public void OrderByLeadingIndexColumn_NoWarning()
    {
        // category_id is the leading (only) column of ix_products_category.
        var result = RunOne("select product_id\nfrom products\norder by category_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void OrderByCompositeNonLeading_LeadingFiltered_NoWarning()
    {
        // launched_at (leading of the composite) is constrained by an equality
        // filter, so ordering by product_name (its second column) is covered.
        var result = RunOne(
            "select product_id\nfrom products\n" +
            "where products.launched_at = @launched_at\norder by product_name\n" +
            "-- @params launched_at:System.DateTime");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void SelfJoin_OrderByOtherAliasUnfiltered_StillWarns()
    {
        // AUD-R8-02 sibling (JNT8007 shares IsColumnIndexSupported with
        // JNT8004): filtering p1.launched_at must not excuse ordering by
        // p2.product_name -- p2's own row instance was never constrained on
        // the composite index's leading column.
        var result = RunOne(
            "select p1.product_id\nfrom products p1\n" +
            "join products p2 on p1.product_id = p2.product_id\n" +
            "where p1.launched_at = @launchedAt\norder by p2.product_name\n" +
            "-- @params launchedAt:System.DateTime");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8007" && d.GetMessage().Contains("products.product_name"));
    }

    [Fact]
    public void OrderByPrimaryKey_NoWarning()
    {
        var result = RunOne("select product_id\nfrom products\norder by product_id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void OrderByExpressionOrdinalOrAlias_NoWarning()
    {
        // Expression: not a plain base column.
        var expr = RunOne("select product_id\nfrom products\norder by lower(product_name)");
        Assert.DoesNotContain(expr.Diagnostics, d => d.Id == "JNT8007");

        // Ordinal: positional reference.
        var ordinal = RunOne("select product_id, product_name\nfrom products\norder by 2");
        Assert.DoesNotContain(ordinal.Diagnostics, d => d.Id == "JNT8007");

        // Projected alias: names a SELECT-list alias, not a base column.
        var alias = RunOne("select count(*) as cnt\nfrom products\norder by cnt");
        Assert.DoesNotContain(alias.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void SnapshotWithoutIndexMetadata_JNT8007Suppressed()
    {
        string oldSchema = SchemaJson
            .Replace(@"""indexes"": [
        { ""name"": ""ix_products_category"", ""columns"": [""category_id""], ""isUnique"": false },
        { ""name"": ""ix_products_composite"", ""columns"": [""launched_at"", ""product_name""], ""isUnique"": false }
      ]", @"""indexes"": []");

        var compilation = CSharpCompilation.Create("PerfTestAssembly3",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", oldSchema),
                new InMemoryAdditionalText("db/Products/TestQuery.sql",
                    "select product_id\nfrom products\norder by product_name")))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        Assert.DoesNotContain(driver.GetRunResult().Diagnostics, d => d.Id == "JNT8007");
    }
}
