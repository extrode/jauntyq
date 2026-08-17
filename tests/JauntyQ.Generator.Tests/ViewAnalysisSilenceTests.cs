using System.Collections.Immutable;
using JauntyQ.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Spec 015 followup, requirement R7: an analysis that cannot be proved
/// against a view stays silent.
///
/// Before 015 the perf and consistency analyses were silent about views by
/// accident — a view was never extracted, so it never resolved to a snapshot
/// relation and every check fell through its own null guard. Making views
/// first-class relations removed that accident without replacing it with a
/// decision, so each check began asserting things a view cannot answer:
///
///   * JNT8006 "matches no declared foreign key" — a view cannot declare a
///     foreign key in any of the five dialects, so EVERY join to one warned.
///   * JNT8004 "no index covers this column" — a view carries no indexes, so
///     every filter on one warned, though the planner inlines the view and may
///     seek an index on the base table underneath.
///   * JNT8007 "ORDER BY has no supporting index" — same, for sorts.
///
/// The consequence was perverse: docs/06-reference/supported-sql.md tells a
/// consumer to rewrite an unsupported shape as a view, and doing so bought
/// them one warning per filter for a query that is not slow.
///
/// Every silence test here is paired with a base-table control, because a
/// check that has been silenced everywhere passes these tests too.
/// </summary>
public class ViewAnalysisSilenceTests
{
    private const string SchemaJson = @"{
  ""dialect"": ""postgres"",
  ""tables"": {
    ""users"": {
      ""name"": ""users"",
      ""columns"": {
        ""id"": { ""name"": ""id"", ""dbType"": ""int"", ""isNullable"": false, ""isPrimaryKey"": true },
        ""region"": { ""name"": ""region"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 20 }
      },
      ""indexes"": [
        { ""name"": ""pk_users"", ""columns"": [""id""], ""isUnique"": true }
      ]
    },
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
    },
    ""v_message_outcomes"": {
      ""name"": ""v_message_outcomes"",
      ""isView"": true,
      ""columns"": {
        ""message_id"": { ""name"": ""message_id"", ""dbType"": ""int"", ""isNullable"": false },
        ""outcome"": { ""name"": ""outcome"", ""dbType"": ""varchar"", ""isNullable"": true, ""maxLength"": 20 }
      },
      ""indexes"": []
    }
  },
  ""foreignKeys"": [
    { ""fromTable"": ""inbound_messages"", ""fromColumn"": ""user_id"", ""toTable"": ""users"", ""toColumn"": ""id"" }
  ]
}";

    private static ImmutableArray<Diagnostic> Run(string sql)
    {
        var compilation = CSharpCompilation.Create("ViewSilenceTestAssembly",
            new[] { CSharpSyntaxTree.ParseText("") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new JauntyQGenerator())
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(
                new InMemoryAdditionalText("db/Outcomes/TestQuery.sql", sql),
                new InMemoryAdditionalText("schema/jaunty.schema.json", SchemaJson)))
            .WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(autoCrud: false));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Diagnostics;
    }

    private static bool Has(ImmutableArray<Diagnostic> diags, string code) =>
        diags.Any(d => d.Id == code);

    // ── JNT8006: joins to a view ───────────────────────────────────────────

    [Fact]
    public void JoinToAView_DoesNotClaimAMissingForeignKey()
    {
        var diags = Run(
            "select m.id, v.outcome from inbound_messages m " +
            "join v_message_outcomes v on v.message_id = m.id");

        Assert.False(Has(diags, "JNT8006"),
            "A view cannot declare a foreign key, so 'matches no declared foreign key' is unprovable, not false.");
    }

    /// <summary>
    /// The control. If JNT8006 had simply been switched off rather than made
    /// view-aware, the test above would pass for the wrong reason.
    /// </summary>
    [Fact]
    public void JoinBetweenTwoBaseTablesOnANonKeyPair_StillWarns()
    {
        var diags = Run(
            "select m.id, u.region from inbound_messages m " +
            "join users u on u.region = m.subject");

        Assert.True(Has(diags, "JNT8006"),
            "The FK check must still fire between two base tables.");
    }

    // ── JNT8004: filters on a view ─────────────────────────────────────────

    [Fact]
    public void FilterOnAViewColumn_DoesNotDemandAnIndex()
    {
        var diags = Run(
            "select v.message_id, v.outcome from v_message_outcomes v where v.outcome = @outcome");

        Assert.False(Has(diags, "JNT8004"),
            "A view declares no indexes, so 'no index covers this column' is true of every view column " +
            "and says nothing about whether the query scans.");
    }

    [Fact]
    public void FilterOnAnUnindexedBaseTableColumn_StillWarns()
    {
        var diags = Run("select m.id from inbound_messages m where m.subject = @s");

        Assert.True(Has(diags, "JNT8004"),
            "The index check must still fire for a base table.");
    }

    // ── JNT8007: ORDER BY on a view ────────────────────────────────────────
    //
    // JNT8007 does not share CheckIndexed's code path -- it reaches
    // IsColumnIndexSupported directly -- so it needed its own gate, and gets
    // its own pair of tests rather than being assumed covered.

    [Fact]
    public void OrderByAViewColumn_DoesNotDemandAnIndex()
    {
        var diags = Run(
            "select v.message_id, v.outcome from v_message_outcomes v order by v.outcome");

        Assert.False(Has(diags, "JNT8007"));
    }

    [Fact]
    public void OrderByAnUnindexedBaseTableColumn_StillWarns()
    {
        var diags = Run("select m.id, m.subject from inbound_messages m order by m.subject");

        Assert.True(Has(diags, "JNT8007"),
            "The sort check must still fire for a base table.");
    }
}
