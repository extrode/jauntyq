using Extrode.JauntyQ.Analysis.Impact;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

public class ReportSerializationTests
{
    private static MigrationImpactReport Sample() => new(
        "schema/jaunty.schema.json",
        new List<string> { "db/migrations/0007_drop_legacy_col.sql" },
        new List<ImpactEntry>
        {
            new("src/App/Queries/GetUser.sql", "User.GetById", Classification.Breaking, new List<ImpactReason>
            {
                new("users.legacy_flag", "removed", "column referenced by the query no longer exists")
            }),
            new("src/App/Queries/SearchUsers.sql", "User.Search", Classification.Risky, new List<ImpactReason>
            {
                new("users.name", "maxLength", "max length 100 → 50")
            }),
            new("src/App/Queries/CountUsers.sql", "User.Count", Classification.Safe, new List<ImpactReason>()),
        });

    [Fact]
    public void RoundTrips_ThroughJson() // SC-002
    {
        var original = Sample();

        var report = MigrationImpactReport.FromJson(original.ToJson());

        Assert.Equal(original.BaselineId, report.BaselineId);
        Assert.Equal(original.MigrationSet, report.MigrationSet);
        Assert.Equal(3, report.Entries.Count);

        var breaking = report.Entries[0];
        Assert.Equal("User.GetById", breaking.EntityMethod);
        Assert.Equal(Classification.Breaking, breaking.Classification);
        Assert.Equal("users.legacy_flag", Assert.Single(breaking.Reasons).SchemaObject);
        Assert.Empty(report.Entries[2].Reasons);
    }

    [Fact]
    public void Classification_SerializesAsUppercaseToken()
    {
        var json = Sample().ToJson();

        Assert.Contains("\"BREAKING\"", json);
        Assert.Contains("\"RISKY\"", json);
        Assert.Contains("\"SAFE\"", json);
    }

    [Fact]
    public void JsonShape_UsesDocumentedFieldNames()
    {
        var json = Sample().ToJson();

        Assert.Contains("\"baselineId\"", json);
        Assert.Contains("\"migrationSet\"", json);
        Assert.Contains("\"entries\"", json);
        Assert.Contains("\"queryFile\"", json);
        Assert.Contains("\"entityMethod\"", json);
        Assert.Contains("\"classification\"", json);
        Assert.Contains("\"reasons\"", json);
        Assert.Contains("\"schemaObject\"", json);
        Assert.Contains("\"changeKind\"", json);
        Assert.Contains("\"effect\"", json);
    }
}
