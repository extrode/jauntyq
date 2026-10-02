using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class SnapshotKeyCaseTests
{
    [Theory]
    [InlineData("Customers", "ID")]
    [InlineData("CUSTOMERS", "id")]
    public void ASnapshotKeyDifferingInCaseFromItsName_StillGetsAutoCrud(string tableKey, string columnKey)
    {
        string json = "{ \"dialect\": \"postgres\", \"tables\": { \"" + tableKey + "\": { \"name\": \"customers\", \"columns\": { " +
            "\"" + columnKey + "\": { \"name\": \"id\", \"dbType\": \"int\", \"isNullable\": false, \"isPrimaryKey\": true }, " +
            "\"c\": { \"name\": \"c\", \"dbType\": \"int\", \"isNullable\": true } } } } }";
        var compilation = CSharpCompilation.Create("SnapshotKeyCaseAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/schema/jaunty.schema.json", json)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true))
            .RunGenerators(compilation).GetRunResult();

        var hints = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();
        Assert.Empty(result.Diagnostics);
        Assert.Contains("Customers.GetAll.auto.g.cs", hints);
        Assert.Contains("Customers.GetById.auto.g.cs", hints);
    }
}
