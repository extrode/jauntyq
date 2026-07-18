using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// AUD-R60-01: an entity accessor or row POCO literally named <c>System</c>
/// (table "system", or "systems" via <c>Inflector.RowTypeName</c>
/// singularization) shadows the BCL <c>System</c> NAMESPACE ROOT -- a third,
/// distinct collision shape after AUD-R52-01 (declaration collision with a
/// generator-owned type) and AUD-R50-03 (simple-name shadowing of a bare
/// emitted type reference): C# namespace-member lookup resolves the first
/// segment of any dotted <c>System.Xxx</c> name against in-namespace types
/// first, so every dotted <c>System.</c> reference in every file compiled
/// into <c>JauntyQ.Generated</c> breaks at once. The always-emitted
/// <c>JauntyQShapeGuard</c> (schema-independent post-initialization output,
/// so it cannot route through <c>CodeEmitter.TypeRef</c>) carries four such
/// references, which is why -- unlike the round-59 residual names, several of
/// which compiled fine on some dialects -- there is NO dialect or feature
/// combination under which a "system" table compiles. That unconditionality
/// is what makes the JNT2006 reserved-name Error the right mechanism here
/// (zero false-positive risk), matching the round-52/55 precedent rather
/// than round 59's TypeRef qualification.
///
/// Pre-fix evidence (real generator run + real Roslyn compile, zero JauntyQ
/// diagnostics): CS0426 "The type name 'Data'/'InvalidOperationException'/
/// 'Net' does not exist in the type 'System'", CS0117 "'System' does not
/// contain a definition for 'StringComparison'", plus a CS1503 cascade at
/// every entity's JauntyQShapeGuard.Validate call site.
/// </summary>
public class SystemReservedNameCollisionTests
{
    private static string TableSchema(string dialect, string tableName, bool withInet = false)
    {
        string inetCol = withInet
            ? @",
        ""addr"": { ""name"": ""addr"", ""dbType"": ""inet"", ""isNullable"": false }"
            : "";
        return $@"{{
  ""dialect"": ""{dialect}"",
  ""tables"": {{
    ""{tableName}"": {{
      ""name"": ""{tableName}"",
      ""columns"": {{
        ""id"": {{ ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true }},
        ""label"": {{ ""name"": ""label"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 40 }}{inetCol}
      }}
    }}
  }}
}}";
    }

    private static (GeneratorDriverRunResult result, List<string> errors, string allSources) RunAutoCrudAndCompile(string schemaJson)
    {
        var compilation0 = CSharpCompilation.Create("SystemReservedNameSeed",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation0, out _, out _);
        var result = driver.GetRunResult();

        var allTrees = result.Results[0].GeneratedSources
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText.ToString()))
            .ToList();
        string allSources = string.Join("\n\n", result.Results[0].GeneratedSources.Select(s => s.SourceText.ToString()));

        string runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Data.Common.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Collections.NonGeneric.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Linq.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Threading.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Net.Primitives.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(typeof(Npgsql.NpgsqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Data.SqlClient.SqlParameter).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MySqlConnector.MySqlParameter).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create("SystemReservedNameCompile",
            allTrees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();
        return (result, errors, allSources);
    }

    private static List<string> Jnt2006Messages(GeneratorDriverRunResult result) =>
        result.Results[0].Diagnostics
            .Where(d => d.Id == "JNT2006")
            .Select(d => d.GetMessage())
            .ToList();

    // Table "system": entityPascal "System" directly -- the entity-level
    // case. The shape guard is emitted on every dialect unconditionally, so
    // the collision (and therefore the diagnostic) must fire on all four.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("mysql")]
    [InlineData("sqlserver")]
    public void EntityNamedSystem_ReportsJNT2006_NamingTheReservedType(string dialect)
    {
        var (result, _, allSources) = RunAutoCrudAndCompile(TableSchema(dialect, "system"));

        // The collision trigger is real: the entity accessor really is
        // declared with the shadowing name.
        Assert.Contains("public partial class System", allSources);

        Assert.Contains(Jnt2006Messages(result), m => m.Contains("'System'") && m.Contains("reserved"));
    }

    // Table "systems" (plural): entityPascal "Systems" (no entity-level
    // collision), but Inflector.RowTypeName singularizes it to "System" --
    // the row-POCO-level case, same shape as AUD-R52-01's "jaunty_dbs" test.
    [Fact]
    public void RowPocoSingularizingToSystem_ReportsJNT2006_NamingTheReservedType()
    {
        var (result, _, allSources) = RunAutoCrudAndCompile(TableSchema("sqlite", "systems"));

        Assert.Contains("public class System", allSources);

        Assert.Contains(Jnt2006Messages(result), m => m.Contains("'System'") && m.Contains("reserved"));
    }

    // The reservation is not over-broad, and the conditionally-emitted dotted
    // System.* references it also protects (here CodeEmitter.Part9.cs's
    // "(System.Net.IPAddress)reader.GetValue(...)" cast plus the verbatim
    // System.Net.IPAddress property type for a postgres inet column) really
    // do compile cleanly for any benign table name: only a type literally
    // named "System" can break a fully-qualified type-position reference.
    [Fact]
    public void BenignSchemaWithInetColumn_NoJNT2006_AndGeneratedCodeCompiles()
    {
        var (result, errors, allSources) = RunAutoCrudAndCompile(TableSchema("postgres", "widgets", withInet: true));

        Assert.Contains("(System.Net.IPAddress)reader.GetValue(", allSources);
        Assert.Empty(Jnt2006Messages(result));
        Assert.True(errors.Count == 0,
            "generated code for a benign 'widgets' table with an inet column failed to compile:\n" +
            string.Join("\n", errors.Take(8)));
    }
}
