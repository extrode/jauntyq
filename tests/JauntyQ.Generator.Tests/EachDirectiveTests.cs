using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Generator-level coverage for the -- @each directive: it wraps the
/// parameter as IReadOnlyList&lt;T&gt;, emits an empty-list guard before the
/// connection opens, expands the SQL text into an IN-list at runtime, and
/// binds one parameter per list element.
/// </summary>
[Trait("Category", "AuditRegression")]
public class EachDirectiveTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""text"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private const string PostgresSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""products"": {
      ""name"": ""products"",
      ""columns"": {
        ""product_id"": { ""name"": ""product_id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""product_name"": { ""name"": ""product_name"", ""dbType"": ""character varying"", ""isNullable"": false, ""maxLength"": 40, ""isUnicode"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string schemaJson = SchemaJson, string path = "db/Products/EachQuery.sql")
    {
        var compilation = CSharpCompilation.Create("EachTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText(path, sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    private static string QuerySource(GeneratorDriverRunResult result) =>
        result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.EachQuery.g.cs")
            .SourceText.ToString();

    private const string EachSql = "-- @each Ids\nselect product_id, product_name from products where product_id in (@Ids)";

    [Fact]
    public void Each_WrapsParamAsIReadOnlyList()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        Assert.Contains("System.Collections.Generic.IReadOnlyList<int> Ids", src);
    }

    [Fact]
    public void Each_EmitsEmptyListGuard_PlainList()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        Assert.Contains("if (Ids.Count == 0)", src);
        // The query selects every schema column of products, so it resolves to
        // the canonical full-row POCO (Product) instead of a query-specific
        // Result type.
        Assert.Contains("return new List<Product>();", src);
    }

    [Fact]
    public void Each_EmptyListGuard_ComesBeforeConnectionOpen()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        int guardIdx = src.IndexOf("if (Ids.Count == 0)");
        int openIdx = src.IndexOf("weOpened");
        Assert.True(guardIdx >= 0 && openIdx >= 0);
        Assert.True(guardIdx < openIdx, "the empty-list guard must run before the connection is opened");
    }

    [Fact]
    public void Each_WithFirst_EmptyGuardReturnsNull()
    {
        string sql = "-- @each Ids\n-- @first\nselect product_id, product_name from products where product_id in (@Ids)";
        var result = Run(sql);
        string src = QuerySource(result);

        Assert.Contains("if (Ids.Count == 0)", src);
        Assert.Contains("return null;", src);
    }

    [Fact]
    public void Each_WithStream_EmptyGuardYieldBreaks()
    {
        string sql = "-- @each Ids\n-- @stream\nselect product_id, product_name from products where product_id in (@Ids)";
        var result = Run(sql);
        string src = QuerySource(result);

        Assert.Contains("if (Ids.Count == 0)", src);
        Assert.Contains("yield break;", src);
    }

    [Fact]
    public void Each_ExpandsCommandTextAtRuntime()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        Assert.Contains("var __each_Ids = new StringBuilder();", src);
        Assert.Contains("for (int __i_Ids = 0; __i_Ids < Ids.Count; __i_Ids++)", src);
        Assert.Contains("__each_Ids.Append(\"@Ids\").Append(__i_Ids);", src);
        Assert.Contains("cmd.CommandText = ", src);
        Assert.Contains("__each_Ids.ToString()", src);
        // the fast-path single-verbatim-string form (no concatenation) must
        // not be used when an @each param is present
        Assert.DoesNotContain("cmd.CommandText = @\"select product_id, product_name from products where product_id in (@Ids)\";", src);
    }

    [Fact]
    public void Each_MultipleOccurrencesOfSameParam_BothExpanded()
    {
        string sql = "-- @each Ids\nselect product_id, product_name from products where product_id in (@Ids) or product_id in (@Ids)";
        var result = Run(sql);
        string src = QuerySource(result);

        // 4 method variants (instance/static x sync/async) each emit their own
        // CommandText, so both occurrences of "@Ids" show up in each of the 4.
        int occurrences = src.Split("__each_Ids.ToString()").Length - 1;
        Assert.Equal(8, occurrences);
        // only one expansion StringBuilder/loop should be built per method body
        // even though the token appears twice in the SQL text
        Assert.Equal(4, src.Split("var __each_Ids = new StringBuilder();").Length - 1);
    }

    [Fact]
    public void Each_DoesNotMatchLongerParamNameSharingPrefix()
    {
        // @IdsFoo must never be treated as a reference to the @each param "Ids".
        string sql = "-- @each Ids\n-- @params IdsFoo:int\nselect product_id from products where product_id in (@Ids) and product_id = @IdsFoo";
        var result = Run(sql);
        string src = QuerySource(result);

        Assert.Contains("@IdsFoo", src); // literal SQL segment preserved verbatim
        Assert.Contains("__each_Ids.ToString()", src);
    }

    [Fact]
    public void NonEach_StillUsesSingleVerbatimCommandText()
    {
        string sql = "select product_id, product_name from products where product_id = @ProductId";
        var result = Run(sql, path: "db/Products/PlainQuery.sql");
        string src = result.Results[0].GeneratedSources
            .Single(s => s.HintName == "Products.PlainQuery.g.cs").SourceText.ToString();

        Assert.Contains("cmd.CommandText = @\"", src);
        Assert.DoesNotContain("StringBuilder", src);
    }

    [Fact]
    public void Each_Sqlite_BindsOneParameterPerElement_GenericAdoPath()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        // AUD-R69-01: "__p0"/"__cmd", not "p0"/"cmd".
        Assert.Contains("for (int __ib_Ids = 0; __ib_Ids < Ids.Count; __ib_Ids++)", src);
        Assert.Contains("var __p0_element = Ids[__ib_Ids];", src);
        Assert.Contains("DbParameter __p0 = __cmd.CreateParameter();", src);
        Assert.Contains("__p0.ParameterName = \"@Ids\" + __ib_Ids;", src);
        Assert.Contains("__p0.Value = __p0_element;", src);
        Assert.Contains("__cmd.Parameters.Add(__p0);", src);
    }

    [Fact]
    public void Each_Postgres_BindsTypedParameterPerElement()
    {
        var result = Run(EachSql, PostgresSchemaJson);
        string src = QuerySource(result);

        Assert.Contains("for (int __ib_Ids = 0; __ib_Ids < Ids.Count; __ib_Ids++)", src);
        Assert.Contains("new NpgsqlParameter<int> { ParameterName = \"@Ids\" + __ib_Ids, TypedValue = Ids[__ib_Ids] }", src);
    }

    [Fact]
    public void Each_Sqlite_StringElement_SizesEachParameterDynamically()
    {
        // product_name is varchar(40): each bound element must size to
        // max(its own length, 40), same defense a plain (non-each) string
        // comparison parameter gets — an oversize element must never clip
        // down to 40 chars and falsely match a shorter stored value.
        string sql = "-- @each Names\nselect product_id, product_name from products where product_name in (@Names)";
        var result = Run(sql);
        string src = QuerySource(result);

        Assert.Contains(
            "__p0.Size = __p0_element.Length > 40 ? __p0_element.Length : 40;",
            src);
    }

    [Fact]
    public void GeneratedEachCode_ParsesClean()
    {
        var result = Run(EachSql);
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void GeneratedEachCode_Postgres_ParsesClean()
    {
        var result = Run(EachSql, PostgresSchemaJson);
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void Each_ParamNameInsideStringLiteral_IsNotExpanded()
    {
        // 'ops@Ids' is literal text, not a parameter reference: the runtime
        // expansion must rewrite only the real IN (@Ids) placeholder.
        string sql = "-- @each Ids\nselect product_id, product_name from products " +
            "where product_id in (@Ids) and product_name != 'ops@Ids'";
        var result = Run(sql);
        string src = QuerySource(result);

        // One expansion site per method variant (4 variants), never two.
        Assert.Equal(4, src.Split("__each_Ids.ToString()").Length - 1);
        // The literal survives verbatim inside the emitted verbatim string.
        Assert.Contains("'ops@Ids'", src);
    }

    [Fact]
    public void Each_ParamNameInsideComment_IsNotExpanded()
    {
        string sql = "-- @each Ids\nselect product_id, product_name from products " +
            "where product_id in (@Ids) /* uses @Ids */";
        var result = Run(sql);
        string src = QuerySource(result);

        Assert.Equal(4, src.Split("__each_Ids.ToString()").Length - 1);
        Assert.Contains("/* uses @Ids */", src);
    }

    [Fact]
    public void Each_OnDelete_FailsJNT3003_InsteadOfSilentlyIgnoring()
    {
        string sql = "-- @each Ids\ndelete from products where product_id in (@Ids)";
        var result = Run(sql);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3003");
        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName == "Products.EachQuery.g.cs");
    }

    [Fact]
    public void Each_WithProc_FailsJNT3003()
    {
        string sql = "-- @each Ids\n-- @proc\nselect product_id, product_name from products where product_id in (@Ids)";
        var result = Run(sql);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3003");
    }

    [Fact]
    public void Each_UnknownParamName_FailsJNT3003_InsteadOfSilentlyIgnoring()
    {
        // Typo'd name: the query declares @Ids but the directive says Idz.
        // Silently ignoring it would generate a scalar-parameter method.
        string sql = "-- @each Idz\nselect product_id, product_name from products where product_id in (@Ids)";
        var result = Run(sql);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT3003");
        Assert.Contains("Idz", diag.GetMessage());
        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName == "Products.EachQuery.g.cs");
    }

    [Fact]
    public void Each_MatchingParamName_CaseInsensitive_NoJNT3003()
    {
        string sql = "-- @each ids\nselect product_id, product_name from products where product_id in (@Ids)";
        var result = Run(sql);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3003");
    }

    [Fact]
    public void Each_EmitsOversizeListGuard_WithDialectBudget()
    {
        var result = Run(EachSql); // sqlite schema → 32000 budget
        string src = QuerySource(result);

        Assert.Contains("if (Ids.Count > 32000)", src);
        Assert.Contains("throw new ArgumentException(", src);
    }

    [Fact]
    public void Each_OversizeGuard_RunsBeforeConnectionOpens()
    {
        var result = Run(EachSql);
        string src = QuerySource(result);

        int guardIdx = src.IndexOf("if (Ids.Count > 32000)");
        int openIdx = src.IndexOf("weOpened");
        Assert.True(guardIdx >= 0 && openIdx >= 0);
        Assert.True(guardIdx < openIdx, "the oversize-list guard must run before the connection is opened");
    }
}
