using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Generator.Directives;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class AllowNPlusOneDirectiveTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""customer_name"": { ""name"": ""customer_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 },
        ""placed_at"": { ""name"": ""placed_at"", ""dbType"": ""datetime"", ""isNullable"": false }
      },
      ""indexes"": []
    },
    ""order_items"": {
      ""name"": ""order_items"",
      ""columns"": {
        ""order_item_id"": { ""name"": ""order_item_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""sku"": { ""name"": ""sku"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""quantity"": { ""name"": ""quantity"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""ix_order_items_order"", ""columns"": [""order_id""], ""isUnique"": false }
      ]
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""order_items"", ""fromColumn"": ""order_id"", ""toTable"": ""orders"", ""toColumn"": ""order_id"" }
  ]
}";

    private const string ParentGetAll = "select order_id, customer_name\nfrom orders";
    private const string ChildGetByOrderId = "select order_item_id, sku, quantity\nfrom order_items\nwhere order_items.order_id = @order_id";

    private const string Reason = "single-order screen, never called per row of a list";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("AllowNPlusOneTestAssembly",
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

    [Fact]
    public void Directive_IsParsed()
    {
        var (directives, _) = DirectiveParser.Parse($"-- @allow-n-plus-one {Reason}\nselect 1");

        Assert.Equal(Reason, directives.AllowNPlusOneReason);
    }

    [Fact]
    public void Directive_LineIsStrippedFromTheCleanedSql()
    {
        var (_, cleaned) = DirectiveParser.Parse($"-- @allow-n-plus-one {Reason}\nselect 1");

        Assert.DoesNotContain("@allow-n-plus-one", cleaned);
    }

    [Fact]
    public void BareDirective_SetsNoReason()
    {
        var (directives, _) = DirectiveParser.Parse("-- @allow-n-plus-one\nselect 1");

        Assert.Null(directives.AllowNPlusOneReason);
    }

    [Fact]
    public void BareDirective_IsReportedAsJNT3008_AndSuppressesNothing()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", "-- @allow-n-plus-one\n" + ChildGetByOrderId));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3008");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8013");
    }

    [Fact]
    public void WithoutTheDirective_ChildLookupRaisesJNT8008()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("OrderItems.GetByOrderId", diag.GetMessage());
    }

    [Fact]
    public void WithTheDirective_JNT8008_IsSuppressed()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", $"-- @allow-n-plus-one {Reason}\n" + ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void DirectiveThatActuallySuppresses_IsNotReportedAsJNT8013()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", $"-- @allow-n-plus-one {Reason}\n" + ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8013");
    }

    [Fact]
    public void DirectiveWithNoParentCollectionInTheCorpus_IsReportedAsJNT8013()
    {
        var result = Run(
            ("db/OrderItems/GetByOrderId.sql", $"-- @allow-n-plus-one {Reason}\n" + ChildGetByOrderId));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8013");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("-- @allow-n-plus-one", diag.GetMessage());
        Assert.Contains("suppresses nothing", diag.GetMessage());
        Assert.Contains(Reason, diag.GetMessage());
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void NoDirectiveAtAll_NeverReportsJNT8013()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8013");
    }

    [Fact]
    public void WithTheDirective_OtherDiagnosticsStillFire()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql",
                $"-- @allow-n-plus-one {Reason}\nselect *\nfrom order_items\nwhere order_items.order_id = @order_id"));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3002");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8013");
    }

    [Fact]
    public void AllowUnindexed_DoesNotSuppressJNT8008()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql",
                "-- @allow-unindexed the order_id index ships in migration 0042\n" + ChildGetByOrderId));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void DirectiveOnTheParent_LeavesTheChildsJNT8008Firing_AndGetsItsOwnJNT8013()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", $"-- @allow-n-plus-one {Reason}\n" + ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8008");

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8013");
        Assert.Equal("db/Orders/GetAll.sql", diag.Location.GetLineSpan().Path);
    }

    [Fact]
    public void DirectiveOnAnAlreadySetBasedChild_IsReportedAsJNT8013()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderIds.sql",
                $"-- @allow-n-plus-one {Reason}\nselect order_item_id, sku, quantity\nfrom order_items\n" +
                "-- @each OrderIds\nwhere order_items.order_id in (@OrderIds)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains(result.Diagnostics, d => d.Id == "JNT8013");
    }

    [Fact]
    public void JNT8013_IsAnchoredAtTheChildsSqlPath()
    {
        var result = Run(
            ("db/OrderItems/GetByOrderId.sql", $"-- @allow-n-plus-one {Reason}\n" + ChildGetByOrderId));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8013");
        Assert.Equal("db/OrderItems/GetByOrderId.sql", diag.Location.GetLineSpan().Path);
    }

    [Fact]
    public void RepeatedDirective_IsReportedAsJNT3011()
    {
        var result = Run(
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql",
                $"-- @allow-n-plus-one {Reason}\n-- @allow-n-plus-one a different reason\n" + ChildGetByOrderId));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT3011");
    }

    [Fact]
    public void AllowUnindexedAndAllowNPlusOne_BothLive_SuppressBothWithNeitherDeadDirectiveWarning()
    {
        const string unindexedSchema = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""customer_name"": { ""name"": ""customer_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 60 },
        ""placed_at"": { ""name"": ""placed_at"", ""dbType"": ""datetime"", ""isNullable"": false }
      },
      ""indexes"": []
    },
    ""order_items"": {
      ""name"": ""order_items"",
      ""columns"": {
        ""order_item_id"": { ""name"": ""order_item_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""sku"": { ""name"": ""sku"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 },
        ""quantity"": { ""name"": ""quantity"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": []
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""order_items"", ""fromColumn"": ""order_id"", ""toTable"": ""orders"", ""toColumn"": ""order_id"" }
  ]
}";

        var compilation = CSharpCompilation.Create("AllowNPlusOneTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>
        {
            new InMemoryAdditionalText("schema/jaunty.schema.json", unindexedSchema),
            new InMemoryAdditionalText("db/Orders/GetAll.sql", ParentGetAll),
            new InMemoryAdditionalText("db/OrderItems/GetByOrderId.sql",
                "-- @allow-unindexed the order_id index ships in migration 0042\n" +
                $"-- @allow-n-plus-one {Reason}\n" + ChildGetByOrderId),
        };

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var result = driver.GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8012");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8013");
    }
}
