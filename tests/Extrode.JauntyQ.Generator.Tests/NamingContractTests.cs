using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class NamingContractTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""widgets"": {
      ""name"": ""widgets"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 100 }
      }
    },
    ""categories"": {
      ""name"": ""categories"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""title"": { ""name"": ""title"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 100 }
      }
    },
    ""status"": {
      ""name"": ""status"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""label"": { ""name"": ""label"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 100 }
      }
    }
  }
}";

    private static (GeneratorDriverRunResult Result, Compilation Output) Run(params (string Path, string Sql)[] files)
    {
        var compilation = CSharpCompilation.Create("NamingContractTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = files
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Sql))
            .Append(new InMemoryAdditionalText("db/schema/jaunty.schema.json", SchemaJson))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts)
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private static MethodDeclarationSyntax? Method(Compilation output, string entity, string method) =>
        output.SyntaxTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.Text == entity && c.Parent is not ClassDeclarationSyntax)
            .SelectMany(c => c.Members.OfType<MethodDeclarationSyntax>())
            .FirstOrDefault(m => m.Identifier.Text == method);

    private static string ReturnType(Compilation output, string entity, string method)
    {
        var m = Method(output, entity, method);
        Assert.True(m != null, $"no method {entity}.{method} was generated");
        return m!.ReturnType.ToString();
    }

    private static readonly (string, string)[] Layout =
    {
        ("db/Widgets/GetNames.sql", "select widgets.name from widgets"),
        ("db/tables/Categories/GetAll.sql", "select categories.id, categories.title from categories"),
        ("db/Widgets/Admin/ListAll.sql", "select widgets.id, widgets.name from widgets"),
        ("db/Status/GetAll.sql", "select status.id, status.label from status"),
        ("db/Ping.sql", "select widgets.id from widgets where widgets.id = @id"),
    };

    [Fact]
    public void TheFolderNamesTheEntity_AndTheFileNamesTheMethod()
    {
        var (result, output) = Run(Layout);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2004");
        Assert.NotNull(Method(output, "Widgets", "GetNames"));
    }

    [Fact]
    public void ALeadingTablesFolder_IsSkipped()
    {
        var (_, output) = Run(Layout);

        Assert.NotNull(Method(output, "Categories", "GetAll"));
        Assert.Null(Method(output, "tables", "GetAll"));
    }

    [Fact]
    public void FoldersBelowTheEntity_DoNotChangeIt()
    {
        var (_, output) = Run(Layout);

        Assert.NotNull(Method(output, "Widgets", "ListAll"));
        Assert.Null(Method(output, "Admin", "ListAll"));
    }

    [Fact]
    public void AFileDirectlyUnderTheRoot_BelongsToQueries()
    {
        var (_, output) = Run(Layout);

        Assert.NotNull(Method(output, "Queries", "Ping"));
    }

    [Fact]
    public void AFullRowSelect_ReturnsTheSingularisedTableType()
    {
        var (_, output) = Run(Layout);

        Assert.Contains("Category", ReturnType(output, "Categories", "GetAll"));
        Assert.Contains("Widget", ReturnType(output, "Widgets", "ListAll"));
        Assert.DoesNotContain("Result.", ReturnType(output, "Widgets", "ListAll"));
    }

    [Fact]
    public void ATableNameSingularisationLeavesUnchanged_GetsTheRowSuffix()
    {
        var (_, output) = Run(Layout);

        Assert.Contains("StatusRow", ReturnType(output, "Status", "GetAll"));
    }

    [Fact]
    public void ACustomProjection_ReturnsTheNestedResultTypeNamedForTheMethod()
    {
        var (_, output) = Run(Layout);

        Assert.Contains("Result.GetNames", ReturnType(output, "Widgets", "GetNames"));
    }

    [Theory]
    [InlineData("db/Widgets/2Fast.sql")]
    [InlineData("db/Wid-gets/GetAll.sql")]
    [InlineData("db/class/GetAll.sql")]
    [InlineData("db/Widgets/int.sql")]
    public void ANameThatIsNotALegalCSharpIdentifier_IsJNT2004(string path)
    {
        var (result, _) = Run((path, "select widgets.id, widgets.name from widgets"), ("db/Other/Get.sql", "select widgets.id from widgets where widgets.id = @id"));

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2004");
    }
}
