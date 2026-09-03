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
}
