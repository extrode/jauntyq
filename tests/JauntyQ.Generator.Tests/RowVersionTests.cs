using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Tier 2 optimistic concurrency: a rowversion column turns the synthetic
/// Update/Delete into version-guarded statements (0 rows affected =
/// conflict), is excluded from Insert/SET/Upsert, and surfaces on the
/// canonical POCO as byte[]? RowVersion.
/// </summary>
public class RowVersionTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""sqlserver"",
  ""tables"": {
    ""gadgets"": {
      ""name"": ""gadgets"",
      ""columns"": {
        ""gadget_id"": { ""name"": ""gadget_id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true, ""isIdentity"": true },
        ""name"": { ""name"": ""name"", ""dbType"": ""nvarchar"", ""isNullable"": false, ""maxLength"": 20, ""isUnicode"": true },
        ""row_version"": { ""name"": ""row_version"", ""dbType"": ""timestamp"", ""isNullable"": false, ""isRowVersion"": true }
      }
    }
  }
}";

    private static GeneratorDriverRunResult Run()
    {
        var compilation = CSharpCompilation.Create("RowVersionTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string Source(GeneratorDriverRunResult result, string hintName) =>
        result.Results[0].GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString();

    [Fact]
    public void Update_IsVersionGuarded_AndNeverWritesTheToken()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Update.auto.g.cs");

        Assert.Contains("public int Update(string name, int gadget_id, byte[]? row_version)", source);
        Assert.Contains("where gadget_id = @gadget_id and row_version = @row_version", source);
        Assert.Contains("set name = @name", source);
        Assert.DoesNotContain("row_version = @row_version,", source); // never in SET
    }

    [Fact]
    public void Delete_IsVersionGuarded()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Delete.auto.g.cs");

        Assert.Contains("public int Delete(int gadget_id, byte[]? row_version)", source);
        Assert.Contains("where gadget_id = @gadget_id and row_version = @row_version", source);
    }

    [Fact]
    public void Insert_ExcludesRowVersion_StillReturnsIdentity()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Insert.auto.g.cs");

        Assert.Contains("public int Insert(string name)", source);
        Assert.DoesNotContain("row_version", source);
    }

    [Fact]
    public void CanonicalPoco_RowVersionIsNullableByteArray_NotRequired()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Row.g.cs");

        Assert.Contains("public byte[]? RowVersion { get; set; }", source);
        Assert.DoesNotContain("required byte[]? RowVersion", source);
    }

    [Fact]
    public void PocoUpdate_ForwardsTheVersionTokenLast()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Poco.auto.g.cs");

        Assert.Contains("public int Update(Gadget row) => Update(row.Name, row.GadgetId, row.RowVersion);", source);
        Assert.Contains("public int Delete(Gadget row) => Delete(row.GadgetId, row.RowVersion);", source);
    }

    [Fact]
    public void Upsert_SkippedForIdentityOnlyKey_NoRowVersionAnywhereInInsertPath()
    {
        var result = Run();

        // gadgets has an identity-only PK: no upsert synthetic at all
        Assert.DoesNotContain(result.Results[0].GeneratedSources,
            s => s.HintName == "Gadgets.Upsert.auto.g.cs");
    }

    [Fact]
    public void VersionParam_HasNoDefault_SoCallersCannotForgetIt()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Update.auto.g.cs");

        // byte[]? is nullable-typed (value unknown until read) but must not
        // be optional: omitting it would silently never match (always
        // "conflict"), so the signature forces callers to pass it.
        Assert.DoesNotContain("byte[]? row_version = default", source);
    }

    [Fact]
    public void WriteGuard_StillAppliesToRealColumns()
    {
        var result = Run();
        string source = Source(result, "Gadgets.Update.auto.g.cs");

        Assert.Contains("if (name.Length > 20)", source);
        Assert.Contains(".Size = 20;", source);
    }
}
