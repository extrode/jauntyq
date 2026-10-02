using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class SnapshotExplicitNullTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""customers"": { ""name"": ""customers"", ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""email"": { ""name"": ""email"", ""dbType"": ""nvarchar"", ""isNullable"": true, ""maxLength"": 50 } },
      ""indexes"": null },
    ""orders"": { ""name"": ""orders"", ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""customer_id"": { ""name"": ""customer_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""tag"": { ""name"": ""tag"", ""dbType"": null, ""isNullable"": true } },
      ""indexes"": [] }
  },
  ""foreignKeys"": null,
  ""procedures"": null
}";

    private static GeneratorDriverRunResult Run(params (string Path, string Text)[] files)
    {
        var compilation = CSharpCompilation.Create("SnapshotExplicitNullAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var texts = new List<AdditionalText> { new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson) };
        texts.AddRange(files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)));
        return CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutableArray())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true))
            .RunGenerators(compilation).GetRunResult();
    }

    [Fact]
    public void ASnapshotWithExplicitNulls_GeneratesWithoutAnInternalError()
    {
        var result = Run(("db/Orders/ByCustomer.sql", "select o.id, c.email from orders o join customers c on c.id = o.customer_id where o.customer_id = @CustomerId"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.HintName == "Orders.ByCustomer.g.cs");
        Assert.Contains(result.Results[0].GeneratedSources, s => s.SourceText.ToString().Contains("GetById"));
    }

    [Fact]
    public void ASnapshotWithExplicitNulls_AppliesAPendingMigrationWithoutAnInternalError()
    {
        var result = Run(
            ("db/Orders/All.sql", "select id, note from orders"),
            ("db/migrations/001_note.sql", "alter table orders add note nvarchar(20) null;"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ANullTableEntry_IsReportedAsAnUnreadableSnapshot_NotAnInternalError()
    {
        var compilation = CSharpCompilation.Create("SnapshotNullEntryAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var texts = ImmutableArray.Create<AdditionalText>(
            new InMemoryAdditionalText("schema/jaunty.schema.json", @"{ ""dialect"": ""sqlserver"", ""tables"": { ""orders"": null } }"),
            new InMemoryAdditionalText("db/Orders/All.sql", "select id from orders"));

        var result = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts)
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true))
            .RunGenerators(compilation).GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT0001");
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
