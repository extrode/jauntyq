using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Regression coverage for CodeEmitter.BuildIdentityInsertSql's SQL Server
/// branch, which splices "output inserted.x" in just before the VALUES
/// keyword. FindValuesKeyword must never match "values" inside a comment or
/// quoted region -- doing so corrupts the comment/identifier into live SQL.
/// </summary>
public class CodeEmitterIdentityInsertTests
{
    [Fact]
    public void SqlServer_PlainInsert_SplicesOutputBeforeValues()
    {
        string sql = "insert into products (name) values (@name)";

        string result = CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "product_id");

        Assert.Equal("insert into products (name) OUTPUT INSERTED.product_id\nvalues (@name)", result);
    }

    [Fact]
    public void SqlServer_LineCommentContainingValues_NotSplicedInto()
    {
        // Regression: "values" inside a "--" comment must not be mistaken
        // for the VALUES keyword -- splicing OUTPUT mid-comment would break
        // the comment in two, and its tail ("here") becomes live SQL text
        // right before the real VALUES clause.
        string sql = "insert into products (name) -- restore old values here\nvalues (@name)";

        string result = CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "product_id");

        Assert.Equal(
            "insert into products (name) -- restore old values here\nOUTPUT INSERTED.product_id\nvalues (@name)",
            result);
    }

    [Fact]
    public void SqlServer_BlockCommentContainingValues_NotSplicedInto()
    {
        string sql = "insert into products (name) /* seed values below */ values (@name)";

        string result = CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "product_id");

        Assert.Equal(
            "insert into products (name) /* seed values below */ OUTPUT INSERTED.product_id\nvalues (@name)",
            result);
    }

    [Fact]
    public void SqlServer_BracketedIdentifierNamedValues_NotMatchedAsKeyword()
    {
        string sql = "insert into products ([values]) values (@name)";

        string result = CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "product_id");

        Assert.Equal("insert into products ([values]) OUTPUT INSERTED.product_id\nvalues (@name)", result);
    }

    [Theory]
    [InlineData("values (1)", "|values (1)")]
    [InlineData("insert into t default values", "insert into t default |values")]
    [InlineData("[a] values (1)", "[a] |values (1)")]
    [InlineData("-- c\nvalues (1)", "-- c\n|values (1)")]
    [InlineData("/* c */values (1)", "/* c */|values (1)")]
    [InlineData("insert into t (a-b) values (1)", "insert into t (a-b) |values (1)")]
    [InlineData("insert into t (a*b) values (1)", "insert into t (a*b) |values (1)")]
    [InlineData("insert into t (a) /**/-- old values\nvalues (1)", "insert into t (a) /**/-- old values\n|values (1)")]
    [InlineData("insert into t (a) /* a*b/c values */ values (1)", "insert into t (a) /* a*b/c values */ |values (1)")]
    [InlineData("insert into t ([a values], \"b values\", 'c values') values (1)", "insert into t ([a values], \"b values\", 'c values') |values (1)")]
    [InlineData("insert into t (xvalues, x_values, @values) values (1)", "insert into t (xvalues, x_values, @values) |values (1)")]
    [InlineData("insert into t ([ab values], [abc values], \"values\", 'values') values (1)", "insert into t ([ab values], [abc values], \"values\", 'values') |values (1)")]
    [InlineData("insert into t (a,      valuesx,      values_) values (1)", "insert into t (a,      valuesx,      values_) |values (1)")]
    public void SqlServer_SplicesOutputBeforeTheValuesKeywordOnly(string sql, string expected)
    {
        string result = CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "id");

        Assert.Equal(expected.Replace("|", "OUTPUT INSERTED.id\n"), result);
    }

    [Theory]
    [InlineData("insert into t select 1")]
    [InlineData("insert into t select 1 -- no values here")]
    [InlineData("insert into t select 1 /* no values here")]
    [InlineData("insert into t select 1 /* no values *")]
    [InlineData("insert into t select 1 -")]
    [InlineData("insert into t select 1 /")]
    [InlineData("insert into t select 'no values here")]
    public void SqlServer_NoValuesKeyword_ReturnsSqlUnchanged(string sql)
    {
        Assert.Equal(sql, CodeEmitter.BuildIdentityInsertSql(sql, "sqlserver", "id"));
    }
}
