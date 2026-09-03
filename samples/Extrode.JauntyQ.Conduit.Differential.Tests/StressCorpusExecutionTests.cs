using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Conduit.Differential.Tests;

/// <summary>
/// Runs the whole corpus on SQLite alone, which needs no container.
///
/// It asserts nothing about divergence -- one engine cannot disagree with
/// itself. What it catches is a case whose SQL is malformed rather than
/// divergent: without it, a typo in a query would arrive as "SQLite refused
/// and the others did not" and read as a finding.
/// </summary>
[Trait("Category", "Differential")]
public class StressCorpusExecutionTests(ITestOutputHelper output) : IDisposable
{
    private readonly SqliteConnection _connection = Open();
    private readonly ITestOutputHelper _output = output;

    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void EveryCaseIsValidSqlOnSqlite()
    {
        var refused = new List<string>();

        foreach (StressCase stressCase in StressCorpus.Cases)
        {
            StressOutcome outcome = SqlRunner.Run(_connection, stressCase, "Sqlite");
            _output.WriteLine($"{stressCase,-45} {outcome.Describe(stressCase.Ordered)}");

            if (outcome.Refused)
                refused.Add($"{stressCase}: {outcome.Refusal}");
        }

        Assert.True(refused.Count == 0,
            "SQLite refused these cases, which means the SQL is wrong rather than divergent:\n  "
            + string.Join("\n  ", refused));
    }
}
