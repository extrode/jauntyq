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
    // AUD-R64-01: PostgreSQL lower-cases every unquoted identifier it parses,
    // so AutoCrud now (correctly) skips a snapshot table/column name
    // containing an uppercase letter under the postgres dialect -- a real
    // extractor never hands back mixed-case names for an unquoted-created
    // table in the first place (confirmed empirically: every shipped
    // postgres-dialect sample snapshot is already all-lowercase). Use
    // realistic lowercase/snake_case names for postgres specifically so this
    // shared fixture still exercises AutoCrud-driven synthesis; other
    // dialects are untouched by that check and keep their original PascalCase
    // names.
    private static string SchemaFor(string dialect)
    {
        bool pg = string.Equals(dialect, "postgres", System.StringComparison.OrdinalIgnoreCase);
        string table = pg ? "widgets" : "Widgets";
        string idCol = pg ? "widget_id" : "WidgetId";
        string nameCol = pg ? "name" : "Name";
        string noteCol = pg ? "note" : "Note";
        return $@"{{
  ""dialect"": ""{dialect}"",
  ""tables"": {{
    ""{table}"": {{
      ""name"": ""{table}"",
      ""columns"": {{
        ""{idCol}"": {{ ""name"": ""{idCol}"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""{nameCol}"": {{ ""name"": ""{nameCol}"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }},
        ""{noteCol}"": {{ ""name"": ""{noteCol}"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 40 }}
      }}
    }}
  }}
}}";
    }

    private static string AllSources(GeneratorDriverRunResult result) =>
        string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

    [Fact]
    public void BulkInsert_IsSynthesized_TakesIEnumerableAndExcludesIdentity()
    {
        var src = AllSources(Run());
        Assert.Contains("int BulkInsert(IEnumerable<", src);
        Assert.Contains("Task<int> BulkInsertAsync(", src);
        Assert.Contains("BeginTransaction", src);
        // Identity WidgetId is excluded from the insert column list.
        Assert.Contains("INSERT INTO Widgets (Name)", src);
        Assert.DoesNotContain("INSERT INTO Widgets (WidgetId", src);
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
        Assert.Contains("COPY widgets (name, note) FROM STDIN (FORMAT BINARY)", src);
        Assert.Contains(".Complete();", src);
        Assert.Contains("await __importer.CompleteAsync(", src);
        // Postgres writes columns directly; no IDataReader adapter is emitted.
        Assert.DoesNotContain("BulkReader", src);
    }

    [Fact]
    public void SqlServer_UsesSqlBulkCopy_OverSharedReaderAdapter()
    {
        var src = AllSources(Run(SchemaFor("sqlserver")));
        Assert.Contains("SqlBulkCopy", src);
        Assert.Contains("__bulkCopy.DestinationTableName = \"Widgets\"", src);
        Assert.Contains("__bulkCopy.ColumnMappings.Add(\"Name\", \"Name\")", src);
        // Shared AOT-safe DbDataReader adapter, ordinal-based, reflection-free.
        Assert.Contains(": DbDataReader", src);
        Assert.Contains("public int RowsRead", src);
    }

    [Fact]
    public void MySql_UsesMySqlBulkCopy_OverSharedReaderAdapter_AndDocsLocalInfile()
    {
        var src = AllSources(Run(SchemaFor("mysql")));
        Assert.Contains("MySqlBulkCopy", src);
        Assert.Contains("__bulkCopy.DestinationTableName = \"Widgets\"", src);
        // Explicit source-ordinal -> destination-name mapping (identity excluded).
        Assert.Contains("new MySqlBulkCopyColumnMapping(0, \"Name\")", src);
        Assert.Contains("new MySqlBulkCopyColumnMapping(1, \"Note\")", src);
        Assert.Contains(": DbDataReader", src);
        Assert.Contains("public int RowsRead", src);
        // The AllowLoadLocalInfile requirement must be documented on the method.
        Assert.Contains("AllowLoadLocalInfile=true", src);
    }

    // A NOT NULL "time with time zone" column maps to non-nullable
    // System.DateTimeOffset (task #26/#30). IsNonNullableValueType (CodeEmitter.
    // Part4.cs) didn't know DateTimeOffset, so EmitBulkInsertBodyPostgres took
    // the reference/nullable branch and emitted `if (row.ObservedAt is null)`
    // against a non-nullable struct property -- CS0037 ("cannot convert null
    // to 'System.DateTimeOffset' because it is a non-nullable value type"),
    // invisible to a syntax-only parse check since object/struct nullability
    // is a binding-time error, not a syntax error.
    // AUD-R64-01: lowercase/snake_case, matching what a real postgres
    // extraction actually produces for an unquoted-created table (see the
    // note on SchemaFor above) -- ToPascalCase folds "observed_at" to the
    // same "ObservedAt" C# property name either way, so the row-POCO-facing
    // assertions below are unaffected by this rename.
    private const string PostgresDateTimeOffsetSchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""widget_id"": { ""name"": ""widget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""observed_at"": { ""name"": ""observed_at"", ""dbType"": ""time with time zone"", ""isNullable"": false }
      }
    }
  }
}";

    [Fact]
    public void Postgres_NonNullableDateTimeOffsetColumn_BulkInsert_TakesUnconditionalWritePath()
    {
        var src = AllSources(Run(PostgresDateTimeOffsetSchemaJson));

        // Non-nullable DateTimeOffset must take IsNonNullableValueType's
        // unconditional-write branch, not the reference/nullable
        // "if (row.Prop is null)" branch.
        Assert.Contains("__importer.Write(row.ObservedAt)", src);
        Assert.DoesNotContain("if (row.ObservedAt is null)", src);
    }

    [Fact]
    public void Postgres_NonNullableDateTimeOffsetColumn_BulkInsert_CompilesCleanAgainstRealNpgsql()
    {
        var result = Run(PostgresDateTimeOffsetSchemaJson);

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create("BulkInsertDateTimeOffsetAssembly",
            allTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0,
            "generated bulk insert code with a non-nullable DateTimeOffset column failed to compile:\n" +
            string.Join("\n", errors.Select(e => e.ToString())));
    }

    // SQL Server tinyint is provider storage type byte (DialectMapper maps it
    // to "byte" only when dialect == "sqlserver"; every other dialect, and
    // null, maps it to "short" -- see DialectMapper.MapDbTypeToCSharp). The
    // shared BulkReaderAdapter (CodeEmitter.Part13.cs, feeding SqlBulkCopy)
    // and the two portable body emitters (CodeEmitter.Part12/13.cs) each call
    // DialectMapper.MapColumnToCSharp independently; EmitBulkInsert's EmitOne/
    // adapter callers (CodeEmitter.Part11.cs) previously omitted the dialect
    // argument on 5 call sites, so tinyint columns always fell through to
    // "short" regardless of the schema's actual dialect -- a mismatch against
    // the row POCO's correctly-dialect-aware "byte" property (CodeEmitter.
    // Part5.cs) that a syntax-only parse can't catch, since both "short" and
    // "byte" are valid C#; only GetFieldType's `typeof(...)` argument reveals
    // the wrong type was selected.
    private const string SqlServerTinyIntSchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""Widgets"": {
      ""name"": ""Widgets"",
      ""columns"": {
        ""WidgetId"": { ""name"": ""WidgetId"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""Flags"": { ""name"": ""Flags"", ""dbType"": ""tinyint"", ""isNullable"": false }
      }
    }
  }
}";

    [Fact]
    public void SqlServer_TinyIntColumn_BulkReaderAdapter_UsesByte_NotShort()
    {
        var src = AllSources(Run(SqlServerTinyIntSchemaJson));

        // The row POCO property (CodeEmitter.Part5.cs, always dialect-aware)
        // must be byte.
        Assert.Contains("public byte Flags", src);

        // The shared BulkReaderAdapter's GetFieldType must agree: byte, not
        // the pre-fix default of short.
        Assert.Contains("return typeof(byte);", src);
        Assert.DoesNotContain("return typeof(short);", src);
    }

    [Fact]
    public void Sqlite_KeepsPortableLoop_NoProviderTypes()
    {
        var src = AllSources(Run(SchemaFor("sqlite")));
        // sqlite is unchanged: portable prepared-command loop in a transaction.
        Assert.Contains("BeginTransaction", src);
        Assert.Contains("INSERT INTO Widgets (Name, Note)", src);
        Assert.DoesNotContain("SqlBulkCopy", src);
        Assert.DoesNotContain("BeginBinaryImport", src);
        Assert.DoesNotContain("MySqlBulkCopy", src);
        Assert.DoesNotContain("BulkReader", src);
    }
}
