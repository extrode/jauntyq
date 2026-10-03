using Xunit;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

public class ScopeFileTests
{
    [Fact]
    public void NoScopeFile_RaisesNothing()
    {
        Assert.Empty(Messages(Run(null), "JNT6004"));
    }

    [Fact]
    public void AValidFile_RaisesNothing()
    {
        Assert.Empty(Messages(Run(OrdersAndLines), "JNT6004"));
    }

    [Fact]
    public void ADroppedEntry_IsReportedWithTheFilePath()
    {
        var result = Run(@"{ ""scopes"": [ { ""table"": ""invoices"", ""column"": ""tenant_id"" } ] }");

        Assert.Equal(
            "Scope entry 1 names table 'invoices', which is not in the schema. It was dropped, so table 'invoices' is unscoped. (db/schema/jaunty.scope.json)",
            Assert.Single(Messages(result, "JNT6004")));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData(@"{ ""scope"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" } ] }")]
    [InlineData(@"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" } ], ""extra"": 1 }")]
    [InlineData(@"{ ""scopes"": null }")]
    public void AnUnreadableFile_ScopesNothing_AndSaysSo(string json)
    {
        var message = Assert.Single(Messages(Run(json), "JNT6004"));

        Assert.StartsWith("Scope file 'db/schema/jaunty.scope.json' could not be read (", message);
        Assert.EndsWith("). Every table is unscoped until it is fixed. (db/schema/jaunty.scope.json)", message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(@"{ ""scopes"": [] }")]
    public void AFileWithNoEntries_IsReported(string json)
    {
        Assert.Equal(
            "The scope file declares no entries under \"scopes\", so every table is unscoped. (db/schema/jaunty.scope.json)",
            Assert.Single(Messages(Run(json), "JNT6004")));
    }

    [Fact]
    public void ANullEntry_IsDropped_AndTheOthersStillScope()
    {
        var result = Run(@"{ ""scopes"": [ null, { ""table"": ""orders"", ""column"": ""tenant_id"" } ] }");

        Assert.Equal(
            "Scope entry 1 is null. It was dropped, so it scopes no table. (db/schema/jaunty.scope.json)",
            Assert.Single(Messages(result, "JNT6004")));
        Assert.Empty(Messages(result, "JNT0001"));
    }

    [Fact]
    public void TwoFiles_TheOrdinalLowestWins_AndTheOthersAreNamed()
    {
        var result = Run(
            new[] { ("db/z.scope.json", "{ not json"), ("db/a.scope.json", OrdersAndLines) },
            SchemaJson, null, false);

        var messages = Messages(result, "JNT6004");
        Assert.Equal(
            "Found 2 scope files (db/a.scope.json, db/z.scope.json); using 'db/a.scope.json'. Scopes in the others are ignored entirely, they are not merged, so any table only they declare is unscoped. Keep one *.scope.json (or exclude the extras from AdditionalFiles).",
            Assert.Single(messages));
    }

    [Fact]
    public void InDdlMode_EntriesResolveAgainstTheDdlSchema()
    {
        var result = Run(
            new[] { (ScopePath, @"{ ""scopes"": [ { ""table"": ""docs"", ""column"": ""owner_id"" }, { ""table"": ""docs"", ""column"": ""nope"" } ] }") },
            null, "sqlite", false,
            ("db/ddl/0001_docs.sql", "CREATE TABLE docs (id INTEGER PRIMARY KEY, owner_id INTEGER NOT NULL);"));

        Assert.Equal(
            "Scope entry 2 names column 'nope', which table 'docs' does not have. It was dropped, so table 'docs' is unscoped by it. (db/schema/jaunty.scope.json)",
            Assert.Single(Messages(result, "JNT6004")));
    }

    [Fact]
    public void WithNoSchema_TheFileIsNotResolved()
    {
        var result = Run(new[] { (ScopePath, "{ not json") }, null, null, false);

        Assert.Empty(Messages(result, "JNT6004"));
    }
}
