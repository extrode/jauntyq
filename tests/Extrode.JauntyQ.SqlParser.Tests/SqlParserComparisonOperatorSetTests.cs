using Extrode.JauntyQ.SqlParser;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class SqlParserComparisonOperatorSetTests
{
    [Theory]
    [InlineData("=")]
    [InlineData("!=")]
    [InlineData("<>")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData(">=")]
    public void EveryComparisonOperator_BindsTheParameterToItsColumn(string op)
    {
        var model = SqlParser.Parse(SqlTokenizer.Tokenize($"SELECT * FROM t WHERE a {op} @p"), "Q");

        var p = Assert.Single(model.Parameters);
        Assert.Equal("a", p.BoundColumnName);
        Assert.Equal(op, p.ComparisonOp);
    }
}
