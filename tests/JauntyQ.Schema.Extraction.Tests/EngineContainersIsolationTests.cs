using System.Threading.Tasks;
using JauntyQ.Schema.Extraction;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Pins the isolation the shared-container design rests on: two fixtures on the
/// same engine live in separate databases, and an extractor pointed at one must
/// never see the other's tables. If database scoping ever regressed (an
/// extractor querying across schemas, or CreateDatabaseAsync handing back the
/// admin catalog), every converted fixture would start bleeding into its
/// neighbours and this is the test that says so directly, instead of a distant
/// Assert.Single failing with a confusing duplicate.
/// </summary>
public class EngineContainersIsolationTests
    : IClassFixture<PostgresPartialIndexFixture>, IClassFixture<PostgresEnumFixture>
{
    private readonly PostgresPartialIndexFixture _partial;
    private readonly PostgresEnumFixture _enum;

    public EngineContainersIsolationTests(PostgresPartialIndexFixture partial, PostgresEnumFixture @enum)
    {
        _partial = partial;
        _enum = @enum;
    }

    [SkippableFact]
    public async Task TwoFixturesOnTheSameEngine_SeeOnlyTheirOwnTables()
    {
        Skip.IfNot(_partial.Available, _partial.SkipReason);
        Skip.IfNot(_enum.Available, _enum.SkipReason);

        var extractor = new PostgresExtractor();
        var partialSchema = await extractor.ExtractAsync(_partial.ConnectionString);
        var enumSchema = await extractor.ExtractAsync(_enum.ConnectionString);

        // Each side's own tables are present...
        Assert.True(partialSchema.Tables.ContainsKey("plain_users"));
        Assert.True(enumSchema.Tables.ContainsKey("shipments"));

        // ...and the other side's are not, even though both databases sit in
        // the one Postgres container.
        Assert.False(partialSchema.Tables.ContainsKey("shipments"));
        Assert.False(enumSchema.Tables.ContainsKey("plain_users"));

        // The enum type declared by the enum fixture must not leak either --
        // Postgres enums are per-database objects, which is exactly why
        // database-level isolation suffices.
        Assert.False(partialSchema.Enums.ContainsKey("order_status"));
    }
}
