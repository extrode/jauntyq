using Extrode.JauntyQ.Analysis.Diff;
using Extrode.JauntyQ.Analysis.Impact;
using Extrode.JauntyQ.Analysis.Migrations;
using System.Text.Json;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for the small model/support types that were out of scope
/// for the 7-file Stryker pass: MigrationStatement's default field values,
/// ImpactReason/ImpactEntry's default field values, MigrationImpactReport's
/// Highest/Count/JsonOptions/FromJson-error paths, Classification's unknown-
/// token deserialization failure, and SchemaDelta.IsEmpty's three-term guard.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SmallModelTypesMutationCoverageTests
{
    // ── MigrationStatement: default field values ────────────────────────

    [Fact]
    public void MigrationStatement_DefaultTableNameAndRawText_AreEmptyStrings()
    {
        var stmt = new MigrationStatement();

        Assert.Equal("", stmt.TableName);
        Assert.Equal("", stmt.RawText);
    }

    // ── ImpactReason: default field values ──────────────────────────────

    [Fact]
    public void ImpactReason_DefaultConstructor_AllFieldsAreEmptyStrings()
    {
        var reason = new ImpactReason();

        Assert.Equal("", reason.SchemaObject);
        Assert.Equal("", reason.ChangeKind);
        Assert.Equal("", reason.Effect);
    }

    // ── ImpactEntry: default field values ───────────────────────────────

    [Fact]
    public void ImpactEntry_DefaultConstructor_QueryFileAndEntityMethodAreEmptyStrings()
    {
        var entry = new ImpactEntry();

        Assert.Equal("", entry.QueryFile);
        Assert.Equal("", entry.EntityMethod);
    }

    // ── MigrationImpactReport: default field value ──────────────────────

    [Fact]
    public void MigrationImpactReport_DefaultConstructor_BaselineIdIsEmptyString()
    {
        var report = new MigrationImpactReport();

        Assert.Equal("", report.BaselineId);
    }

    // ── MigrationImpactReport.Highest: the '>' comparison ───────────────

    [Fact]
    public void Highest_NoEntries_IsSafe()
    {
        var report = new MigrationImpactReport("b", new List<string>(), new List<ImpactEntry>());

        Assert.Equal(Classification.Safe, report.Highest);
    }

    [Fact]
    public void Highest_PicksTheMostSevereEntry_RegardlessOfOrder()
    {
        var report = new MigrationImpactReport("b", new List<string>(), new List<ImpactEntry>
        {
            new("a", "m1", Classification.Risky, new List<ImpactReason>()),
            new("b", "m2", Classification.Safe, new List<ImpactReason>()),
            new("c", "m3", Classification.Breaking, new List<ImpactReason>()),
            new("d", "m4", Classification.Risky, new List<ImpactReason>()),
        });

        Assert.Equal(Classification.Breaking, report.Highest);
    }

    // Note: the '>' vs '>=' mutation at MigrationImpactReport.cs:50 is a
    // confirmed-equivalent mutant, not force-killed here. `highest` is only
    // ever reassigned to `e.Classification`, so when `e.Classification ==
    // highest` the reassignment (fired only under the mutated `>=`) sets
    // `highest` to the exact value it already held -- no test can produce an
    // observable difference between skipping the reassignment (`>`) and
    // performing a same-value reassignment (`>=`). Documented in the handoff.

    // ── MigrationImpactReport.Count ──────────────────────────────────────

    [Fact]
    public void Count_ReturnsNumberOfEntriesAtTheGivenClassification()
    {
        var report = new MigrationImpactReport("b", new List<string>(), new List<ImpactEntry>
        {
            new("a", "m1", Classification.Risky, new List<ImpactReason>()),
            new("b", "m2", Classification.Safe, new List<ImpactReason>()),
            new("c", "m3", Classification.Risky, new List<ImpactReason>()),
        });

        Assert.Equal(2, report.Count(Classification.Risky));
        Assert.Equal(1, report.Count(Classification.Safe));
        Assert.Equal(0, report.Count(Classification.Breaking));
    }

    // ── MigrationImpactReport.ToJson: WriteIndented actually fires ──────

    [Fact]
    public void ToJson_IsActuallyIndented_NotASingleLine()
    {
        var report = new MigrationImpactReport("b", new List<string> { "x.sql" }, new List<ImpactEntry>());

        var json = report.ToJson();

        Assert.Contains("\n", json);
    }

    // ── MigrationImpactReport.FromJson: the deserialize-to-null guard ───

    [Fact]
    public void FromJson_JsonLiteralNull_ThrowsWithExactMessage()
    {
        var ex = Assert.Throws<JsonException>(() => MigrationImpactReport.FromJson("null"));

        Assert.Equal("Migration impact report deserialized to null.", ex.Message);
    }

    // ── Classification: unknown wire token on deserialize ────────────────

    [Fact]
    public void Classification_UnknownWireToken_ThrowsWithExactMessage()
    {
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Classification>("\"UNKNOWN\""));

        Assert.Equal("Unknown classification 'UNKNOWN'.", ex.Message);
    }

    // ── SchemaDelta.IsEmpty: all three terms must hold ──────────────────

    private static SchemaDelta Delta(
        IReadOnlyList<string>? added = null,
        IReadOnlyList<string>? removed = null,
        IReadOnlyList<TableDelta>? modified = null) =>
        new(added ?? new List<string>(), removed ?? new List<string>(), modified ?? new List<TableDelta>());

    [Fact]
    public void IsEmpty_AllThreeCollectionsEmpty_IsTrue()
    {
        Assert.True(Delta().IsEmpty);
    }

    [Fact]
    public void IsEmpty_OnlyAddedTablesNonEmpty_IsFalse()
    {
        Assert.False(Delta(added: new List<string> { "t" }).IsEmpty);
    }

    [Fact]
    public void IsEmpty_OnlyRemovedTablesNonEmpty_IsFalse()
    {
        Assert.False(Delta(removed: new List<string> { "t" }).IsEmpty);
    }

    [Fact]
    public void IsEmpty_OnlyModifiedTablesNonEmpty_IsFalse()
    {
        var modified = new List<TableDelta>
        {
            new("t", new List<string>(), new List<string>(), new List<ColumnChange>())
        };

        Assert.False(Delta(modified: modified).IsEmpty);
    }
}
