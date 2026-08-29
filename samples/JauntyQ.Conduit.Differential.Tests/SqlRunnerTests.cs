using Microsoft.Data.Sqlite;
using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class SqlRunnerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqlRunnerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private static StressCase Case(string sql, Dictionary<string, string>? overrides = null, bool ordered = false) =>
        new()
        {
            Name = "case",
            Category = "test",
            Sql = sql,
            SqlByEngine = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Ordered = ordered,
            Groups = [["Sqlite"]],
            Reason = "reason",
            Guidance = "guidance",
        };

    [Fact]
    public void RowsComeBackByOrdinal()
    {
        StressOutcome outcome = SqlRunner.Run(_connection, Case("SELECT 1 AS a, 'x' AS b"), "Sqlite");

        Assert.False(outcome.Refused);
        var row = Assert.Single(outcome.Rows!);
        Assert.Equal(2, row.Count);
        Assert.Equal(1L, row[0]);
        Assert.Equal("x", row[1]);
    }

    [Fact]
    public void ANullColumnComesBackAsNullNotDbNull()
    {
        StressOutcome outcome = SqlRunner.Run(_connection, Case("SELECT NULL AS a"), "Sqlite");

        Assert.Null(outcome.Rows![0][0]);
    }

    [Fact]
    public void AQueryReturningNoRowsIsNotARefusal()
    {
        StressOutcome outcome = SqlRunner.Run(_connection, Case("SELECT 1 AS a WHERE 1 = 0"), "Sqlite");

        Assert.False(outcome.Refused);
        Assert.Empty(outcome.Rows!);
    }

    [Fact]
    public void ARefusalBecomesAnOutcomeRatherThanAnException()
    {
        StressOutcome outcome = SqlRunner.Run(_connection, Case("SELECT nonsense FROM nowhere"), "Sqlite");

        Assert.True(outcome.Refused);
        Assert.NotEmpty(outcome.Refusal!);
    }

    [Fact]
    public void TwoRefusalsGroupTogetherWhateverTheProvidersSay()
    {
        StressOutcome one = SqlRunner.Run(_connection, Case("SELECT nonsense FROM nowhere"), "Sqlite");
        StressOutcome two = SqlRunner.Run(_connection, Case("SELECT * FROM other_missing_table"), "Sqlite");

        Assert.NotEqual(one.Refusal, two.Refusal);
        Assert.Equal(one.GroupKey(false), two.GroupKey(false));
    }

    [Fact]
    public void ARefusalIsDescribedWithItsMessageEvenThoughItGroupsWithout()
    {
        StressOutcome outcome = SqlRunner.Run(_connection, Case("SELECT nonsense FROM nowhere"), "Sqlite");

        Assert.Contains("refused", outcome.Describe(false), StringComparison.Ordinal);
        Assert.NotEqual(outcome.GroupKey(false), outcome.Describe(false));
    }

    [Fact]
    public void APerEngineOverrideIsPreferredForThatEngine()
    {
        var stressCase = Case("SELECT 1 AS a", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sqlite"] = "SELECT 2 AS a",
        });

        Assert.Equal(2L, SqlRunner.Run(_connection, stressCase, "Sqlite").Rows![0][0]);
    }

    [Fact]
    public void AnEngineWithoutAnOverrideTakesTheDefaultSql()
    {
        var stressCase = Case("SELECT 1 AS a", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Postgres"] = "SELECT 2 AS a",
        });

        Assert.Equal(1L, SqlRunner.Run(_connection, stressCase, "Sqlite").Rows![0][0]);
    }
}
