using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class AllowSortDirectiveTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""articles"": {
      ""name"": ""articles"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""author_id"": { ""name"": ""author_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""ix_articles_author"", ""columns"": [""author_id""], ""isUnique"": false }
      ]
    }
  }
}";

    private const string CompositeIndexSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""articles"": {
      ""name"": ""articles"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""author_id"": { ""name"": ""author_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""ix_articles_author_created"", ""columns"": [""author_id"", ""created_at""], ""isUnique"": false }
      ]
    }
  }
}";

    private const string NoIndexMetadataSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""articles"": {
      ""name"": ""articles"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""author_id"": { ""name"": ""author_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""created_at"": { ""name"": ""created_at"", ""dbType"": ""timestamp"", ""isNullable"": false },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": []
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql, string schemaJson = SchemaJson)
    {
        var compilation = CSharpCompilation.Create("AllowSortTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Articles/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private const string UnsortedQuery =
        "select articles.id, articles.title\n" +
        "from articles\n" +
        "order by articles.created_at";

    private const string Reason = "20 rows a page, the sort never sees more than a screenful";

    [Fact]
    public void Directive_IsParsed()
    {
        var (directives, _) = DirectiveParser.Parse($"-- @allow-sort {Reason}\nselect 1");

        Assert.Equal(Reason, directives.AllowSortReason);
    }

    [Fact]
    public void Directive_LineIsStrippedFromTheCleanedSql()
    {
        var (_, cleaned) = DirectiveParser.Parse($"-- @allow-sort {Reason}\nselect 1");

        Assert.DoesNotContain("@allow-sort", cleaned);
    }

    [Fact]
    public void WithoutTheDirective_UnindexedOrderBy_RaisesJNT8007()
    {
        var result = Run(UnsortedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void WithTheDirective_JNT8007_IsSuppressed()
    {
        var result = Run($"-- @allow-sort {Reason}\n" + UnsortedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void WithTheDirective_OtherDiagnosticsStillFire()
    {
        var result = Run(
            $"-- @allow-sort {Reason}\n" +
            "select *\n" +
            "from articles\n" +
            "order by articles.created_at");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3002");
    }

    [Fact]
    public void WithTheDirective_UnindexedFilter_StillRaisesJNT8004()
    {
        var result = Run(
            $"-- @allow-sort {Reason}\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.title = @title\n" +
            "order by articles.created_at");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void AllowUnindexed_DoesNotSuppressJNT8007()
    {
        var result = Run("-- @allow-unindexed the title index ships in migration 0042\n" + UnsortedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void BareDirective_SetsNoReason()
    {
        var (directives, _) = DirectiveParser.Parse("-- @allow-sort\nselect 1");

        Assert.Null(directives.AllowSortReason);
    }

    [Fact]
    public void BareDirective_IsReportedAsJNT3008_AndSuppressesNothing()
    {
        var result = Run("-- @allow-sort\n" + UnsortedQuery);

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void DirectiveOnAnIndexedSort_IsReportedAsJNT8012()
    {
        var result = Run(
            $"-- @allow-sort {Reason}\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "order by articles.author_id");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8012");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("-- @allow-sort", diag.GetMessage());
        Assert.Contains(Reason, diag.GetMessage());
    }

    [Fact]
    public void DirectiveThatActuallySuppresses_IsNotReportedAsUnnecessary()
    {
        var result = Run($"-- @allow-sort {Reason}\n" + UnsortedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    [Fact]
    public void NoDirectiveAtAll_NeverReportsJNT8012()
    {
        var result = Run(UnsortedQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    [Fact]
    public void BothDirectives_LiveFilterAndDeadSort_ReportsOnlyTheSortAsUnnecessary()
    {
        var result = Run(
            "-- @allow-unindexed the title index ships in migration 0042\n" +
            $"-- @allow-sort {Reason}\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.title = @title\n" +
            "order by articles.author_id");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8012");
        Assert.Contains("-- @allow-sort", diag.GetMessage());
        Assert.DoesNotContain("-- @allow-unindexed", diag.GetMessage());
    }

    [Fact]
    public void BothDirectives_LiveSortAndDeadFilter_ReportsOnlyTheFilterAsUnnecessary()
    {
        var result = Run(
            "-- @allow-unindexed left over from before the index landed\n" +
            $"-- @allow-sort {Reason}\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.author_id = @authorId\n" +
            "order by articles.created_at");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8012");
        Assert.Contains("-- @allow-unindexed", diag.GetMessage());
        Assert.DoesNotContain("-- @allow-sort", diag.GetMessage());
    }

    [Fact]
    public void CompositeIndex_EachOrderByItemIsTestedIndependently_NonLeadingUnfilteredRaisesJNT8007()
    {
        var result = Run(
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "order by articles.author_id desc, articles.created_at",
            CompositeIndexSchemaJson);

        Assert.Contains(result.Diagnostics,
            d => d.Id == "JNT8007" && d.GetMessage().Contains("articles.created_at"));
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id == "JNT8007" && d.GetMessage().Contains("articles.author_id"));
    }

    [Fact]
    public void CompositeIndex_NonEqualityFilterOnTheLeadingColumn_SuppressesJNT8007()
    {
        var result = Run(
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.author_id > @since\n" +
            "order by articles.created_at",
            CompositeIndexSchemaJson);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
    }

    [Fact]
    public void SnapshotWithNoIndexMetadata_DirectiveIsNotReportedAsUnnecessary()
    {
        var result = Run($"-- @allow-sort {Reason}\n" + UnsortedQuery, NoIndexMetadataSchemaJson);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    [Fact]
    public void QueryThatFailsEarlierValidation_DirectiveIsNotReportedAsUnnecessary()
    {
        var result = Run(
            $"-- @allow-sort {Reason}\n" +
            "select t.id from no_such_table t order by t.author_id");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }

    [Fact]
    public void BothDirectives_BothLive_ReportsNeitherAsUnnecessary()
    {
        var result = Run(
            "-- @allow-unindexed the title index ships in migration 0042\n" +
            $"-- @allow-sort {Reason}\n" +
            "select articles.id, articles.title\n" +
            "from articles\n" +
            "where articles.title = @title\n" +
            "order by articles.created_at");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8007");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
    }
}
