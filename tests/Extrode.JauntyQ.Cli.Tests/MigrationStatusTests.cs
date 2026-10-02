using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

public class MigrationStatusTests
{
    private static string Report(MigrationStatus status, int fileCount)
    {
        var writer = new StringWriter();
        status.Write(writer, "db/migrations", fileCount);
        return writer.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void FilesWithNoRow_ArePendingInNaturalOrder()
    {
        var status = MigrationStatus.Compute(
            new[] { "V10__rename.sql", "V2__add.sql", "V3__index.sql" },
            new[] { "V1__init.sql" });

        Assert.Equal(new[] { "V2__add.sql", "V3__index.sql", "V10__rename.sql" }, status.Pending);
        Assert.Empty(status.AppliedStillPresent);
        Assert.Empty(status.OutOfOrder);
        Assert.Equal("V1__init.sql", status.LatestApplied);
        Assert.Equal(1, status.RecordedCount);
        Assert.True(status.TableExists);
        Assert.False(status.HasDrift);
    }

    [Fact]
    public void AFileWithARow_IsAppliedStillPresent_AndIsDrift()
    {
        var status = MigrationStatus.Compute(
            new[] { "0002_b.sql", "0003_c.sql" },
            new[] { "0001_a.sql", "0002_b.sql" });

        Assert.Equal(new[] { "0002_b.sql" }, status.AppliedStillPresent);
        Assert.Equal(new[] { "0003_c.sql" }, status.Pending);
        Assert.Empty(status.OutOfOrder);
        Assert.True(status.HasDrift);
    }

    [Fact]
    public void APendingFileSortingBeforeTheLatestApplied_IsOutOfOrder()
    {
        var status = MigrationStatus.Compute(
            new[] { "V9__late.sql", "V12__next.sql" },
            new[] { "V2__a.sql", "V10__b.sql", "V3__c.sql" });

        Assert.Equal("V10__b.sql", status.LatestApplied);
        Assert.Equal(new[] { "V9__late.sql", "V12__next.sql" }, status.Pending);
        Assert.Equal(new[] { "V9__late.sql" }, status.OutOfOrder);
        Assert.True(status.HasDrift);
    }

    [Fact]
    public void NoTable_EverythingIsPendingAndNothingIsOutOfOrder()
    {
        var status = MigrationStatus.Compute(new[] { "0002_b.sql", "0001_a.sql" }, null);

        Assert.False(status.TableExists);
        Assert.Null(status.LatestApplied);
        Assert.Equal(0, status.RecordedCount);
        Assert.Equal(new[] { "0001_a.sql", "0002_b.sql" }, status.Pending);
        Assert.Empty(status.OutOfOrder);
        Assert.False(status.HasDrift);
    }

    [Fact]
    public void VersionsMatchFileNamesExactly()
    {
        var status = MigrationStatus.Compute(new[] { "0001_A.sql" }, new[] { "0001_a.sql", "0001_A" });

        Assert.Equal(new[] { "0001_A.sql" }, status.Pending);
        Assert.Empty(status.AppliedStillPresent);
    }

    [Fact]
    public void TheReport_ListsEachGroupUnderItsHeading()
    {
        var status = MigrationStatus.Compute(
            new[] { "0001_a.sql", "0002_b.sql", "0005_e.sql" },
            new[] { "0001_a.sql", "0003_c.sql" });

        Assert.Equal(
            "Migration files in db/migrations: 3\n" +
            "Recorded in schema_migrations: 2\n" +
            "\n" +
            "Pending, in apply order (2):\n" +
            "  0002_b.sql\n" +
            "  0005_e.sql\n" +
            "\n" +
            "Applied but still in db/migrations (1). The build applies these again; re-pull the snapshot, then move them out:\n" +
            "  0001_a.sql\n" +
            "\n" +
            "Out of order (1). These sort before 0003_c.sql, the latest applied migration, so a runner applies them after migrations they come before:\n" +
            "  0002_b.sql\n",
            Report(status, 3));
    }

    [Fact]
    public void TheReport_SaysUpToDate_WhenNothingIsPendingOrDrifted()
    {
        var status = MigrationStatus.Compute(Array.Empty<string>(), new[] { "0001_a.sql" });

        Assert.Equal(
            "Migration files in db/migrations: 0\n" +
            "Recorded in schema_migrations: 1\n" +
            "\n" +
            "Up to date: nothing pending.\n",
            Report(status, 0));
    }

    [Fact]
    public void TheReport_NamesAMissingTable()
    {
        var status = MigrationStatus.Compute(new[] { "0001_a.sql" }, null);

        Assert.StartsWith(
            "Migration files in db/migrations: 1\n" +
            "No schema_migrations table: nothing is recorded as applied.\n" +
            "\n" +
            "Pending, in apply order (1):\n",
            Report(status, 1));
    }
}
