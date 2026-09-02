using JauntyQ.Schema;
using JauntyQ.Schema.Extraction;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Spec 013 / T3: parsing MySQL's COLUMN_TYPE into enum members. MySQL has no
/// named enum type, so this string is the only place the member list exists —
/// if the parser is wrong the members are wrong everywhere downstream, with
/// nothing to cross-check against.
///
/// The awkward inputs here are not invented. MySQL 8.4.10 reports exactly
/// <c>enum('a,b','it''s','','in-progress')</c> for a column declared with
/// those four members (probed live, spec 013 T0), which is why splitting on
/// ',' is not an option.
/// </summary>
public class MySqlEnumParsingTests
{
    [Fact]
    public void ParsesAnOrdinaryMemberListInDeclarationOrder()
    {
        var members = MySqlExtractor.ParseEnumMembers("enum('pending','shipped','canceled')");

        Assert.Equal(new[] { "pending", "shipped", "canceled" }, members);
    }

    [Fact]
    public void AMemberMayContainAComma()
    {
        var members = MySqlExtractor.ParseEnumMembers("enum('a,b','c')");

        Assert.Equal(new[] { "a,b", "c" }, members);
    }

    [Fact]
    public void ADoubledQuoteIsOneLiteralQuote()
    {
        var members = MySqlExtractor.ParseEnumMembers("enum('it''s','ok')");

        Assert.Equal(new[] { "it's", "ok" }, members);
    }

    [Fact]
    public void TheEmptyMemberIsAMemberNotAnAbsence()
    {
        // ENUM('') is legal MySQL, and '' looks exactly like the start of an
        // escape until the next character is read.
        var members = MySqlExtractor.ParseEnumMembers("enum('a','','b')");

        Assert.Equal(new[] { "a", "", "b" }, members);
    }

    [Fact]
    public void ParsesTheFullAwkwardListObservedLive()
    {
        var members = MySqlExtractor.ParseEnumMembers("enum('a,b','it''s','','in-progress')");

        Assert.Equal(new[] { "a,b", "it's", "", "in-progress" }, members);
    }

    [Theory]
    [InlineData("")]
    [InlineData("varchar(50)")]
    [InlineData("enum")]
    [InlineData("enum(")]
    public void MalformedOrNonEnumInputYieldsNoMembersRatherThanThrowing(string columnType)
    {
        Assert.Empty(MySqlExtractor.ParseEnumMembers(columnType));
    }

    [Fact]
    public void CaptureMintsAnEntityNamedForTableAndColumn()
    {
        var schema = new DatabaseSchema { Dialect = "mysql" };

        string? name = MySqlExtractor.CaptureInlineEnum(
            schema, "orders", "status", "enum", "enum('pending','shipped')");

        Assert.Equal("OrdersStatus", name);
        Assert.Equal(new[] { "pending", "shipped" }, schema.Enums["OrdersStatus"].Members.Select(m => m.Value));
        Assert.Equal(new[] { "Pending", "Shipped" }, schema.Enums["OrdersStatus"].Members.Select(m => m.CSharpName));
    }

    [Fact]
    public void TwoColumnsWithIdenticalMemberListsStayTwoEntities()
    {
        // Merging them would make the emitted type name depend on which
        // column the extractor happened to reach first.
        var schema = new DatabaseSchema { Dialect = "mysql" };

        MySqlExtractor.CaptureInlineEnum(schema, "orders", "status", "enum", "enum('a','b')");
        MySqlExtractor.CaptureInlineEnum(schema, "users", "status", "enum", "enum('a','b')");

        Assert.Equal(2, schema.Enums.Count);
        Assert.True(schema.Enums.ContainsKey("OrdersStatus"));
        Assert.True(schema.Enums.ContainsKey("UsersStatus"));
    }

    [Fact]
    public void SetIsNotCapturedAndKeepsItsStringMapping()
    {
        // FR-011. SET is multi-valued and out of scope for spec 013.
        var schema = new DatabaseSchema { Dialect = "mysql" };

        string? name = MySqlExtractor.CaptureInlineEnum(
            schema, "orders", "tags", "set", "set('a','b')");

        Assert.Null(name);
        Assert.Empty(schema.Enums);
    }

    [Fact]
    public void ANonEnumColumnIsNotCaptured()
    {
        var schema = new DatabaseSchema { Dialect = "mysql" };

        Assert.Null(MySqlExtractor.CaptureInlineEnum(schema, "orders", "note", "varchar", "varchar(50)"));
        Assert.Empty(schema.Enums);
    }
}
