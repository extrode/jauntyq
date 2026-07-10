using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// JNT8008 N+1 heuristic: a child point-lookup by a full foreign key paired
/// with a parent-collection query over the FK's parent table fires a Warning
/// on the child lookup suggesting a join or a batched IN. Guards keep the
/// false-positive rate at zero: already-IN children, join children,
/// single-row PK parents, FK-less snapshots and suppressed builds never fire.
/// </summary>
public class NPlusOneAnalyzerTests
{
    // orders (parent) / order_items (child) with the FK declared.
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

    /// <summary>Same shape with the FK metadata removed.</summary>
    private static string SchemaWithoutFks => SchemaJson.Replace(
        @"""foreignKeys"": [
    { ""fromTable"": ""order_items"", ""fromColumn"": ""order_id"", ""toTable"": ""orders"", ""toColumn"": ""order_id"" }
  ]",
        @"""foreignKeys"": []");

    private const string ParentGetAll = "select order_id, customer_name\nfrom orders";
    private const string ChildGetByOrderId = "select order_item_id, sku, quantity\nfrom order_items\nwhere order_items.order_id = @order_id";

    private static GeneratorDriverRunResult Run(string schemaJson, params (string Path, string Text)[] files)
        => Run(schemaJson, options => options, files);

    private static GeneratorDriverRunResult Run(
        string schemaJson,
        Func<CSharpCompilationOptions, CSharpCompilationOptions> configureOptions,
        params (string Path, string Text)[] files)
    {
        var options = configureOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compilation = CSharpCompilation.Create("NPlusOneTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            options);

        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson) };
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // ── US1: the FK-linked parent/child pair fires on the child ─────────

    [Fact]
    public void ParentCollectionPlusChildFkLookup_JNT8008OnChild()
    {
        var result = Run(SchemaJson,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);

        // Names the child query, the parent-collection query and the FK,
        // and suggests both set-based fixes (join / batched IN).
        string message = diag.GetMessage();
        Assert.Contains("OrderItems.GetByOrderId", message);
        Assert.Contains("Orders.GetAll", message);
        Assert.Contains("order_id", message);
        Assert.Contains("orders", message);
        Assert.Contains("join", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IN (...)", message);

        // A warning never blocks: both queries still compiled.
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "OrderItems.GetByOrderId.g.cs");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Orders.GetAll.g.cs");
    }

    [Fact]
    public void NoForeignKeyInSnapshot_NoWarning()
    {
        var result = Run(SchemaWithoutFks,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void NoParentCollectionQuery_NoWarning()
    {
        // The child lookup alone has nothing to iterate: no N+1 shape.
        var result = Run(SchemaJson,
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void TwoParentCollectionQueries_StillExactlyOneWarning()
    {
        // Fires once per child lookup, naming one representative parent
        // (the ordinal-first name), not once per matching parent query.
        var result = Run(SchemaJson,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/Orders/GetRecent.sql",
                "select order_id, customer_name\nfrom orders\nwhere orders.placed_at >= @since\n-- @params since:System.DateTime"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("Orders.GetAll", diag.GetMessage());
    }

    // ── US2: guards — legitimately set-based / intentional shapes ───────

    [Fact]
    public void ChildAlreadyBatchedWithIn_NoWarning()
    {
        var result = Run(SchemaJson,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderIds.sql",
                "select order_item_id, sku, quantity\nfrom order_items\nwhere order_items.order_id in (@order_ids)"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void ChildJoinsParent_NoWarning()
    {
        // Even with the FK equality present, joining the parent is set-based.
        var result = Run(SchemaJson,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetWithOrder.sql",
                "select oi.order_item_id, oi.sku, o.customer_name\nfrom order_items oi\njoin orders o on oi.order_id = o.order_id\nwhere oi.order_id = @order_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void ParentIsSingleRowPkLookup_NoWarning()
    {
        // A parent narrowed to one row by its PK is no collection to iterate.
        var result = Run(SchemaJson,
            ("db/Orders/GetById.sql",
                "-- @first\nselect order_id, customer_name\nfrom orders\nwhere orders.order_id = @order_id"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void SuppressedViaStandardDiagnosticOptions_NoWarning()
    {
        // FR-005: the standard Roslyn suppression path (equivalent to
        // <NoWarn>JNT8008</NoWarn> / .editorconfig severity = none). The
        // diagnostic is reported at Location.None, so project-level
        // suppression is the applicable standard mechanism.
        var result = Run(SchemaJson,
            options => options.WithSpecificDiagnosticOptions(
                ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("JNT8008", ReportDiagnostic.Suppress)),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    // ── US2: composite FK — all columns or nothing ──────────────────────

    // warehouses parent with a composite key; pallets child carries a
    // two-column FK (one snapshot row per column pair, same table pair).
    private const string CompositeSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""warehouses"": {
      ""name"": ""warehouses"",
      ""columns"": {
        ""region"": { ""name"": ""region"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 10, ""isPrimaryKey"": true },
        ""code"": { ""name"": ""code"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 10, ""isPrimaryKey"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 40 }
      },
      ""indexes"": []
    },
    ""pallets"": {
      ""name"": ""pallets"",
      ""columns"": {
        ""pallet_id"": { ""name"": ""pallet_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""wh_region"": { ""name"": ""wh_region"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 10 },
        ""wh_code"": { ""name"": ""wh_code"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 10 },
        ""contents"": { ""name"": ""contents"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 }
      },
      ""indexes"": []
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""pallets"", ""fromColumn"": ""wh_region"", ""toTable"": ""warehouses"", ""toColumn"": ""region"" },
    { ""fromTable"": ""pallets"", ""fromColumn"": ""wh_code"", ""toTable"": ""warehouses"", ""toColumn"": ""code"" }
  ]
}";

    [Fact]
    public void CompositeFk_AllColumnsFiltered_Fires()
    {
        var result = Run(CompositeSchemaJson,
            ("db/Warehouses/GetAll.sql", "select region, code, label\nfrom warehouses"),
            ("db/Pallets/GetByWarehouse.sql",
                "select pallet_id, contents\nfrom pallets\nwhere pallets.wh_region = @region and pallets.wh_code = @code"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("Pallets.GetByWarehouse", diag.GetMessage());
        Assert.Contains("wh_region", diag.GetMessage());
        Assert.Contains("wh_code", diag.GetMessage());
    }

    [Fact]
    public void CompositeFk_PartialColumnFilter_NoWarning()
    {
        // Only one of the two FK columns: not a per-parent point lookup.
        var result = Run(CompositeSchemaJson,
            ("db/Warehouses/GetAll.sql", "select region, code, label\nfrom warehouses"),
            ("db/Pallets/GetByRegion.sql",
                "select pallet_id, contents\nfrom pallets\nwhere pallets.wh_region = @region"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    // ── US2: self-referencing FK ────────────────────────────────────────

    private const string SelfFkSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""employees"": {
      ""name"": ""employees"",
      ""columns"": {
        ""employee_id"": { ""name"": ""employee_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""manager_id"": { ""name"": ""manager_id"", ""dbType"": ""int"", ""isNullable"": true },
        ""full_name"": { ""name"": ""full_name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 80 }
      },
      ""indexes"": []
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""employees"", ""fromColumn"": ""manager_id"", ""toTable"": ""employees"", ""toColumn"": ""employee_id"" }
  ]
}";

    [Fact]
    public void SelfReferencingFk_CollectionPlusLookupOnSameTable_Fires()
    {
        var result = Run(SelfFkSchemaJson,
            ("db/Employees/GetAll.sql", "select employee_id, full_name\nfrom employees"),
            ("db/Employees/GetByManagerId.sql",
                "select employee_id, full_name\nfrom employees\nwhere employees.manager_id = @manager_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("Employees.GetByManagerId", diag.GetMessage());
        Assert.Contains("Employees.GetAll", diag.GetMessage());
        Assert.Contains("manager_id", diag.GetMessage());
    }

    [Fact]
    public void SelfReferencingFk_LookupAloneInCorpus_NoWarning()
    {
        // The lookup is itself a multi-row SELECT over employees, but it
        // cannot be its own parent collection: with no other collection
        // query in the corpus there is nothing to iterate.
        var result = Run(SelfFkSchemaJson,
            ("db/Employees/GetByManagerId.sql",
                "select employee_id, full_name\nfrom employees\nwhere employees.manager_id = @manager_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    // ── US2: scalar aggregate-per-parent variant ────────────────────────

    [Fact]
    public void ScalarCountPerParent_FiresWithGroupedAggregateSuggestion()
    {
        var result = Run(SchemaJson,
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/CountByOrderId.sql",
                "select count(*) as item_count\nfrom order_items\nwhere order_items.order_id = @order_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
        Assert.Contains("OrderItems.CountByOrderId", diag.GetMessage());
        Assert.Contains("Orders.GetAll", diag.GetMessage());
        Assert.Contains("grouped aggregate join", diag.GetMessage());
    }
}
