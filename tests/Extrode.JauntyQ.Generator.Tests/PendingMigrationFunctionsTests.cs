using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

[Trait("Category", "AuditRegression")]
public class PendingMigrationFunctionsTests
{
    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgres")]
    [InlineData("mysql")]
    public void PendingMigration_KeepsTheFunctionAccessor(string dialect)
    {
        string dir = Path.Combine(GeneratedOutputApprovalTests.ApprovedDir(), dialect);
        string withoutMigration = JauntyDbSource(GeneratedOutputApprovalTests.Generate(dir));
        string withMigration = JauntyDbSource(GeneratedOutputApprovalTests.Generate(dir, null,
            ("db/migrations/001_note.sql", "ALTER TABLE customers ADD note varchar(10) NULL;")));

        Assert.Contains("public FunctionAccessor Functions", withoutMigration);
        Assert.Equal(withoutMigration, withMigration);
    }

    private static string JauntyDbSource(string output)
    {
        int start = output.IndexOf("==== JauntyDb.g.cs\n", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = output.IndexOf("\n==== ", start + 1, StringComparison.Ordinal);
        return end < 0 ? output.Substring(start) : output.Substring(start, end - start);
    }
}
