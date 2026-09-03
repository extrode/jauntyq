using System.Collections.Immutable;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class UnindexedAcceptanceTests
{
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
    ""shipments"": {
      ""name"": ""shipments"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""carrier"": { ""name"": ""carrier"", ""dbType"": ""varchar"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""pk_shipments"", ""columns"": [""id""], ""isUnique"": true }
      ]
    },
    ""regions"": {
      ""name"": ""regions"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""varchar"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""pk_regions"", ""columns"": [""id""], ""isUnique"": true }
      ]
    },
    ""order_lines"": {
      ""name"": ""order_lines"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""shipment_id"": { ""name"": ""shipment_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""region_id"": { ""name"": ""region_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""qty"": { ""name"": ""qty"", ""dbType"": ""int"", ""isNullable"": false }
      },
      ""indexes"": [
        { ""name"": ""pk_order_lines"", ""columns"": [""id""], ""isUnique"": true },
        { ""name"": ""ix_order_lines_region"", ""columns"": [""region_id""], ""isUnique"": false }
      ]
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""order_lines"", ""fromColumn"": ""order_id"", ""toTable"": ""orders"", ""toColumn"": ""id"" },
    { ""fromTable"": ""order_lines"", ""fromColumn"": ""shipment_id"", ""toTable"": ""shipments"", ""toColumn"": ""id"" },
    { ""fromTable"": ""order_lines"", ""fromColumn"": ""region_id"", ""toTable"": ""regions"", ""toColumn"": ""id"" }
  ]
}";

    private const string AcceptPath = "db/schema/jaunty.accept.json";

    private const string AnchorPath = "db/tables/Orders/GetOne.sql";
    private const string AnchorSql = "select o.id, o.placed_on from orders o where o.id = @id";

    private static string Accepts(params string[] entries) =>
        "{ \"allowUnindexed\": [" + string.Join(", ", entries) + "] }";

    private static string Entry(string table, string column, string reason) =>
        $"{{ \"table\": \"{table}\", \"column\": \"{column}\", \"reason\": \"{reason}\" }}";

    private static ImmutableArray<Diagnostic> Run(
        bool autoCrud,
        (string path, string json)[]? acceptFiles = null,
        params (string path, string sql)[] files)
    {
        var compilation = CSharpCompilation.Create("UnindexedAcceptanceTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = ImmutableArray.CreateBuilder<AdditionalText>();
        foreach (var (path, sql) in files)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("db/schema/jaunty.schema.json", FkSchema));
        if (acceptFiles != null)
            foreach (var (path, json) in acceptFiles)
                texts.Add(new InMemoryAdditionalText(path, json));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutable())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Diagnostics;
    }

    private static ImmutableArray<Diagnostic> WithAcceptance(string json, params (string path, string sql)[] files) =>
        Run(autoCrud: true, new[] { (AcceptPath, json) }, files);

    private static List<string> Messages(ImmutableArray<Diagnostic> diags, string id) =>
        diags.Where(d => d.Id == id).Select(d => d.GetMessage()).ToList();

    private static (string path, string sql) OverrideAdvice(string message)
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
    public void WithNoAcceptanceFile_BothUnindexedLoadersRaiseJnt8004()
    {
        var diags = Run(autoCrud: true, null, (AnchorPath, AnchorSql));

        var unindexed = Messages(diags, "JNT8004");
        Assert.Equal(2, unindexed.Count);
        Assert.Single(unindexed, m => m.Contains("order_lines.order_id"));
        Assert.Single(unindexed, m => m.Contains("order_lines.shipment_id"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void AnAcceptedColumn_RaisesNoJnt8004()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql));

        Assert.DoesNotContain(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
    }

    [Fact]
    public void AnAcceptedColumn_LeavesTheSiblingColumnOnTheSameTableReported()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql));

        string remaining = Assert.Single(Messages(diags, "JNT8004"));
        Assert.Contains("order_lines.shipment_id", remaining);
    }

    [Fact]
    public void AUsedEntry_ReportsNoJnt8012()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql));

        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
    }

    [Fact]
    public void AnEntryOnAnIndexedColumn_IsReportedAsJnt8012()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "region_id", "kept from before the index landed")),
            (AnchorPath, AnchorSql));

        string dead = Assert.Single(Messages(diags, "JNT8012"));
        Assert.Contains(AcceptPath, dead);
        Assert.Contains("order_lines.region_id", dead);
        Assert.Contains("kept from before the index landed", dead);
        Assert.Contains("-- @allow-unindexed", dead);
    }

    [Fact]
    public void AnEntryWhoseSlotAHandWrittenFileClaims_IsReportedAsJnt8012()
    {
        string first = Assert.Single(
            Messages(Run(autoCrud: true, null, (AnchorPath, AnchorSql)), "JNT8004"),
            m => m.Contains("order_lines.order_id"));
        var (path, sql) = OverrideAdvice(first);

        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql),
            (path, "-- @allow-unindexed upstream schema, index not ours to add\n" + sql));

        string dead = Assert.Single(Messages(diags, "JNT8012"));
        Assert.Contains("order_lines.order_id", dead);
        Assert.Contains("claimed", dead);
        Assert.DoesNotContain(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
    }

    [Fact]
    public void AnEntryWithNoReason_IsReportedAsJnt6003_AndAcceptsNothing()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "   ")),
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("order_lines.order_id", invalid);
        Assert.Contains("'reason'", invalid);
        Assert.Contains(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void AnEntryWithNoTable_IsReportedAsJnt6003()
    {
        var diags = WithAcceptance(
            "{ \"allowUnindexed\": [ { \"column\": \"order_id\", \"reason\": \"upstream\" } ] }",
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("'table'", invalid);
        Assert.Contains(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
    }

    [Fact]
    public void AnEntryWithNoColumn_IsReportedAsJnt6003()
    {
        var diags = WithAcceptance(
            "{ \"allowUnindexed\": [ { \"table\": \"order_lines\", \"reason\": \"upstream\" } ] }",
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("'column'", invalid);
        Assert.Contains(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
    }

    [Fact]
    public void AnEntryNamingAnUnknownTable_IsReportedAsJnt6003_AndAcceptsNothing()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_line", "order_id", "typo in the table name")),
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("order_line", invalid);
        Assert.Contains("not in the schema snapshot", invalid);
        Assert.Equal(2, Messages(diags, "JNT8004").Count);
    }

    [Fact]
    public void AnEntryNamingAnUnknownColumn_IsReportedAsJnt6003_AndAcceptsNothing()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_ids", "typo in the column name")),
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("order_ids", invalid);
        Assert.Contains("does not have", invalid);
        Assert.Equal(2, Messages(diags, "JNT8004").Count);
    }

    [Fact]
    public void TheSameColumnAcceptedTwice_IsReportedAsJnt6003_AndTheFirstEntryStillApplies()
    {
        var diags = WithAcceptance(
            Accepts(
                Entry("order_lines", "order_id", "first reason"),
                Entry("order_lines", "order_id", "second reason")),
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("accepted more than once", invalid);
        Assert.DoesNotContain(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void TwoColumnsOfOneTableAcceptedSeparately_AreBothInForce()
    {
        var diags = WithAcceptance(
            Accepts(
                Entry("order_lines", "order_id", "upstream schema, index not ours to add"),
                Entry("order_lines", "shipment_id", "written once a quarter, scanned never")),
            (AnchorPath, AnchorSql));

        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
        Assert.Empty(Messages(diags, "JNT8004"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void UnparseableJson_IsReportedAsJnt6003_AndAcceptsNothing()
    {
        var diags = WithAcceptance("{ \"allowUnindexed\": [ ", (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains(AcceptPath, invalid);
        Assert.Contains("allowUnindexed", invalid);
        Assert.Equal(2, Messages(diags, "JNT8004").Count);
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void JsonThatIsTheLiteralNull_IsReportedAsJnt6003()
    {
        var diags = WithAcceptance("null", (AnchorPath, AnchorSql));

        Assert.Single(Messages(diags, "JNT6003"));
        Assert.Equal(2, Messages(diags, "JNT8004").Count);
    }

    [Fact]
    public void TwoAcceptanceFiles_AreReportedAsJnt6003_AndTheOrdinalLowestWins()
    {
        var diags = Run(autoCrud: true,
            new[]
            {
                ("db/schema/b.accept.json", Accepts(Entry("order_lines", "shipment_id", "from the later file"))),
                ("db/schema/a.accept.json", Accepts(Entry("order_lines", "order_id", "from the earlier file"))),
            },
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("db/schema/a.accept.json", invalid);
        Assert.Contains("db/schema/b.accept.json", invalid);

        string remaining = Assert.Single(Messages(diags, "JNT8004"));
        Assert.Contains("order_lines.shipment_id", remaining);
    }

    [Fact]
    public void AHandWrittenQueryFilteringAnAcceptedColumn_StillRaisesJnt8004()
    {
        var diags = WithAcceptance(
            Accepts(Entry("order_lines", "order_id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql),
            ("db/tables/OrderLines/GetLinesForOrder.sql",
             "select l.id, l.order_id from order_lines l where l.order_id = @orderId"));

        var unindexed = Messages(diags, "JNT8004");
        Assert.Equal(2, unindexed.Count);
        Assert.Single(unindexed, m => m.Contains("order_lines.order_id"));
        Assert.DoesNotContain(unindexed, m => m.Contains("order_lines.order_id") && m.Contains("auto-CRUD"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void WithAutoCrudOff_AnAcceptanceReportsNoJnt8012()
    {
        var diags = Run(autoCrud: false,
            new[] { (AcceptPath, Accepts(Entry("order_lines", "order_id", "upstream schema"))) },
            (AnchorPath, AnchorSql));

        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
        Assert.DoesNotContain(diags, d => d.Id == "JNT8004");
    }

    [Fact]
    public void WithAutoCrudOff_AStructuralProblemIsStillReportedAsJnt6003()
    {
        var diags = Run(autoCrud: false,
            new[] { (AcceptPath, Accepts(Entry("order_lines", "order_ids", "typo in the column name"))) },
            (AnchorPath, AnchorSql));

        string invalid = Assert.Single(Messages(diags, "JNT6003"));
        Assert.Contains("order_ids", invalid);
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
    }

    [Fact]
    public void AnEmptyAllowUnindexedList_ReportsNeitherJnt6003NorJnt8012()
    {
        var diags = WithAcceptance("{ \"allowUnindexed\": [] }", (AnchorPath, AnchorSql));

        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
        Assert.Equal(2, Messages(diags, "JNT8004").Count);
    }

    [Fact]
    public void TheAcceptanceMatchesCaseInsensitivelyOnBothHalves()
    {
        var diags = WithAcceptance(
            Accepts(Entry("ORDER_LINES", "Order_Id", "upstream schema, index not ours to add")),
            (AnchorPath, AnchorSql));

        Assert.DoesNotContain(Messages(diags, "JNT8004"), m => m.Contains("order_lines.order_id"));
        Assert.DoesNotContain(diags, d => d.Id == "JNT8012");
        Assert.DoesNotContain(diags, d => d.Id == "JNT6003");
    }

    [Fact]
    public void TheSyntheticHint_NamesTheAcceptanceFileAsTheAlternative()
    {
        string message = Assert.Single(
            Messages(Run(autoCrud: true, null, (AnchorPath, AnchorSql)), "JNT8004"),
            m => m.Contains("order_lines.order_id"));

        Assert.Contains("jaunty.accept.json", message);
        Assert.Contains("allowUnindexed", message);
        Assert.Contains("\"table\": \"order_lines\"", message);
        Assert.Contains("\"column\": \"order_id\"", message);
    }

    [Fact]
    public void Synthesize_SetsFilterColumnOnFkLoadersOnly()
    {
        var schema = SchemaLoader.Load(FkSchema);
        var synthesized = AutoCrud.Synthesize(schema).ToList();

        var lines = synthesized.Where(s => s.TableName == "order_lines").ToList();
        Assert.NotEmpty(lines);

        foreach (var synth in lines.Where(s => s.MethodName.StartsWith("GetBy", StringComparison.Ordinal)
                                               && s.MethodName != "GetById"))
        {
            Assert.NotNull(synth.FilterColumn);
        }

        Assert.Equal("order_id", Assert.Single(lines, s => s.MethodName == "GetByOrderId").FilterColumn);
        Assert.Equal("shipment_id", Assert.Single(lines, s => s.MethodName == "GetByShipmentId").FilterColumn);
        Assert.Equal("region_id", Assert.Single(lines, s => s.MethodName == "GetByRegionId").FilterColumn);

        foreach (var synth in synthesized.Where(s => s.MethodName is "GetAll" or "GetById" or "Insert"
                                                     or "Update" or "Delete" or "Upsert"))
        {
            Assert.Null(synth.FilterColumn);
        }
    }
}
