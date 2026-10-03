using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

internal static class ScopeHarness
{
    public const string ScopePath = "db/schema/jaunty.scope.json";

    public const string SchemaJson = @"{
  ""dialect"": ""sqlite"",
  ""tables"": {
    ""orders"": {
      ""name"": ""orders"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""tenant_id"": { ""name"": ""tenant_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""note"": { ""name"": ""note"", ""dbType"": ""varchar"", ""isNullable"": true }
      }
    },
    ""order_lines"": {
      ""name"": ""order_lines"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""order_id"": { ""name"": ""order_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""tenant_id"": { ""name"": ""tenant_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""qty"": { ""name"": ""qty"", ""dbType"": ""int"", ""isNullable"": false }
      }
    },
    ""customers"": {
      ""name"": ""customers"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""varchar"", ""isNullable"": false }
      }
    }
  },
  ""foreignKeys"": []
}";

    public const string OrdersAndLines =
        @"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" }, { ""table"": ""order_lines"", ""column"": ""tenant_id"" } ] }";

    public static GeneratorDriverRunResult Run(
        string? scopeJson,
        params (string path, string sql)[] files)
        => Run(scopeJson == null ? Array.Empty<(string, string)>() : new[] { (ScopePath, scopeJson) }, SchemaJson, null, false, files);

    public static GeneratorDriverRunResult Run(
        (string path, string json)[] scopeFiles,
        string? schemaJson,
        string? dialect,
        bool autoCrud,
        params (string path, string text)[] files)
    {
        var compilation = CSharpCompilation.Create("ScopeTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = ImmutableArray.CreateBuilder<AdditionalText>();
        foreach (var (path, text) in files)
            texts.Add(new InMemoryAdditionalText(path, text));
        if (schemaJson != null)
            texts.Add(new InMemoryAdditionalText("db/schema/jaunty.schema.json", schemaJson));
        foreach (var (path, json) in scopeFiles)
            texts.Add(new InMemoryAdditionalText(path, json));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(texts.ToImmutable())
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud, dialect));

        return driver.RunGenerators(compilation).GetRunResult();
    }

    public static List<string> Messages(GeneratorDriverRunResult result, string id)
    {
        var messages = new List<string>();
        foreach (var d in result.Diagnostics)
            if (d.Id == id)
                messages.Add(d.GetMessage());
        return messages;
    }

    public static bool Emits(GeneratorDriverRunResult result, string hintName)
    {
        foreach (var r in result.Results)
            foreach (var s in r.GeneratedSources)
                if (s.HintName == hintName)
                    return true;
        return false;
    }
}
