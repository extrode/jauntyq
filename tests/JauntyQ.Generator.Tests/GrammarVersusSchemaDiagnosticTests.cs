using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 T6: "your schema is missing something" (JNT2001) and "my grammar is
/// missing something" (JNT1009) are different failures needing opposite
/// responses from whoever reads them, so they no longer share a code.
///
/// The split is only worth having if BOTH halves stay sharp, so the genuine
/// missing-relation case is pinned here too.
/// </summary>
public class GrammarVersusSchemaDiagnosticTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""inbound_messages"": {
      ""name"": ""inbound_messages"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""subject"": { ""name"": ""subject"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": []
    },
    ""inbound_deliveries"": {
      ""name"": ""inbound_deliveries"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""inbound_message_id"": { ""name"": ""inbound_message_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""status"": { ""name"": ""status"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 20 }
      },
      ""indexes"": [
        { ""name"": ""ix_deliveries_message"", ""columns"": [""inbound_message_id""], ""isUnique"": false }
      ]
    }
  }
}";

    private static GeneratorDriverRunResult Run(string sql)
    {
        var compilation = CSharpCompilation.Create("GrammarSchemaTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Messages/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private const string LateralQuery =
        "select inbound_messages.id, d.status\n" +
        "from inbound_messages\n" +
        "left join lateral (select inbound_deliveries.status from inbound_deliveries\n" +
        "  where inbound_deliveries.inbound_message_id = inbound_messages.id limit 1) d on true";

    [Fact]
    public void LateralJoin_ReportsJNT1009()
    {
        var result = Run(LateralQuery);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1009");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    /// <summary>
    /// The regression this whole task exists for: the consumer was told a table
    /// they never wrote was missing from a schema that was perfectly correct.
    /// </summary>
    [Fact]
    public void LateralJoin_DoesNotClaimTheKeywordIsAMissingTable()
    {
        var result = Run(LateralQuery);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id == "JNT2001" && d.GetMessage().Contains("lateral", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LateralDiagnostic_PointsAtTheSupportedSurfaceReference()
    {
        var result = Run(LateralQuery);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1009");
        Assert.Contains("supported-sql.md", diag.GetMessage());
    }

    // ── The other half of the split must stay sharp ────────────────────────

    [Fact]
    public void GenuinelyAbsentTable_StillReportsJNT2001()
    {
        var result = Run("select ghosts.id from ghosts");

        Assert.Contains(result.Diagnostics, d => d.Id == "JNT2001");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT1009");
    }

    [Fact]
    public void OrdinaryJoin_ReportsNeither()
    {
        var result = Run(
            "select inbound_messages.id, inbound_deliveries.status\n" +
            "from inbound_messages\n" +
            "join inbound_deliveries on inbound_deliveries.inbound_message_id = inbound_messages.id");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT1009");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT2001");
    }
}
