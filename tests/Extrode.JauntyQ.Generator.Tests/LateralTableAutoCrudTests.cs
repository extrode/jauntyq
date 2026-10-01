using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class LateralTableAutoCrudTests
{
    private static GeneratorDriverRunResult Run(string dialect, string table)
    {
        string json = "{ \"dialect\": \"" + dialect + "\", \"tables\": { \"" + table + "\": { \"name\": \"" + table + "\", \"columns\": { " +
            "\"id\": { \"name\": \"id\", \"dbType\": \"int\", \"isNullable\": false, \"isPrimaryKey\": true }, " +
            "\"c\": { \"name\": \"c\", \"dbType\": \"int\", \"isNullable\": true } } } } }";
        var compilation = CSharpCompilation.Create("LateralTableAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/schema/jaunty.schema.json", json)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));
        return driver.RunGenerators(compilation).GetRunResult();
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("sqlite")]
    public void ATableNamedLateral_GetsItsReadMethods(string dialect)
    {
        var result = Run(dialect, "lateral");

        var hints = result.GeneratedTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();
        Assert.Contains("Lateral.GetAll.auto.g.cs", hints);
        Assert.Contains("Lateral.GetById.auto.g.cs", hints);
        Assert.Contains("Lateral.Insert.auto.g.cs", hints);
        Assert.Empty(result.Diagnostics);
    }
}
