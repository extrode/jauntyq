using JauntyQ.Analysis;
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
[Trait("Category", "AuditRegression")]
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

    private const string NullableUnmappedColumnSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""employee"": {
      ""name"": ""employee"",
      ""columns"": {
        ""business_entity_id"": { ""name"": ""business_entity_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""organization_node"": { ""name"": ""organization_node"", ""dbType"": ""hierarchyid"", ""isNullable"": true }
      }
    }
  }
}";

    private const string SqlServerNonUdtUnmappedSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""payload"": { ""name"": ""payload"", ""dbType"": ""sql_variant"", ""isNullable"": true }
      }
    }
  }
}";

    private const string CapturedEnumColumnSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""enums"": {
    ""widget_status_enum"": {
      ""name"": ""widget_status_enum"",
      ""members"": [
        { ""value"": ""new"", ""csharpName"": ""New"" },
        { ""value"": ""done"", ""csharpName"": ""Done"" }
      ]
    }
  },
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""status"": { ""name"": ""status"", ""dbType"": ""widget_status_enum"", ""isNullable"": false, ""enumName"": ""widget_status_enum"" }
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

    // Found live by the canonical Pagila suite (2026-08-01): film.rating is a
    // captured mpaa_rating enum -- MapColumnToCSharp resolves it to the
    // generated C# enum via the column's EnumName link (spec 013) -- yet the
    // JNT2007 detector consulted only the raw dbType switch and flagged every
    // enum-typed column as "degrades to 'object'", which the generated code
    // demonstrably does not do. The unlinked sibling case (same dbType, no
    // enumName / no captured enum) must keep warning; that is pinned by
    // ColumnWithUnrecognizedDbType_ReportsJnt2007 above.
    [Fact]
    public void CapturedEnumColumn_NoJnt2007_AndPropertyIsGeneratedEnum()
    {
        var result = RunGenerator(CapturedEnumColumnSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, status from widgets where widget_id = @widget_id"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2007");

        string allSources = string.Join("\n\n", result.Results[0].GeneratedSources
            .Select(s => $"// ---- {s.HintName} ----\n{s.SourceText}"));
        Assert.Contains("WidgetStatusEnum Status", allSources);
        Assert.DoesNotContain("public object Status", allSources);
    }

    // Found live by canonical Pagila (2026-08-01): a NOT NULL column of an
    // unmapped type maps to bare `object`, and the row POCO emitted it with
    // no `required` modifier and no initializer -- leaking CS8618 out of
    // GENERATED code into every consuming build. Strings and byte[] already
    // carried `required`; the non-nullable object fallback must too.
    [Fact]
    public void NonNullableUnmappedColumn_RowPropertyIsRequired()
    {
        var result = RunGenerator(UnmappedColumnSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, status from widgets where widget_id = @widget_id"));

        string allSources = string.Join("\n\n", result.Results[0].GeneratedSources
            .Select(s => s.SourceText.ToString()));
        Assert.Contains("public required object Status", allSources);
        Assert.DoesNotContain("public object Status", allSources);
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

    // Round 13 (2026-07-17), AUD-R13-01: a NULLABLE column of an unrecognized
    // db type -- e.g. SQL Server "hierarchyid", which really is used, live,
    // by AdventureWorksLite's HumanResources.Employee.OrganizationNode
    // (samples/JauntyQ.AdventureWorksLite.SqlServer.Tests/db/schema/jaunty.schema.json)
    // -- used to generate a non-nullable-annotated `object` property AND skip
    // the `reader.IsDBNull(...)` guard entirely (CodeEmitter.Part9's
    // GetReaderCall infers "is this nullable" from whether the type string
    // ends in "?", which the unconditional `_ => "object"` fallback in
    // DialectMapper.MapDbTypeToCSharp never did). A genuine SQL NULL for such
    // a column therefore came through as the raw `System.DBNull.Value`
    // sentinel instead of C# `null`, silently breaking the null-means-no-value
    // contract every other nullable column honors -- with no diagnostic,
    // since JNT2007 only warns about the type itself being unmapped, not
    // about this null-handling asymmetry. Fixed by making the fallback arm
    // append "?" for a nullable column, same as every other arm.
    [Fact]
    public void NullableUnmappedColumn_PropertyIsNullableObject_AndReaderGuardsIsDBNull()
    {
        var result = RunGenerator(NullableUnmappedColumnSchemaJson,
            ("db/tables/Employee/GetById.sql", "select business_entity_id, organization_node from employee where business_entity_id = @business_entity_id"));

        var rowSource = Assert.Single(result.Results[0].GeneratedSources,
            s => s.SourceText.ToString().Contains("class EmployeeRow")).SourceText.ToString();

        Assert.Contains("public object? OrganizationNode { get; set; }", rowSource);
        Assert.Contains("OrganizationNode = reader.IsDBNull(1) ? default(object?) : reader.GetValue(1)", rowSource);
        // The old, buggy shape (non-nullable property, unguarded GetValue) must not reappear.
        Assert.DoesNotContain("public object OrganizationNode { get; set; }", rowSource);
    }

    [Fact]
    public void NullableUnmappedColumn_StillReportsJnt2007()
    {
        // Regression guard for the companion IsUnmappedDbType fix: once the
        // mapper's fallback started returning "object?" (not just "object")
        // for nullable columns, the JNT2007 detector's `== "object"` string
        // comparison would otherwise stop matching and silently swallow the
        // diagnostic for every nullable unmapped-type column.
        var result = RunGenerator(NullableUnmappedColumnSchemaJson,
            ("db/tables/Employee/GetById.sql", "select business_entity_id, organization_node from employee where business_entity_id = @business_entity_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2007");
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
        Assert.Contains("employee.organization_node", diag.GetMessage());
        Assert.Contains("hierarchyid", diag.GetMessage());
    }

    // Round 14 (2026-07-17): hierarchyid (and geography/geometry) are SQL
    // Server CLR UDT columns that fall through to the same "object"/
    // "object?" JNT2007 path as any other unmapped type, but reading one at
    // RUNTIME via the generated fallback's reader.GetValue(i) call actually
    // THROWS (System.IO.FileNotFoundException on Microsoft.SqlServer.Types)
    // -- confirmed live this round against AdventureWorksLite's real
    // HumanResources.Employee.OrganizationNode column
    // (samples/JauntyQ.AdventureWorksLite.SqlServer.Tests). The old generic
    // "degrades to object... cast at the call site" message didn't warn
    // about this; a call-site cast can't fix a reader-level crash. This
    // locks the sharpened message content in place.
    [Fact]
    public void SqlServerClrUdtColumn_Jnt2007MessageWarnsAboutRuntimeCrashRisk()
    {
        var result = RunGenerator(NullableUnmappedColumnSchemaJson,
            ("db/tables/Employee/GetById.sql", "select business_entity_id, organization_node from employee where business_entity_id = @business_entity_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2007");
        var message = diag.GetMessage();
        Assert.Contains("CLR user-defined type", message);
        Assert.Contains("FileNotFoundException", message);
        Assert.Contains("Microsoft.SqlServer.Types", message);
        Assert.Contains("CAST", message);
    }

    [Fact]
    public void NonSqlServerUnmappedColumn_Jnt2007MessageDoesNotMentionClrUdtCrash()
    {
        // Sibling guard, first half: the CLR-UDT-specific message text must
        // only appear for sqlserver-dialect columns matching a known CLR UDT
        // type name -- postgres's widget_status_enum (or any other
        // dialect's unmapped type) must keep the original generic message
        // unchanged.
        var result = RunGenerator(UnmappedColumnSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, status from widgets where widget_id = @widget_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2007");
        var message = diag.GetMessage();
        Assert.DoesNotContain("Microsoft.SqlServer.Types", message);
        Assert.Contains("Add a case for it or accept the untyped column and cast at the call site.", message);
    }

    [Fact]
    public void SqlServerNonClrUdtUnmappedColumn_Jnt2007MessageStaysGeneric()
    {
        // Sibling guard, second half: sqlserver dialect alone isn't enough
        // to trigger the CLR-UDT-specific message -- only a column whose
        // dbType is one of the known CLR UDT names (hierarchyid/geography/
        // geometry) does. sql_variant is unmapped on SQL Server too, but is
        // not a CLR UDT and does not crash the same way, so it keeps the
        // original generic text.
        var result = RunGenerator(SqlServerNonUdtUnmappedSchemaJson,
            ("db/tables/Widget/GetById.sql", "select widget_id, payload from widgets where widget_id = @widget_id"));

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT2007");
        var message = diag.GetMessage();
        Assert.DoesNotContain("Microsoft.SqlServer.Types", message);
        Assert.Contains("Add a case for it or accept the untyped column and cast at the call site.", message);
    }
}
