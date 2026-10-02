using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class ParenlessComputedColumnTests
{
    [Theory]
    [InlineData("db/ddl/001_tables.sql", "create table orders (id int not null primary key, qty int not null, price decimal(10,2) not null, total as qty * price)")]
    [InlineData("db/migrations/0001_total.sql", "alter table orders add total as qty * price")]
    public void AQueryOnAParenlessComputedColumn_IsNotAFalseJnt2002(string path, string ddl)
    {
        var texts = ImmutableArray.CreateBuilder<AdditionalText>();
        if (path.StartsWith("db/migrations/"))
            texts.Add(new InMemoryAdditionalText("db/ddl/001_tables.sql",
                "create table orders (id int not null primary key, qty int not null, price decimal(10,2) not null)"));
        texts.Add(new InMemoryAdditionalText(path, ddl));
        texts.Add(new InMemoryAdditionalText("db/Orders/GetTotals.sql", "select id, total from orders"));
        var compilation = CSharpCompilation.Create("ParenlessComputedAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutable())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(false, "sqlserver"))
            .RunGenerators(compilation).GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName.StartsWith("Orders.GetTotals"));
    }
}
