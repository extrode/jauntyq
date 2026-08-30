using JauntyQ.Analysis.Migrations;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.Tokens;
using Xunit;

namespace JauntyQ.Analysis.Tests;

/// <summary>
/// Pins the invariant that makes <c>MigrationParser.ReadObjectName</c>'s
/// dotted-name rejoin loop dead code, so that a tokenizer change wakes it up
/// as a failing test rather than as a live path nobody has exercised.
///
/// <para>The loop exists to rejoin an <c>Identifier . Identifier</c> sequence
/// and its comment says the tokenizer "may split <c>[dbo].[Gadgets]</c>". It
/// does not: <see cref="SqlTokenizer"/> fuses a qualified name into a single
/// Identifier token, spaces and bracket quoting included, so the loop's
/// condition is never true. That was verified once by probe on 2026-08-25 and
/// recorded in <c>the todo list</c> — a probe proves it on the day it is run,
/// which is what this file replaces.</para>
///
/// <para>Both halves are asserted, because either alone would be
/// misleading: the tokenizer's fusing (the reason the loop is dead) and
/// <see cref="MigrationParser"/>'s resulting name (the behaviour a consumer
/// sees). If the tokenizer ever starts splitting, the first set fails and the
/// second says whether the loop caught it.</para>
/// </summary>
[Trait("Category", "AuditRegression")]
public class QualifiedObjectNameTests
{
    /// <summary>
    /// The three spellings of one qualified name. The spaced form is included
    /// because it is the one that looks most like it ought to tokenize as three
    /// tokens, and does not.
    /// </summary>
    public static TheoryData<string> QualifiedSpellings() => new()
    {
        "dbo.gadgets",
        "[dbo].[gadgets]",
        "dbo . gadgets",
    };

    [Theory]
    [MemberData(nameof(QualifiedSpellings))]
    public void TheTokenizer_FusesAQualifiedNameIntoOneIdentifier(string spelling)
    {
        var tokens = SqlTokenizer.Tokenize(spelling);

        var identifiers = tokens.Where(t => t.Type == TokenType.Identifier).ToList();
        Assert.Single(identifiers);
        Assert.DoesNotContain(tokens, t => t.Type == TokenType.Symbol && t.Value == ".");
    }

    /// <summary>
    /// The discrimination guard for the theory above. <c>Assert.Single</c> over
    /// identifiers proves nothing unless a shape the tokenizer genuinely splits
    /// produces more than one — otherwise a tokenizer that emitted a single
    /// token for everything would pass it.
    /// </summary>
    [Fact]
    public void TheTokenizer_DoesSplitWhereItShould()
    {
        var tokens = SqlTokenizer.Tokenize("dbo, gadgets");

        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.Identifier));
    }

    [Theory]
    [MemberData(nameof(QualifiedSpellings))]
    public void CreateTable_TakesTheBareNameFromAQualifiedName(string spelling)
    {
        var stmt = Assert.Single(MigrationParser.Parse($"create table {spelling} (id int primary key)"));

        Assert.Equal(MigrationStatementKind.CreateTable, stmt.Kind);
        Assert.Equal("gadgets", stmt.TableName);
    }

    [Theory]
    [MemberData(nameof(QualifiedSpellings))]
    public void AlterTableAddColumn_TakesTheBareNameFromAQualifiedName(string spelling)
    {
        var stmt = Assert.Single(MigrationParser.Parse($"alter table {spelling} add price int"));

        Assert.Equal(MigrationStatementKind.AddColumn, stmt.Kind);
        Assert.Equal("gadgets", stmt.TableName);
    }

    [Theory]
    [MemberData(nameof(QualifiedSpellings))]
    public void DropTable_TakesTheBareNameFromAQualifiedName(string spelling)
    {
        var stmt = Assert.Single(MigrationParser.Parse($"drop table {spelling}"));

        Assert.Equal(MigrationStatementKind.DropTable, stmt.Kind);
        Assert.Equal("gadgets", stmt.TableName);
    }

    /// <summary>
    /// A three-part name, since SQL Server permits <c>db.schema.object</c> and
    /// the bare-name rule is "everything after the last dot" rather than
    /// "the second part".
    /// </summary>
    [Fact]
    public void AThreePartName_ResolvesToItsLastSegment()
    {
        var stmt = Assert.Single(MigrationParser.Parse("create table mydb.dbo.gadgets (id int primary key)"));

        Assert.Equal("gadgets", stmt.TableName);
    }

    /// <summary>
    /// The false-positive guard. An unqualified name has no dot to strip, and a
    /// rule that stripped anything here would silently rename every table in a
    /// migration file that uses no schema prefix — which is most of them.
    /// </summary>
    [Fact]
    public void AnUnqualifiedName_IsUnchanged()
    {
        var stmt = Assert.Single(MigrationParser.Parse("create table gadgets (id int primary key)"));

        Assert.Equal("gadgets", stmt.TableName);
    }
}
