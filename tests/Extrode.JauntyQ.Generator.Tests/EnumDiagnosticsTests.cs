using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Spec 013 T8/T9. A captured enum contributes three names to the generated
/// namespace — the enum, its <c>{EnumName}Values</c> companion, and the shared
/// <c>JauntyQEnumValueException</c> — and its members contribute one C#
/// identifier each. JNT2016 covers a member-name collision, JNT2017 a type-name
/// one. Both are errors: silently renaming is the anti-pattern JNT2015 exists
/// to end.
/// </summary>
public class EnumDiagnosticsTests
{
    /// <summary>
    /// One enum with a caller-chosen member list, referenced by a column on a
    /// caller-chosen table, so both diagnostics can be provoked from the same
    /// shape.
    /// </summary>
    private static string SchemaJson(string enumName, string members, string table = "orders", string extraTables = "")
        => $@"{{
  ""dialect"": ""postgres"",
  ""enums"": {{
    ""{enumName}"": {{ ""name"": ""{enumName}"", ""members"": [ {members} ] }}
  }},
  ""tables"": {{
    ""{table}"": {{
      ""name"": ""{table}"",
      ""columns"": {{
        ""id"": {{ ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""status"": {{ ""name"": ""status"", ""dbType"": ""{enumName}"", ""isNullable"": false, ""enumName"": ""{enumName}"" }}
      }}
    }}{extraTables}
  }}
}}";

    private static string Member(string value, string csharpName)
        => $@"{{ ""value"": ""{value}"", ""csharpName"": ""{csharpName}"" }}";

    private static string Table(string name)
        => $@",
    ""{name}"": {{
      ""name"": ""{name}"",
      ""columns"": {{
        ""id"": {{ ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }}
      }}
    }}";

    private static ImmutableArray<Diagnostic> Run(string schemaJson)
    {
        var compilation = CSharpCompilation.Create("EnumDiagnosticsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Diagnostics;
    }

    // ---- T8: JNT2016 duplicate enum member name --------------------------

    [Fact]
    public void TwoMembersFoldingToOneIdentifier_ReportsJnt2016_NamingBoth()
    {
        var diags = Run(SchemaJson("order_status",
            Member("in progress", "InProgress") + "," + Member("in-progress", "InProgress")));

        var diag = Assert.Single(diags, d => d.Id == "JNT2016");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        string message = diag.GetMessage();
        Assert.Contains("'in progress'", message);
        Assert.Contains("'in-progress'", message);
        Assert.Contains("'InProgress'", message);
        Assert.Contains("order_status", message);
    }

    /// <summary>
    /// The empty member value and any value with no alphanumeric characters
    /// both fold to "_" (EnumMemberNaming.Fold), which is the collision most
    /// likely to reach a real MySQL schema.
    /// </summary>
    [Fact]
    public void EmptyAndPunctuationMembers_BothFoldToUnderscore_ReportsJnt2016()
    {
        var diags = Run(SchemaJson("order_status", Member("", "_") + "," + Member("-", "_")));

        var diag = Assert.Single(diags, d => d.Id == "JNT2016");
        Assert.Contains("'_'", diag.GetMessage());
    }

    [Fact]
    public void DistinctMembers_NoJnt2016()
    {
        var diags = Run(SchemaJson("order_status",
            Member("pending", "Pending") + "," + Member("shipped", "Shipped")));

        Assert.DoesNotContain(diags, d => d.Id == "JNT2016");
    }

    /// <summary>
    /// A captured type no column points at is never emitted, so it contributes
    /// no C# names and cannot collide with anything. Reporting it would fail
    /// the build over a type the consumer has no way to reach.
    /// </summary>
    [Fact]
    public void UnreferencedEnumWithCollidingMembers_NoDiagnostic()
    {
        string json = SchemaJson("order_status",
            Member("in progress", "InProgress") + "," + Member("in-progress", "InProgress"))
            .Replace(@"""enumName"": ""order_status""", @"""enumName"": null");

        Assert.DoesNotContain(Run(json), d => d.Id == "JNT2016");
    }

    // ---- T9: JNT2017 enum type name collision ----------------------------

    /// <summary>
    /// The companion-class case named in the plan: a table called
    /// order_status_values folds to "OrderStatusValues", which is exactly the
    /// name the generated Parse/ToWire companion for enum order_status takes.
    /// </summary>
    [Fact]
    public void TableCollidingWithCompanionClass_ReportsJnt2017()
    {
        var diags = Run(SchemaJson("order_status", Member("pending", "Pending"),
            extraTables: Table("order_status_values")));

        var diag = Assert.Single(diags, d => d.Id == "JNT2017");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("OrderStatusValues", diag.GetMessage());
        Assert.Contains("order_status", diag.GetMessage());
    }

    /// <summary>
    /// The enum's own name against an entity accessor: a table named
    /// order_status alongside the enum order_status.
    /// </summary>
    [Fact]
    public void TableCollidingWithEnumTypeName_ReportsJnt2017()
    {
        var diags = Run(SchemaJson("order_status", Member("pending", "Pending"),
            extraTables: Table("order_status")));

        var diag = Assert.Single(diags, d => d.Id == "JNT2017");
        Assert.Contains("OrderStatus", diag.GetMessage());
    }

    /// <summary>
    /// The exception type is emitted once per assembly whenever any enum is,
    /// so a table claiming its name breaks the build regardless of which enum
    /// caused the emission.
    /// </summary>
    [Fact]
    public void TableCollidingWithEmittedExceptionType_ReportsJnt2017()
    {
        var diags = Run(SchemaJson("order_status", Member("pending", "Pending"),
            extraTables: Table("jaunty_q_enum_value_exception")));

        var diag = Assert.Single(diags, d => d.Id == "JNT2017");
        Assert.Contains("JauntyQEnumValueException", diag.GetMessage());
    }

    [Fact]
    public void NoCollision_NoJnt2017()
    {
        var diags = Run(SchemaJson("order_status", Member("pending", "Pending"),
            extraTables: Table("customers")));

        Assert.DoesNotContain(diags, d => d.Id == "JNT2017");
    }

    /// <summary>
    /// Same reason as the JNT2016 case above: an unreferenced type is not
    /// emitted, so the name it would have taken is still free.
    /// </summary>
    [Fact]
    public void UnreferencedEnumCollidingWithTable_NoDiagnostic()
    {
        string json = SchemaJson("order_status", Member("pending", "Pending"),
            extraTables: Table("order_status_values"))
            .Replace(@"""enumName"": ""order_status""", @"""enumName"": null");

        Assert.DoesNotContain(Run(json), d => d.Id == "JNT2017");
    }
}
