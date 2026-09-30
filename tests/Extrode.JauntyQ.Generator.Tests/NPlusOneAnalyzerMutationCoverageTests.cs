using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class NPlusOneAnalyzerMutationCoverageTests
{
    private static string Col(string name, bool pk = false, string type = "int") =>
        $"\"{name}\": {{ \"name\": \"{name}\", \"dbType\": \"{type}\", \"isNullable\": false, \"isPrimaryKey\": {(pk ? "true" : "false")} }}";

    private static string Index(string name, bool unique, params string[] columns) =>
        $"{{ \"name\": \"{name}\", \"columns\": [{string.Join(", ", columns.Select(c => $"\"{c}\""))}], \"isUnique\": {(unique ? "true" : "false")} }}";

    private static string Table(string name, string[] columns, params string[] indexes) =>
        $"\"{name}\": {{ \"name\": \"{name}\", \"columns\": {{ {string.Join(", ", columns)} }}, \"indexes\": [{string.Join(", ", indexes)}] }}";

    private static string Fk(string fromTable, string fromColumn, string toTable, string toColumn) =>
        $"{{ \"fromTable\": \"{fromTable}\", \"fromColumn\": \"{fromColumn}\", \"toTable\": \"{toTable}\", \"toColumn\": \"{toColumn}\" }}";

    private static string Schema(string[] tables, params string[] fks) =>
        $"{{ \"dialect\": \"postgres\", \"tables\": {{ {string.Join(", ", tables)} }}, \"foreignKeys\": [{string.Join(", ", fks)}] }}";

    private static readonly string Orders = Table("orders",
        new[] { Col("order_id", pk: true), Col("customer_name", type: "text") },
        Index("ix_orders_customer", false, "customer_name"));

    private static readonly string OrderItems = Table("order_items",
        new[] { Col("order_item_id", pk: true), Col("order_id"), Col("sku", type: "text") },
        Index("ix_order_items_order", false, "order_id"));

    private static readonly string OrderItemsFk = Fk("order_items", "order_id", "orders", "order_id");

    private const string ParentGetAll = "select order_id, customer_name\nfrom orders";
    private const string ChildGetByOrderId = "select order_item_id, sku\nfrom order_items\nwhere order_items.order_id = @order_id";

    private static string PointLookupMessage(string child, string childTable, string parentTable, string columns, string parent) =>
        $"{child} is a point lookup on {childTable} by its foreign key to {parentTable} ({columns}), and {parent} " +
        $"returns a {parentTable} collection: calling it once per row is an N+1 query pattern. Fetch the rows " +
        $"set-based instead: join {childTable} into the parent query, or batch the lookups with WHERE {columns} IN (...).";

    private static GeneratorDriverRunResult Run(string schemaJson, params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("NPlusOneMutationTestAssembly",
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
        var result = driver.GetRunResult();
        Assert.Null(result.Results[0].Exception);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        return result;
    }

    private static string SingleNPlusOneMessage(GeneratorDriverRunResult result) =>
        Assert.Single(result.Diagnostics, d => d.Id == "JNT8008").GetMessage();

    [Fact]
    public void PointLookup_ExactMessage()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            "OrderItems.GetByOrderId is a point lookup on order_items by its foreign key to orders (order_id), and " +
            "Orders.GetAll returns a orders collection: calling it once per row is an N+1 query pattern. Fetch the " +
            "rows set-based instead: join order_items into the parent query, or batch the lookups with WHERE " +
            "order_id IN (...).",
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void AggregateChild_ExactMessage()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/CountByOrderId.sql",
                "select count(*) as n\nfrom order_items\nwhere order_items.order_id = @order_id"));

        Assert.Equal(
            "OrderItems.CountByOrderId computes a per-row aggregate on order_items filtered by its foreign key to " +
            "orders (order_id), and Orders.GetAll returns a orders collection: calling it once per row is an N+1 " +
            "query pattern. Consider a grouped aggregate join (JOIN order_items ... GROUP BY order_id) or a batched " +
            "WHERE order_id IN (...).",
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void DuplicateFkRow_ColumnNamedOnce()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void CompositeFk_ColumnsSortedAndCommaSeparated()
    {
        var orderLines = Table("order_lines", new[] { Col("order_id", pk: true), Col("line_no", pk: true) });
        var lineNotes = Table("line_notes", new[] { Col("note_id", pk: true), Col("order_id"), Col("line_no") });
        var result = Run(Schema(new[] { orderLines, lineNotes },
                Fk("line_notes", "order_id", "order_lines", "order_id"),
                Fk("line_notes", "line_no", "order_lines", "line_no")),
            ("db/OrderLines/GetAll.sql", "select order_id, line_no\nfrom order_lines"),
            ("db/LineNotes/GetByLine.sql",
                "select note_id\nfrom line_notes\nwhere line_notes.order_id = @order_id and line_notes.line_no = @line_no"));

        Assert.Equal(
            PointLookupMessage("LineNotes.GetByLine", "line_notes", "order_lines", "line_no, order_id", "OrderLines.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void UniqueIndexExactlyOnFk_StillFires()
    {
        var oneToOne = Table("order_items",
            new[] { Col("order_item_id", pk: true), Col("order_id"), Col("sku", type: "text") },
            Index("ux_order_items_order", true, "order_id"));
        var result = Run(Schema(new[] { Orders, oneToOne }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void ParentWithoutPrimaryKey_IsStillACollection()
    {
        var keyless = Table("orders", new[] { Col("order_id"), Col("customer_name", type: "text") });
        var result = Run(Schema(new[] { keyless, OrderItems }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void ChildWithoutPrimaryKey_StillFires()
    {
        var keyless = Table("order_items", new[] { Col("order_item_id"), Col("order_id"), Col("sku", type: "text") });
        var result = Run(Schema(new[] { Orders, keyless }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void ParentFilteredByNonUniqueIndex_IsStillACollection()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetByCustomer.sql",
                "select order_id, customer_name\nfrom orders\nwhere orders.customer_name = @customer_name"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetByCustomer"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void DeleteByFk_IsNeverAChildLookup()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/DeleteByOrderId.sql", "delete from order_items\nwhere order_items.order_id = @order_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void RepresentativeParent_IsTheOrdinalFirstName_WhateverTheFileOrder()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/ZList.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId),
            ("db/Orders/AList.sql", ParentGetAll));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.AList"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void UnusedDirectives_ReportedInNameOrder()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/ZOne.sql", "-- @allow-n-plus-one zeta\nselect order_id\nfrom orders"),
            ("db/Orders/AOne.sql", "-- @allow-n-plus-one alpha\nselect order_id\nfrom orders"));

        var messages = result.Diagnostics.Where(d => d.Id == "JNT8013").Select(d => d.GetMessage()).ToList();
        Assert.Equal(2, messages.Count);
        Assert.EndsWith("(Stated reason: alpha)", messages[0]);
        Assert.EndsWith("(Stated reason: zeta)", messages[1]);
    }

    [Fact]
    public void LookupPinningTwoChildTables_FiresOnceForTheFirstChildInOrder()
    {
        var carriers = Table("carriers", new[] { Col("carrier_id", pk: true), Col("carrier_name", type: "text") });
        var shipments = Table("shipments", new[] { Col("shipment_id", pk: true), Col("carrier_id"), Col("order_ref") });
        var result = Run(Schema(new[] { Orders, OrderItems, carriers, shipments },
                Fk("shipments", "carrier_id", "carriers", "carrier_id"),
                OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/Carriers/GetAll.sql", "select carrier_id, carrier_name\nfrom carriers"),
            ("db/OrderItems/GetShipped.sql",
                "select oi.sku\nfrom order_items oi\njoin shipments s on s.order_ref = oi.order_id\n" +
                "where oi.order_id = @order_id and s.carrier_id = @carrier_id"));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetShipped", "order_items", "orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void FkOfAnotherChildTable_DoesNotCountAsASecondParent()
    {
        var archive = Table("order_archive", new[] { Col("order_id", pk: true) });
        var refunds = Table("refunds", new[] { Col("refund_id", pk: true), Col("order_id") });
        var result = Run(Schema(new[] { Orders, OrderItems, archive, refunds },
                OrderItemsFk,
                Fk("refunds", "order_id", "order_archive", "order_id")),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Theory]
    [InlineData("order_items", "order_id", "")]
    [InlineData("order_items", "", "orders")]
    [InlineData("", "order_id", "orders")]
    public void FkRowMissingAName_IsIgnored(string fromTable, string fromColumn, string toTable)
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk, Fk(fromTable, fromColumn, toTable, "order_id")),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void FkPairsWhoseNamesConcatenateAlike_StayDistinct()
    {
        var a = Table("a", new[] { Col("id", pk: true), Col("bc_id") });
        var bc = Table("bc", new[] { Col("id", pk: true) });
        var ab = Table("ab", new[] { Col("id", pk: true), Col("c_id") });
        var c = Table("c", new[] { Col("id", pk: true) });
        var result = Run(Schema(new[] { a, bc, ab, c }, Fk("a", "bc_id", "bc", "id"), Fk("ab", "c_id", "c", "id")),
            ("db/C/GetAll.sql", "select id\nfrom c"),
            ("db/Ab/GetByC.sql", "select id\nfrom ab\nwhere ab.c_id = @c_id"));

        Assert.Equal(PointLookupMessage("Ab.GetByC", "ab", "c", "c_id", "C.GetAll"), SingleNPlusOneMessage(result));
    }

    [Fact]
    public void SchemaTableKeyInDifferentCase_StillFound()
    {
        var upperOrders = Table("Orders", new[] { Col("order_id", pk: true), Col("customer_name", type: "text") });
        var result = Run(Schema(new[] { upperOrders, OrderItems }, Fk("order_items", "order_id", "Orders", "order_id")),
            ("db/Orders/GetAll.sql", "select order_id, customer_name\nfrom Orders"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "Orders", "order_id", "Orders.GetAll"),
            SingleNPlusOneMessage(result));
    }

    [Fact]
    public void CteNamedLikeAnUnknownFkParent_IsNotACollection()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, Fk("order_items", "order_id", "legacy_orders", "order_id")),
            ("db/Orders/GetLegacy.sql",
                "with legacy_orders as (select order_id from orders)\nselect order_id\nfrom legacy_orders"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void FkColumnWithAnExtraNonEqualityFilter_StillFires()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetAll.sql", ParentGetAll),
            ("db/OrderItems/GetByOrderIdExcept.sql",
                "select order_item_id, sku\nfrom order_items\nwhere order_items.order_id = @order_id and order_items.order_id <> @skipped"));

        Assert.Single(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Theory]
    [InlineData("-- @type n text\nselect upper(customer_name) as n\nfrom orders")]
    [InlineData("select count(*) as n, customer_name\nfrom orders\ngroup by customer_name")]
    [InlineData("-- @type t text\nselect null as t, order_id\nfrom orders")]
    public void ParentProjection_IsACollection(string parentSql)
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/List.sql", parentSql),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.List"),
            SingleNPlusOneMessage(result));
    }

    [Theory]
    [InlineData("-- @type v bigint\nselect (sum(order_id)) as v\nfrom orders")]
    [InlineData("-- @type v bigint\nselect sum(order_id + 1) as v\nfrom orders")]
    [InlineData("-- @type v bigint\nselect avg(order_id + 1) as v\nfrom orders")]
    [InlineData("-- @type v bigint\nselect min(order_id + 1) as v\nfrom orders")]
    [InlineData("-- @type v bigint\nselect max(order_id + 1) as v\nfrom orders")]
    public void AggregateOnlyParent_IsNotACollection(string parentSql)
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/Total.sql", parentSql),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Orders.Total.g.cs");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void AmbiguousUnqualifiedBinding_IsSkipped()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetWithItems.sql",
                "select orders.order_id, order_items.sku\nfrom orders\n" +
                "join order_items on order_items.order_id = orders.order_id\nwhere order_id = @order_id\n" +
                "-- @params order_id:int"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Orders.GetWithItems.g.cs");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT8008");
    }

    [Fact]
    public void SubqueryLocalBinding_IsSkipped()
    {
        var result = Run(Schema(new[] { Orders, OrderItems }, OrderItemsFk),
            ("db/Orders/GetBySku.sql",
                "select order_id, customer_name\nfrom orders\n" +
                "where orders.order_id in (select oi.order_id from order_items oi where oi.sku = @sku)\n-- @params sku:string"),
            ("db/OrderItems/GetByOrderId.sql", ChildGetByOrderId));

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(
            PointLookupMessage("OrderItems.GetByOrderId", "order_items", "orders", "order_id", "Orders.GetBySku"),
            SingleNPlusOneMessage(result));
    }
}
