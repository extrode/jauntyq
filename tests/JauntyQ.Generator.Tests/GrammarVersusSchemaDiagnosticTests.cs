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

    // ── DISTINCT ON: the same class of failure, found 2026-08-18 ───────────

    private const string DistinctOnQuery =
        "select distinct on (inbound_deliveries.inbound_message_id)\n" +
        "  inbound_deliveries.inbound_message_id, inbound_deliveries.status\n" +
        "from inbound_deliveries\n" +
        "order by inbound_deliveries.inbound_message_id, inbound_deliveries.id desc";

    /// <summary>
    /// The aliased form is the one that makes this a correctness fix. The two
    /// unaliased shapes were caught by JNT3004 for the wrong reason ("this
    /// expression needs an alias"); giving the column the alias JNT3004 asks
    /// for made the file pass validation outright, and the generated mapper
    /// then typed its first property by inferring a shape from the text
    /// "ON ( ... ) inbound_deliveries.inbound_message_id".
    /// </summary>
    private const string AliasedDistinctOnQuery =
        "select distinct on (inbound_deliveries.inbound_message_id)\n" +
        "  inbound_deliveries.inbound_message_id as message_id, inbound_deliveries.status\n" +
        "from inbound_deliveries\n" +
        "order by inbound_deliveries.inbound_message_id, inbound_deliveries.id desc";

    [Theory]
    [InlineData("unaliased")]
    [InlineData("aliased")]
    public void DistinctOn_ReportsJNT1009(string form)
    {
        var result = Run(form == "aliased" ? AliasedDistinctOnQuery : DistinctOnQuery);

        var diag = Assert.Single(result.Diagnostics, d => d.Id == "JNT1009");
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Contains("DISTINCT ON", diag.GetMessage());
        Assert.Contains("supported-sql.md", diag.GetMessage());
    }

    /// <summary>
    /// The refusal must arrive alone. Before the parser stepped over the whole
    /// modifier, the ON and its list glued onto the first projected column, so
    /// an unaliased DISTINCT ON also drew JNT3004 telling the consumer to alias
    /// a column that needs no alias — a true statement about a column they did
    /// not write, which is the same misdirection JNT1009 exists to end.
    /// </summary>
    [Fact]
    public void DistinctOn_DoesNotAlsoDemandAnAliasForTheFirstColumn()
    {
        var result = Run(DistinctOnQuery);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT3004");
    }

    /// <summary>
    /// Plain DISTINCT does not change the result shape and is still skipped,
    /// not refused — a check that fired on the DISTINCT keyword alone would
    /// refuse a large part of the accepted surface.
    /// </summary>
    [Fact]
    public void PlainDistinct_IsStillAccepted()
    {
        var result = Run("select distinct inbound_deliveries.status from inbound_deliveries");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "JNT1009");
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
