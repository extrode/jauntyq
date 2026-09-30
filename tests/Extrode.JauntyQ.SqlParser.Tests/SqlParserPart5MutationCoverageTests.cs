using System.Linq;
using Extrode.JauntyQ.SqlParser;
using Extrode.JauntyQ.SqlParser.IR;
using Extrode.JauntyQ.SqlParser.Tokens;
using Xunit;

namespace Extrode.JauntyQ.SqlParser.Tests;

/// <summary>
/// Direct coverage for SqlParser.Part5.cs mutation survivors: INSERT's
/// target-table Alias default, INSERT...SELECT's WHERE/RETURNING pass-through
/// and its FROM/JOIN continuation loop (including the RETURNING-terminator
/// check that must not let a RETURNING-clause subquery's own FROM corrupt the
/// source table list), the VALUES-slot and SELECT-list paren-depth trackers
/// that gate comma splitting, a bare positive numeric VALUES literal, and
/// UPDATE's table-name guard and SET-clause subquery depth tracking.
/// </summary>
[Trait("Category", "AuditRegression")]
public class SqlParserPart5MutationCoverageTests
{
    private static QueryModel ParseSql(string sql, string name = "TestQuery")
        => SqlParser.Parse(SqlTokenizer.Tokenize(sql), name);

    // ── ParseInsert: target table ────────────────────────────────────

    [Fact]
    public void Insert_TargetTableRef_AliasIsEmptyString()
    {
        var model = ParseSql("INSERT INTO Products (ProductName) VALUES (@ProductName)");

        Assert.Equal(string.Empty, model.Tables[0].Alias);
    }

    // ── INSERT...SELECT: WHERE/RETURNING pass-through ────────────────

    [Fact]
    public void InsertSelect_WhereClauseParamOnSourceTable_IsBound()
    {
        var model = ParseSql(
            "insert into products (id) select id from categories where category_id = @cid");

        var p = model.Parameters.Single(x => x.Name == "cid");
        Assert.Equal("category_id", p.BoundColumnName);
    }

    [Fact]
    public void InsertSelect_ReturningClause_PopulatesReturning()
    {
        var model = ParseSql(
            "insert into products (id) select id from categories returning id");

        Assert.True(model.HasReturning);
        Assert.Single(model.Returning);
        Assert.Equal("id", model.Returning[0].ColumnName);
    }

    [Fact]
    public void InsertSelect_ReturningNotDuplicatedByFallThroughAfterSelectBranch()
    {
        // ParseInsert's SELECT branch must return once it has handled
        // WHERE-binding and RETURNING for the INSERT...SELECT form -- falling
        // through into the VALUES-tuple path afterward would call
        // ExtractReturning a second time and duplicate every RETURNING item.
        var model = ParseSql(
            "insert into products (id) select id from categories returning id");

        Assert.Single(model.Returning);
    }

    // ── INSERT...SELECT: FROM/JOIN continuation loop ─────────────────

    [Fact]
    public void InsertSelect_CapturesSourceTableFromSelectBody()
    {
        var model = ParseSql(
            "insert into products (id) select cat.category_id from categories cat");

        Assert.Equal(2, model.Tables.Count);
        Assert.Contains(model.Tables, t => t.TableName == "products");
        Assert.Contains(model.Tables, t => t.TableName == "categories" && t.Alias == "cat");
    }

    [Fact]
    public void InsertSelect_SubqueryInReturningDoesNotCorruptSourceTables()
    {
        // The FROM/JOIN continuation loop must stop at RETURNING rather than
        // keep scanning into it: a RETURNING-clause scalar subquery carries
        // its own unrelated FROM, which must never be mistaken for this
        // SELECT's own FROM continuation and merged into the source tables.
        var model = ParseSql(
            "insert into products (id) select id from categories " +
            "returning (select count(*) from orders) as cnt");

        Assert.Equal(2, model.Tables.Count);
        Assert.Contains(model.Tables, t => t.TableName == "products");
        Assert.Contains(model.Tables, t => t.TableName == "categories");
        Assert.DoesNotContain(model.Tables, t => t.TableName == "orders");
    }

    // ── BindInsertSelectParams: paren depth / clause-keyword boundary ──

