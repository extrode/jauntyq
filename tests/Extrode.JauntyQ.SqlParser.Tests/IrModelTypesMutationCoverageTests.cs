using Extrode.JauntyQ.SqlParser.IR;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

[Trait("Category", "AuditRegression")]
public class IrModelTypesMutationCoverageTests
{
    [Fact]
    public void TableRef_Defaults_AreEmptyStrings()
    {
        var t = new TableRef();

        Assert.Equal(string.Empty, t.TableName);
        Assert.Equal(string.Empty, t.Alias);
    }

    [Fact]
    public void CteRef_Defaults_NameIsEmptyString()
    {
        var c = new CteRef();

        Assert.Equal(string.Empty, c.Name);
    }

    [Fact]
    public void JoinRef_Defaults_AreEmptyStrings()
    {
        var j = new JoinRef();

        Assert.Equal(string.Empty, j.LeftTable);
        Assert.Equal(string.Empty, j.LeftColumn);
        Assert.Equal(string.Empty, j.RightTable);
        Assert.Equal(string.Empty, j.RightColumn);
    }

    [Fact]
    public void LiteralBinding_Defaults_AreEmptyStrings()
    {
        var l = new LiteralBinding();

        Assert.Equal(string.Empty, l.Value);
        Assert.Equal(string.Empty, l.BoundTableAlias);
        Assert.Equal(string.Empty, l.BoundColumnName);
    }

    [Fact]
    public void PerfHint_Defaults_AreEmptyStrings()
    {
        var p = new PerfHint();

        Assert.Equal(string.Empty, p.FunctionName);
        Assert.Equal(string.Empty, p.BoundTableAlias);
        Assert.Equal(string.Empty, p.BoundColumnName);
        Assert.Equal(string.Empty, p.Detail);
    }

    [Fact]
    public void QueryModel_Defaults_NameIsEmptyString()
    {
        var q = new QueryModel();

        Assert.Equal(string.Empty, q.Name);
    }
}
