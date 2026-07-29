using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 013 T10/T11: the generated enums, their <c>{EnumName}Values</c>
/// companions, the shared <c>JauntyQEnumValueException</c>, and reads that go
/// through them.
///
/// Several of these assert a <em>real Roslyn compile</em>, not a parse. Every
/// failure this feature can produce — an undefined enum type, object assigned
/// to an enum property, <c>is null</c> against a struct — is a binding-time
/// error that a syntax check sails straight past.
/// </summary>
public class EnumEmissionTests
{
    private const string PostgresSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""enums"": {
    ""order_status"": {
      ""name"": ""order_status"",
      ""members"": [
        { ""value"": ""pending"", ""csharpName"": ""Pending"" },
        { ""value"": ""in progress"", ""csharpName"": ""InProgress"" },
        { ""value"": ""shipped"", ""csharpName"": ""Shipped"" }
      ]
    }
  },
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""order_status"", ""isNullable"": false, ""enumName"": ""order_status"" },
        ""prior_status"": { ""name"": ""prior_status"", ""dbType"": ""order_status"", ""isNullable"": true, ""enumName"": ""order_status"" }
      }
    }
  }
}";

    /// <summary>
    /// MySQL mints one entity per column ({Table}{Column}), and its member
    /// list carries the three values the inline-enum parser exists for: an
    /// embedded comma, an escaped quote, and the empty string.
    /// </summary>
    private const string MySqlSchemaJson = @"{
  ""dialect"": ""mysql"",
  ""enums"": {
    ""OrdersStatus"": {
      ""name"": ""OrdersStatus"",
      ""members"": [
        { ""value"": ""a,b"", ""csharpName"": ""Ab"" },
        { ""value"": ""it's"", ""csharpName"": ""Its"" },
        { ""value"": """", ""csharpName"": ""_"" }
      ]
    }
  },
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""enum"", ""isNullable"": false, ""enumName"": ""OrdersStatus"" }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string schemaJson, params (string path, string sql)[] sqlFiles)
    {
        var compilation = CSharpCompilation.Create("EnumEmissionTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string AllSources(GeneratorDriverRunResult result)
        => string.Join("\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    private static System.Collections.Generic.List<MetadataReference> BaseReferences()
    {
        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return new()
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Net.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlBulkCopy).Assembly.Location),
        };
    }

    private static void AssertCompiles(GeneratorDriverRunResult result, string what)
    {
        var trees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        var compilation = CSharpCompilation.Create("EnumEmittedCode", trees, BaseReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.True(errors.Count == 0,
            $"{what} failed to compile:\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    // ---- T10: the emitted types ------------------------------------------

    [Fact]
    public void PostgresEnum_IsEmittedWithMembersInDeclarationOrder()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.Contains("public enum OrderStatus", src);
        int pending = src.IndexOf("Pending,", System.StringComparison.Ordinal);
        int inProgress = src.IndexOf("InProgress,", System.StringComparison.Ordinal);
        int shipped = src.IndexOf("Shipped,", System.StringComparison.Ordinal);
        Assert.True(pending >= 0 && pending < inProgress && inProgress < shipped,
            "members must be emitted in the database's own order (enumsortorder)");
    }

    [Fact]
    public void CompanionClass_MapsBothDirectionsOverVerbatimWireValues()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.Contains("public static class OrderStatusValues", src);
        Assert.Contains("public static OrderStatus Parse(string value, string column)", src);
        Assert.Contains("public static string ToWire(OrderStatus value)", src);
        // The wire value, not the C# member name -- "in progress" must survive
        // the round trip verbatim or every read of that row throws.
        Assert.Contains("case \"in progress\":", src);
        Assert.Contains("return \"in progress\";", src);
    }

    /// <summary>
    /// The mandate: JauntyQ.Runtime must not gain a helper generated code
    /// calls unconditionally, so the conversion is reflection-free emitted
    /// switches rather than Enum.Parse or an attribute lookup.
    /// </summary>
    [Fact]
    public void CompanionClass_UsesNoReflection()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.DoesNotContain("Enum.Parse", src);
        Assert.DoesNotContain("GetCustomAttribute", src);
        Assert.DoesNotContain("typeof(OrderStatus)", src);
    }

    [Fact]
    public void ExceptionType_IsEmittedOnceWithColumnValueAndEnumType()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.Contains("public sealed class JauntyQEnumValueException : InvalidOperationException", src);
        Assert.Contains("public string EnumType { get; }", src);
        Assert.Contains("public string Column { get; }", src);
        Assert.Contains("public string Value { get; }", src);
        // Two enum types in one assembly must not emit it twice (CS0101).
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            src, @"class JauntyQEnumValueException"));
    }

    [Fact]
    public void MySqlEnum_MembersWithCommaQuoteAndEmptyValue_AreEmittedVerbatim()
    {
        string src = AllSources(Run(MySqlSchemaJson));

        Assert.Contains("public enum OrdersStatus", src);
        Assert.Contains("case \"a,b\":", src);
        Assert.Contains("case \"it's\":", src);
        Assert.Contains("case \"\":", src);
    }

    /// <summary>
    /// A captured type no column references is not emitted — it stays in the
    /// snapshot for drift detection, but adding a public type nothing can
    /// reach is surface for nothing.
    /// </summary>
    [Fact]
    public void UnreferencedEnum_IsNotEmitted()
    {
        string json = PostgresSchemaJson.Replace(@"""enumName"": ""order_status""", @"""enumName"": null");
        string src = AllSources(Run(json));

        Assert.DoesNotContain("public enum OrderStatus", src);
        Assert.DoesNotContain("class JauntyQEnumValueException", src);
    }

    [Fact]
    public void SnapshotWithNoEnums_EmitsNoEnumFile()
    {
        string json = @"{
  ""dialect"": ""postgres"",
  ""tables"": { ""orders"": { ""name"": ""orders"", ""columns"": {
    ""id"": { ""name"": ""id"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true } } } }
}";
        var result = Run(json);

        Assert.DoesNotContain(result.Results[0].GeneratedSources, s => s.HintName == "Enums.g.cs");
    }

    // ---- T11: reads ------------------------------------------------------

    [Fact]
    public void NonNullableEnumColumn_ReadsThroughParseWithColumnIdentity()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.Contains("OrderStatusValues.Parse(reader.GetString(1), \"orders.status\")", src);
    }

    /// <summary>
    /// FR-006: the nullable guard must survive. Without the IsDBNull wrapper,
    /// GetString on a NULL enum column throws SqlNullValueException instead of
    /// yielding null.
    /// </summary>
    [Fact]
    public void NullableEnumColumn_KeepsIsDBNullGuard()
    {
        string src = AllSources(Run(PostgresSchemaJson));

        Assert.Contains("reader.IsDBNull(2) ? default(OrderStatus?) : OrderStatusValues.Parse(reader.GetString(2), \"orders.prior_status\")", src);
    }

    [Fact]
    public void QueryProjection_ReadsEnumThroughParse()
    {
        var result = Run(PostgresSchemaJson,
            ("db/Orders/GetStatuses.sql", "select id, status from orders where id = @id"));
        string src = AllSources(result);

        Assert.Contains("OrderStatusValues.Parse(reader.GetString(1), \"status\")", src);
    }

    // ---- The real compiles ------------------------------------------------

    [Fact]
    public void PostgresEnumSchema_GeneratedCode_CompilesClean()
        => AssertCompiles(Run(PostgresSchemaJson,
            ("db/Orders/GetStatuses.sql", "select id, status, prior_status from orders where id = @id")),
            "postgres enum schema");

    [Fact]
    public void MySqlEnumSchema_GeneratedCode_CompilesClean()
        => AssertCompiles(Run(MySqlSchemaJson,
            ("db/Orders/GetStatuses.sql", "select id, status from orders where id = @id")),
            "mysql enum schema");

    /// <summary>
    /// The T7 clause that could not be proven until the enum type existed: a
    /// non-nullable enum column drives the bulk-copy path, which emits
    /// <c>if (row.Status is null)</c> against a struct unless
    /// IsNonNullableValueType recognises the enum. That is CS0037/CS0023, and
    /// only a real compile sees it.
    /// </summary>
    [Theory]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void NonNullableEnumColumn_BulkAndCrudPaths_CompileClean(string dialect)
    {
        string json = dialect switch
        {
            "postgres" => PostgresSchemaJson,
            "mysql" => MySqlSchemaJson,
            _ => MySqlSchemaJson.Replace(@"""dialect"": ""mysql""", @"""dialect"": ""sqlite""")
        };

        AssertCompiles(Run(json), $"{dialect} non-nullable enum column");
    }
}