    [Fact]
    public void InsertSelect_AliasKeywordInSelectList_DoesNotPrematurelyStopParamBinding()
    {
        // "AS" is a Keyword token but not a clause keyword: the select-list
        // scan must keep going past it rather than treating it like a
        // FROM/WHERE-style clause boundary, or every item after it (including
        // a lone-@param slot due for positional binding) is silently dropped.
        var model = ParseSql(
            "insert into products (product_id, product_name) " +
            "select category_id AS cid, @name from categories");

        var p = model.Parameters.Single(x => x.Name == "name");
        Assert.Equal("product_name", p.BoundColumnName);
    }

    [Fact]
    public void InsertSelect_ParenExpressionBeforeParam_TracksDepthForCommaSplit()
    {
        // coalesce(x, 0) carries its own internal comma at paren-depth 1: the
        // scan must not treat it as the top-level slot separator, or the
        // following lone-@param item is bound to the wrong column index.
        var model = ParseSql(
            "insert into products (a, b) select coalesce(x, 0), @p from t");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("b", p.BoundColumnName);
    }

    [Fact]
    public void InsertSelect_ExtraSelectItemBeyondColumnList_NotBound()
    {
        // The INSERT has only one column, so the second select-list item's
        // colIndex (1) is out of range for insertColumns (Count 1). Flush's
        // bounds check must exclude the boundary itself, or this indexes
        // insertColumns out of range.
        var model = ParseSql("insert into t (a) select x, @extra from t2");

        var extra = model.Parameters.Single(p => p.Name == "extra");
        Assert.False(extra.IsWriteTarget);
        Assert.Equal(string.Empty, extra.BoundColumnName);
    }

    [Fact]
    public void InsertSelect_SameParamTwiceInSelectList_FirstBindingWins()
    {
        var model = ParseSql("insert into t (a, b) select @p, @p from t2");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("a", p.BoundColumnName);
    }

    // ── BindInsertSlot: VALUES-tuple paren depth / literal kind ──────

    [Fact]
    public void InsertValues_NestedFunctionCallSlot_TracksParenDepthForCommaSplitting()
    {
        // foo(1, 2) carries its own internal comma at paren-depth 1 inside a
        // VALUES tuple: without depth tracking on "(" it's mistaken for the
        // top-level slot separator, corrupting every column index after it.
        var model = ParseSql(
            "insert into t (a, b) values (foo(1, 2), @p)");

        var p = model.Parameters.Single(x => x.Name == "p");
        Assert.Equal("b", p.BoundColumnName);
        Assert.True(p.IsWriteTarget);
    }

    [Fact]
    public void InsertValues_PositiveNumberLiteral_BoundAsNumberKind()
    {
        var model = ParseSql("insert into t (a) values (5)");

        var lit = Assert.Single(model.Literals);
        Assert.Equal(LiteralKind.Number, lit.Kind);
        Assert.Equal("5", lit.Value);
        Assert.Equal("a", lit.BoundColumnName);
    }

    // ── ParseUpdate: table-name guard / SET-clause subquery depth ────

    [Fact]
    public void Update_MissingTableName_DoesNotFabricateTargetFromNextToken()
    {
        // Malformed: no table name between UPDATE and SET. The identifier
        // guard must actually gate on token type rather than always fire,
        // or the SET keyword token itself gets recorded as the target table.
        var model = ParseSql("UPDATE SET x = @p WHERE y = @q");

        Assert.DoesNotContain(model.Tables, t => t.TableName == "SET");
    }

    [Fact]
    public void Update_CommaInsideSetClauseSubquery_DoesNotPrematurelyResetDepth()
    {
        // The SET-clause subquery's own top-level comma (inside its one pair
        // of parens) must not be mistaken for the outer paren-depth tracker's
        // closing paren: doing so resets depth to 0 while still logically
        // inside the subquery, so the subquery's own nested WHERE is then
        // mistaken for the UPDATE's own top-level WHERE and the scan breaks
        // early -- silently dropping the write-target mark on every SET
        // assignment still to come, including last_editor further along.
        var model = ParseSql(
            "update products set total = (select p2.a, p2.b from products p2 where p2.id = @sub), " +
            "last_editor = @editor where product_id = @id");

        var editor = model.Parameters.Single(x => x.Name == "editor");
        Assert.True(editor.IsWriteTarget);
    }
}
