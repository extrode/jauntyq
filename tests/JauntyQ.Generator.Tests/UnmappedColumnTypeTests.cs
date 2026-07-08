using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Consumer-gaps-report gap #3: a column whose db type has no case in
/// DialectMapper.MapDbTypeToCSharp silently degrades to `object`. JNT2007
/// surfaces that at build time instead of leaving it to be discovered only
/// by noticing the generated property type.
/// </summary>
public class UnmappedColumnTypeTests
{
    private const string UnmappedColumnSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""widget_status_enum"", ""isNullable"": false }
      }
    }
  }
}";

    private const string MappedColumnsSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""metadata"": { ""name"": ""metadata"", ""dbType"": ""jsonb"", ""isNullable"": true },
        ""origin"": { ""name"": ""origin"", ""dbType"": ""inet"", ""isNullable"": true }
      }
    }
  }
}";

    private const string RowVersionSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""row_version"": { ""name"": ""row_version"", ""dbType"": ""rowversion"", ""isNullable"": false, ""isRowVersion"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult RunGenerator(string schemaJson, params (string path, string sql)[] sqlFiles)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("");
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Data.Common.DbConnection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
        };

        var compilation = CSharpCompilation.Create("UnmappedColumnTypeTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new System.Collections.Generic.List<AdditionalText>();
        foreach (var (path, sql) in sqlFiles)
            texts.Add(new InMemoryAdditionalText(path, sql));
        texts.Add(new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.CreateRange(texts))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [Fact]
    public void ColumnWithUnrecognizedDbType_ReportsJnt2007()
    {
        var result = RunGenerator(UnmappedColumnSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, status from widgets where widget_id = @widget_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2007");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("widgets.status", diag.GetMessage());
        Assert.Contains("widget_status_enum", diag.GetMessage());
    }

    [Fact]
    public void JsonbAndInetColumns_NoJnt2007()
    {
        var result = RunGenerator(MappedColumnsSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, metadata, origin from widgets where widget_id = @widget_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2007");
    }

    [Fact]
    public void RowVersionColumn_NoJnt2007()
    {
        // SQL Server reports rowversion columns as dbType "rowversion" -- not
        // in DialectMapper's switch -- but MapColumnToCSharp special-cases
        // IsRowVersion columns to byte[]? before that switch ever runs. The
        // JNT2007 check must honor the same special case, not just the raw
        // dbType, or every rowversion column false-positives.
        var result = RunGenerator(RowVersionSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, row_version from widgets where widget_id = @widget_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2007");
    }
}
