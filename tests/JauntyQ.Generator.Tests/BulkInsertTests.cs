using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Generator coverage for the auto-synthesized BulkInsert(IEnumerable&lt;Row&gt;):
/// it takes a sequence, wraps a transaction, excludes identity columns, and the
/// emitted code parses as valid C#.
/// </summary>
public class BulkInsertTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""WidgetId"": { ""name"": ""WidgetId"", ""dbType"": ""integer"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""Name"": { ""name"": ""Name"", ""dbType"": ""text"", ""isNullable"": false, ""maxLength"": 40 }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run(string schemaJson = SchemaJson)
    {
        var compilation = CSharpCompilation.Create("BulkTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // autoCrud ON so the BulkInsert synthetic is generated.
        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    // Same Widgets table as SchemaJson but with a nullable second column and a
    // swappable dialect, so the provider-native fast paths (postgres/sqlserver/
    // mysql) get exercised with both a non-nullable and a nullable column.
    private static string SchemaFor(string dialect) => $@"{{
  ""dialect"": ""{dialect}"",
  ""tables"": {{
    ""Widgets"": {{
      ""name"": ""Widgets"",
      ""columns"": {{
        ""WidgetId"": {{ ""name"": ""WidgetId"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""Name"": {{ ""name"": ""Name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }},
        ""Note"": {{ ""name"": ""Note"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 40 }}
      }}
    }}
  }}
}}";

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void BulkInsert_IsSynthesized_TakesIEnumerableAndExcludesIdentity()
    {
        var src = AllSources(Run());
        Assert.Contains("int BulkInsert(System.Collections.Generic.IEnumerable<", src);
        Assert.Contains("System.Threading.Tasks.Task<int> BulkInsertAsync(", src);
        Assert.Contains("BeginTransaction", src);
        // Identity WidgetId is excluded from the insert column list.
        Assert.Contains("insert into Widgets (Name)", src);
        Assert.DoesNotContain("insert into Widgets (WidgetId", src);
    }

    [Fact]
    public void GeneratedBulkCode_ParsesClean()
    {
        var result = Run();
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"bulk code broke in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    [InlineData("mysql")]
    [InlineData("sqlite")]
    public void GeneratedBulkCode_ParsesClean_PerDialect(string dialect)
    {
        var result = Run(SchemaFor(dialect));
        foreach (var gen in result.Results[0].GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gen.SourceText.ToString());
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.True(errors.Count == 0,
                $"bulk code broke ({dialect}) in {gen.HintName}: {string.Join("; ", errors.Select(e => e.GetMessage()))}");
        }
    }

    [Fact]
    public void Postgres_UsesBinaryCopy_NoReaderAdapter()
    {
        var src = AllSources(Run(SchemaFor("postgres")));
        Assert.Contains("BeginBinaryImport", src);
        Assert.Contains("COPY Widgets (Name, Note) FROM STDIN (FORMAT BINARY)", src);
        Assert.Contains(".Complete();", src);
        Assert.Contains("await __importer.CompleteAsync(", src);
        // Postgres writes columns directly; no IDataReader adapter is emitted.
        Assert.DoesNotContain("BulkReader", src);
    }

    [Fact]
    public void SqlServer_UsesSqlBulkCopy_OverSharedReaderAdapter()
    {
        var src = AllSources(Run(SchemaFor("sqlserver")));
        Assert.Contains("global::Microsoft.Data.SqlClient.SqlBulkCopy", src);
        Assert.Contains("__bulkCopy.DestinationTableName = \"Widgets\"", src);
        Assert.Contains("__bulkCopy.ColumnMappings.Add(\"Name\", \"Name\")", src);
        // Shared AOT-safe DbDataReader adapter, ordinal-based, reflection-free.
        Assert.Contains(": System.Data.Common.DbDataReader", src);
        Assert.Contains("public int RowsRead", src);
    }

    [Fact]
    public void MySql_UsesMySqlBulkCopy_OverSharedReaderAdapter_AndDocsLocalInfile()
    {
        var src = AllSources(Run(SchemaFor("mysql")));
        Assert.Contains("global::MySqlConnector.MySqlBulkCopy", src);
        Assert.Contains("__bulkCopy.DestinationTableName = \"Widgets\"", src);
        // Explicit source-ordinal -> destination-name mapping (identity excluded).
        Assert.Contains("new global::MySqlConnector.MySqlBulkCopyColumnMapping(0, \"Name\")", src);
        Assert.Contains("new global::MySqlConnector.MySqlBulkCopyColumnMapping(1, \"Note\")", src);
        Assert.Contains(": System.Data.Common.DbDataReader", src);
        Assert.Contains("public int RowsRead", src);
        // The AllowLoadLocalInfile requirement must be documented on the method.
        Assert.Contains("AllowLoadLocalInfile=true", src);
    }

    [Fact]
    public void Sqlite_KeepsPortableLoop_NoProviderTypes()
    {
        var src = AllSources(Run(SchemaFor("sqlite")));
        // sqlite is unchanged: portable prepared-command loop in a transaction.
        Assert.Contains("BeginTransaction", src);
        Assert.Contains("insert into Widgets (Name, Note)", src);
        Assert.DoesNotContain("SqlBulkCopy", src);
        Assert.DoesNotContain("BeginBinaryImport", src);
        Assert.DoesNotContain("MySqlBulkCopy", src);
        Assert.DoesNotContain("BulkReader", src);
    }
}
