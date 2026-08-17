using JauntyQ.Analysis;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// Direct unit coverage for the canonical entity-name algorithm (audit round
/// 61: unified home for what used to be three independently maintained
/// copies — this one, plus JauntyQ.Cli's ImpactCommand/UsageManifestBuilder,
/// which now both forward here instead of re-deriving the convention).
/// </summary>
[Trait("Category", "AuditRegression")]
public class EntityNameResolverTests
{
    [Fact]
    public void FileDirectlyInRoot_ReturnsQueriesCatchAll()
    {
        Assert.Equal("Queries", EntityNameResolver.ExtractEntityName(
            "db/GetOrphaned.sql", "db/"));
    }

    [Fact]
    public void OneFolderDeep_NoWrapper_ReturnsSubfolderName()
    {
        Assert.Equal("Products", EntityNameResolver.ExtractEntityName(
            "db/Products/GetAll.sql", "db/"));
    }

    [Theory]
    [InlineData("tables")]
    [InlineData("TABLES")]
    [InlineData("views")]
    public void OneFolderDeep_WithTablesOrViewsWrapper_ReturnsEntityFolderName(string wrapper)
    {
        Assert.Equal("Products", EntityNameResolver.ExtractEntityName(
            $"db/{wrapper}/Products/GetAll.sql", "db/"));
    }

    [Fact]
    public void FileDirectlyUnderTablesWrapper_NoEntitySubfolder_ReturnsWrapperNameNotQueries()
    {
        // Only 2 segments after the prefix ("tables", "File.sql"), so the
        // tables/views skip never triggers (it requires >= 3 segments) —
        // this is the existing, deliberate behavior of the algorithm, not
        // something round 61 changed.
        Assert.Equal("tables", EntityNameResolver.ExtractEntityName(
            "db/tables/GetAll.sql", "db/"));
    }

    [Theory]
    [InlineData("db/tables/Products/Sub/GetAll.sql")]
    [InlineData("db/Products/Sub/GetAll.sql")]
    public void NestedMoreThanOneFolderDeep_ReturnsEntityName_NotImmediateParent(string path)
    {
        // AUD-R61-01: this is exactly the case the old CLI DeriveEntity got
        // wrong (it would have returned "Sub", the immediate parent, instead
        // of the real entity "Products").
        Assert.Equal("Products", EntityNameResolver.ExtractEntityName(path, "db/"));
    }

    [Fact]
    public void PathOutsideCommonPrefix_FallsBackToBareFileName()
    {
        Assert.Equal("Queries", EntityNameResolver.ExtractEntityName(
            "other/GetAll.sql", "db/"));
    }
}
