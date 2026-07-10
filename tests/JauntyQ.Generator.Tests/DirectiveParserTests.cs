using JauntyQ.Generator.Directives;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class DirectiveParserTests
{
    [Fact]
    public void NoDirectives_ReturnsEmptyModel()
    {
        var sql = "SELECT * FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.False(directives.HasDirectives);
        Assert.Null(directives.ResultTypeName);
        Assert.False(directives.ResultIsVoid);
        Assert.Null(directives.InlineColumns);
        Assert.Null(directives.ExplicitParams);
        Assert.Equal(sql, cleaned);
    }

    [Fact]
    public void RegularComment_NotTreatedAsDirective()
    {
        var sql = "-- This is a regular comment\nSELECT * FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.False(directives.HasDirectives);
        Assert.Contains("-- This is a regular comment", cleaned);
    }

    // ── @result void ──────────────────────────────────────

    [Fact]
    public void ResultVoid_SetsResultIsVoid()
    {
        var sql = "-- @result void\nDELETE FROM Products WHERE ProductID = @ProductID";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.ResultIsVoid);
        Assert.Null(directives.ResultTypeName);
        Assert.DoesNotContain("@result", cleaned);
        Assert.Contains("DELETE", cleaned);
    }

    [Fact]
    public void ResultVoid_CaseInsensitive()
    {
        var sql = "-- @result Void\nDELETE FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.True(directives.ResultIsVoid);
    }

    // ── @result TypeName ──────────────────────────────────

    [Fact]
    public void ResultTypeName_SetsResultTypeName()
    {
        var sql = "-- @result ProductSummary\nSELECT p.ProductId FROM Products p";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.Equal("ProductSummary", directives.ResultTypeName);
        Assert.False(directives.ResultIsVoid);
        Assert.DoesNotContain("@result", cleaned);
    }

    [Fact]
    public void ResultTypeName_WithExtraWhitespace()
    {
        var sql = "  --   @result   ProductDetail  \nSELECT * FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.Equal("ProductDetail", directives.ResultTypeName);
    }

    // ── @result (inline columns) ──────────────────────────

    [Fact]
    public void ResultInline_ParsesColumns()
    {
        var sql = "-- @result (int ProductId, string ProductName)\nSELECT ProductId, ProductName FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.InlineColumns);
        Assert.Equal(2, directives.InlineColumns!.Count);

        Assert.Equal("int", directives.InlineColumns[0].Type);
        Assert.Equal("ProductId", directives.InlineColumns[0].Name);

        Assert.Equal("string", directives.InlineColumns[1].Type);
        Assert.Equal("ProductName", directives.InlineColumns[1].Name);

        Assert.DoesNotContain("@result", cleaned);
    }

    [Fact]
    public void ResultInline_NullableTypes()
    {
        var sql = "-- @result (int? CategoryId, decimal? UnitPrice)\nSELECT CategoryId, UnitPrice FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.InlineColumns);
        Assert.Equal(2, directives.InlineColumns!.Count);
        Assert.Equal("int?", directives.InlineColumns[0].Type);
        Assert.Equal("decimal?", directives.InlineColumns[1].Type);
    }

    [Fact]
    public void ResultInline_FullyQualifiedType()
    {
        var sql = "-- @result (System.DateTime OrderDate, string Name)\nSELECT OrderDate, Name FROM Orders";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.InlineColumns);
        Assert.Equal("System.DateTime", directives.InlineColumns![0].Type);
        Assert.Equal("OrderDate", directives.InlineColumns[0].Name);
    }

    // ── @params ───────────────────────────────────────────

    [Fact]
    public void Params_ParsesSingleParam()
    {
        var sql = "-- @params CategoryId:int\nSELECT * FROM Products WHERE CategoryId = @CategoryId";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.ExplicitParams);
        Assert.Single(directives.ExplicitParams!);
        Assert.Equal("CategoryId", directives.ExplicitParams[0].Name);
        Assert.Equal("int", directives.ExplicitParams[0].CSharpType);
        Assert.DoesNotContain("@params", cleaned);
    }

    [Fact]
    public void Params_ParsesMultipleParams()
    {
        var sql = "-- @params CategoryId:int, MinPrice:decimal, Name:string\nSELECT * FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.ExplicitParams);
        Assert.Equal(3, directives.ExplicitParams!.Count);
        Assert.Equal("CategoryId", directives.ExplicitParams[0].Name);
        Assert.Equal("int", directives.ExplicitParams[0].CSharpType);
        Assert.Equal("MinPrice", directives.ExplicitParams[1].Name);
        Assert.Equal("decimal", directives.ExplicitParams[1].CSharpType);
        Assert.Equal("Name", directives.ExplicitParams[2].Name);
        Assert.Equal("string", directives.ExplicitParams[2].CSharpType);
    }

    [Fact]
    public void Params_NullableType()
    {
        var sql = "-- @params CategoryId:int?\nSELECT * FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.ExplicitParams);
        Assert.Equal("int?", directives.ExplicitParams![0].CSharpType);
    }

    // ── @proc ────────────────────────────────────────────

    [Fact]
    public void Proc_NoName_SetsIsProc()
    {
        var sql = "-- @proc\nSELECT * FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsProc);
        Assert.Null(directives.ProcName);
        Assert.True(directives.HasDirectives);
        Assert.DoesNotContain("@proc", cleaned);
        Assert.Contains("SELECT", cleaned);
    }

    [Fact]
    public void Proc_WithName_SetsProcName()
    {
        var sql = "-- @proc usp_GetProducts\nSELECT * FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsProc);
        Assert.Equal("usp_GetProducts", directives.ProcName);
        Assert.DoesNotContain("@proc", cleaned);
    }

    [Fact]
    public void Proc_StrippedFromCleanedSql()
    {
        var sql = "-- @proc CustomName\nSELECT ProductId FROM Products";
        var (_, cleaned) = DirectiveParser.Parse(sql);

        Assert.DoesNotContain("@proc", cleaned);
        Assert.DoesNotContain("CustomName", cleaned);
        Assert.Contains("SELECT", cleaned);
    }

    [Fact]
    public void Proc_CombinedWithParams()
    {
        var sql = "-- @proc\n-- @params CategoryId:int\nSELECT * FROM Products WHERE CategoryId = @CategoryId";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsProc);
        Assert.NotNull(directives.ExplicitParams);
        Assert.Single(directives.ExplicitParams!);
        Assert.DoesNotContain("@proc", cleaned);
        Assert.DoesNotContain("@params", cleaned);
    }

    [Fact]
    public void Proc_CombinedWithResult()
    {
        var sql = "-- @proc\n-- @result ProductDto\nSELECT * FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsProc);
        Assert.Equal("ProductDto", directives.ResultTypeName);
    }

    [Fact]
    public void NoProcDirective_IsProcFalse()
    {
        var sql = "SELECT * FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.False(directives.IsProc);
        Assert.Null(directives.ProcName);
    }

    // ── Multiple directives ───────────────────────────────

    [Fact]
    public void MultipleDirectives_AllParsed()
    {
        var sql = @"-- @result ProductWithCategory
-- @params CategoryId:int
SELECT p.ProductId, p.ProductName, c.CategoryName
FROM Products p
JOIN Categories c ON p.CategoryId = c.CategoryId
WHERE p.CategoryId = @CategoryId";

        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.Equal("ProductWithCategory", directives.ResultTypeName);
        Assert.NotNull(directives.ExplicitParams);
        Assert.Single(directives.ExplicitParams!);
        Assert.Equal("CategoryId", directives.ExplicitParams[0].Name);
        Assert.Equal("int", directives.ExplicitParams[0].CSharpType);

        Assert.DoesNotContain("@result", cleaned);
        Assert.DoesNotContain("@params", cleaned);
        Assert.Contains("SELECT", cleaned);
    }

    // ── @each ─────────────────────────────────────────────

    [Fact]
    public void Each_ParsesSingleParam()
    {
        var sql = "-- @each Ids\nSELECT * FROM Products WHERE ProductId IN (@Ids)";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.EachParams);
        Assert.Single(directives.EachParams!);
        Assert.Equal("Ids", directives.EachParams![0]);
        Assert.True(directives.HasDirectives);
        Assert.DoesNotContain("@each", cleaned);
        Assert.Contains("SELECT", cleaned);
    }

    [Fact]
    public void Each_Repeatable_ParsesMultipleParams()
    {
        var sql = "-- @each Ids\n-- @each CategoryIds\nSELECT * FROM Products WHERE ProductId IN (@Ids) OR CategoryId IN (@CategoryIds)";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.EachParams);
        Assert.Equal(2, directives.EachParams!.Count);
        Assert.Equal("Ids", directives.EachParams[0]);
        Assert.Equal("CategoryIds", directives.EachParams[1]);
        Assert.DoesNotContain("@each", cleaned);
    }

    [Fact]
    public void Each_CaseInsensitive()
    {
        var sql = "-- @EACH Ids\nSELECT * FROM Products WHERE ProductId IN (@Ids)";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.NotNull(directives.EachParams);
        Assert.Equal("Ids", directives.EachParams![0]);
    }

    [Fact]
    public void Each_NoDirective_EachParamsNull()
    {
        var sql = "SELECT * FROM Products WHERE ProductId IN (@Ids)";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.Null(directives.EachParams);
        Assert.False(directives.HasDirectives);
    }

    // ── Directive lines stripped from cleaned SQL ─────────

    [Fact]
    public void DirectivesStripped_RegularCommentsPreserved()
    {
        var sql = @"-- @result void
-- This is a regular comment
DELETE FROM Products WHERE ProductID = @ProductID";

        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.ResultIsVoid);
        Assert.Contains("-- This is a regular comment", cleaned);
        Assert.Contains("DELETE FROM Products", cleaned);
        Assert.DoesNotContain("@result", cleaned);
    }

    [Fact]
    public void CleanedSql_PreservesOriginalSqlStructure()
    {
        var sql = @"-- @result Products
SELECT ProductId, ProductName
FROM Products
WHERE CategoryId = @CategoryId";

        var (_, cleaned) = DirectiveParser.Parse(sql);

        Assert.StartsWith("SELECT", cleaned.TrimStart());
        Assert.Contains("FROM Products", cleaned);
        Assert.Contains("WHERE CategoryId = @CategoryId", cleaned);
    }
    // ── @first directive ──────────────────────────────────

    [Fact]
    public void First_SetsIsFirstAndIsStripped()
    {
        var sql = @"-- @first
SELECT ProductId FROM Products WHERE ProductId = @ProductId";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsFirst);
        Assert.DoesNotContain("@first", cleaned);
        Assert.Contains("SELECT ProductId", cleaned);
    }

    [Fact]
    public void First_CaseInsensitive()
    {
        var sql = @"-- @FIRST
SELECT ProductId FROM Products WHERE ProductId = @ProductId";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsFirst);
    }

    [Fact]
    public void Firstborn_DoesNotSetIsFirst()
    {
        // @firstborn is not a recognized directive -- must not trip the @first check
        var sql = @"-- @firstborn
SELECT ProductId FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.False(directives.IsFirst);
        // The line is not a directive so it is preserved in the cleaned SQL
        Assert.Contains("@firstborn", cleaned);
    }

    [Fact]
    public void ProceedComment_DoesNotSetIsProc()
    {
        // Same word-boundary rule as @firstborn: "@proceed" must not match
        // @proc (previously IsProc=true with ProcName "eed with caution",
        // ending in a baffling JNT2004).
        var sql = @"-- @proceed with caution
SELECT ProductId FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.False(directives.IsProc);
        Assert.Null(directives.ProcName);
        Assert.Contains("@proceed with caution", cleaned);
    }

    [Fact]
    public void CallerComment_DoesNotSetCallProcName()
    {
        var sql = @"-- @caller must hold a lock
SELECT ProductId FROM Products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.Null(directives.CallProcName);
        Assert.Contains("@caller must hold a lock", cleaned);
    }

    [Fact]
    public void ProcWithName_StillParses()
    {
        var sql = @"-- @proc usp_GetProducts
SELECT ProductId FROM Products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.True(directives.IsProc);
        Assert.Equal("usp_GetProducts", directives.ProcName);
    }

    [Fact]
    public void CallWithName_StillParses()
    {
        var sql = "-- @call usp_TopProducts";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.Equal("usp_TopProducts", directives.CallProcName);
    }

    // ── JNT3008: directive-lookalike comments that parsed as nothing ────

    [Fact]
    public void BareValueTakingDirective_RegistersSuspicious()
    {
        var sql = "-- @each\nselect product_id from products where product_id in (@Ids)";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.Null(directives.EachParams);
        var msg = Assert.Single(directives.SuspiciousDirectives!);
        Assert.Contains("@each requires a value", msg);
        // The line stays a plain comment in the cleaned SQL.
        Assert.Contains("-- @each", cleaned);
    }

    [Fact]
    public void OneEditTypo_RegistersSuspicious_WithSuggestion()
    {
        var sql = "-- @frist\nselect product_id from products";
        var (directives, _) = DirectiveParser.Parse(sql);

        Assert.False(directives.IsFirst);
        var msg = Assert.Single(directives.SuspiciousDirectives!);
        Assert.Contains("did you mean '-- @first'", msg);
    }

    [Fact]
    public void OrdinaryAtComments_StaySilent()
    {
        // @author / @firstborn / @copyright are ordinary comment idioms, not
        // near-misses — no warning, lines preserved.
        var sql = "-- @author sy\n-- @firstborn\n-- @copyright 2026\nselect product_id from products";
        var (directives, cleaned) = DirectiveParser.Parse(sql);

        Assert.Null(directives.SuspiciousDirectives);
        Assert.Contains("@author", cleaned);
        Assert.Contains("@firstborn", cleaned);
    }
}
