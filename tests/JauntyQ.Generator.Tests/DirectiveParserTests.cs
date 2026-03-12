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
}
