using System.Collections.Immutable;
using JauntyQ.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 followup: JNT8012 must only fire when the JNT8004 analysis it
/// reports on actually reached a verdict.
///
/// JNT8012 says "no filter column on this query is unindexed, so it suppresses
/// nothing. The index it was waiting for probably exists now: remove the
/// directive." That is a claim about a completed analysis. As 015 shipped it,
/// the check ran BEFORE the hasErrors/schema-null gate and asked only whether
/// anything had been suppressed, so it also fired in three cases where no
/// index analysis had run at all:
///
///   * no schema snapshot,
///   * the query failed earlier validation,
///   * the snapshot declares no index metadata anywhere, which makes
///     QueryValidator's index checks return early.
///
/// Acting on the advice in any of those cases -- deleting the directive --
/// produces the JNT8004 it was suppressing all along, which is the opposite
/// of what the consumer was told.
/// </summary>
public class AllowUnindexedDeadDirectiveTests
{
    private const string IndexedSchema = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""inbound_messages"": {
      ""name"": ""inbound_messages"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""subject"": { ""name"": ""subject"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": [
        { ""name"": ""pk_msg"", ""columns"": [""id""], ""isUnique"": true },
        { ""name"": ""ix_msg_user"", ""columns"": [""user_id""], ""isUnique"": false }
      ]
    }
  }
}";

    /// <summary>
    /// Same tables and columns, every index removed. A snapshot in this shape
    /// is what an extractor produces when index metadata was not collected --
    /// it does not mean the database has no indexes.
    /// </summary>
    private const string NoIndexMetadataSchema = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""inbound_messages"": {
      ""name"": ""inbound_messages"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""user_id"": { ""name"": ""user_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""subject"": { ""name"": ""subject"", ""dbType"": ""varchar"", ""isNullable"": false, ""maxLength"": 200 }
      },
      ""indexes"": []
    }
  }
}";

    private static ImmutableArray<Diagnostic> Run(string sql, string schemaJson)
    {
        var compilation = CSharpCompilation.Create("DeadDirectiveTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Outcomes/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", schemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Diagnostics;
    }

    private static bool Has(ImmutableArray<Diagnostic> diags, string code) =>
        diags.Any(d => d.Id == code);

    /// <summary>
    /// The case JNT8012 exists for, and the control for every negative below:
    /// the analysis ran, found every filter covered, so the directive really
    /// is dead.
    /// </summary>
    [Fact]
    public void DirectiveOnAFullyIndexedQuery_IsReportedAsDead()
    {
        var diags = Run(
            "-- @allow-unindexed backfill job, runs nightly\n" +
            "select m.id from inbound_messages m where m.user_id = @userId",
            IndexedSchema);

        Assert.True(Has(diags, "JNT8012"));
    }

    [Fact]
    public void DirectiveOnAGenuinelyUnindexedQuery_IsNotReportedAsDead()
    {
        var diags = Run(
            "-- @allow-unindexed backfill job, runs nightly\n" +
            "select m.id from inbound_messages m where m.subject = @s",
            IndexedSchema);

        Assert.False(Has(diags, "JNT8012"));
        Assert.False(Has(diags, "JNT8004"), "The directive's whole job is suppressing this one.");
    }

    [Fact]
    public void DirectiveOnAQueryThatFailedValidation_IsNotReportedAsDead()
    {
        var diags = Run(
            "-- @allow-unindexed backfill job, runs nightly\n" +
            "select t.id from no_such_table t where t.subject = @s",
            IndexedSchema);

        Assert.True(Has(diags, "JNT2001"), "Precondition: this query must fail validation.");
        Assert.False(Has(diags, "JNT8012"),
            "Nothing resolved this query's filter columns against an index, so the directive " +
            "cannot be shown to be dead.");
    }

    [Fact]
    public void DirectiveAgainstASnapshotWithNoIndexMetadata_IsNotReportedAsDead()
    {
        var diags = Run(
            "-- @allow-unindexed backfill job, runs nightly\n" +
            "select m.id from inbound_messages m where m.subject = @s",
            NoIndexMetadataSchema);

        Assert.False(Has(diags, "JNT8012"),
            "A snapshot that records no indexes anywhere makes the index checks return early. " +
            "That is absence of evidence, not evidence the filters are covered.");
    }
}
