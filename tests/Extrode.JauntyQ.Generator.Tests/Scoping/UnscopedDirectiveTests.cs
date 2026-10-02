using Extrode.JauntyQ.Generator.Directives;
using Xunit;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

public class UnscopedDirectiveTests
{
    private const string Unproven = "select o.id from orders o where o.note = @note";
    private const string Proven = "select o.id from orders o where o.tenant_id = @tenantId";

    [Fact]
    public void AReason_AcceptsTheFilesUnprovenReaches()
    {
        var result = Run(OrdersAndLines, ("db/Orders/GetByNote.sql", "-- @unscoped support looks up a note across tenants\n" + Unproven));

        Assert.Empty(Messages(result, "JNT4005"));
        Assert.Empty(Messages(result, "JNT4006"));
        Assert.True(Emits(result, "Orders.GetByNote.g.cs"));
    }

    [Fact]
    public void ABareDirective_AcceptsNothing()
    {
        var result = Run(OrdersAndLines, ("db/Orders/GetByNote.sql", "-- @unscoped\n" + Unproven));

        Assert.Single(Messages(result, "JNT4005"));
        Assert.Single(Messages(result, "JNT3008"));
    }

    [Fact]
    public void ADirective_CoversItsOwnFileOnly()
    {
        var result = Run(OrdersAndLines,
            ("db/Orders/GetByNote.sql", "-- @unscoped support looks up a note across tenants\n" + Unproven),
            ("db/Orders/FindByNote.sql", Unproven));

        Assert.Single(Messages(result, "JNT4005"));
        Assert.False(Emits(result, "Orders.FindByNote.g.cs"));
    }

    [Fact]
    public void ADirectiveThatAcceptsNothing_IsReported()
    {
        var result = Run(OrdersAndLines, ("db/Orders/GetMine.sql", "-- @unscoped left over\n" + Proven));

        Assert.Equal(
            "-- @unscoped is declared but this query reaches every scoped table with its scope parameter, so it accepts nothing. "
            + "Remove the directive. (Stated reason: left over)",
            Assert.Single(Messages(result, "JNT4006")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"{ ""scopes"": [] }")]
    [InlineData(@"{ ""scopes"": [ { ""table"": ""invoices"", ""column"": ""tenant_id"" } ] }")]
    public void WithNoUsableScope_ADirectiveIsNotCalledDead(string? scopeJson)
    {
        Assert.Empty(Messages(Run(scopeJson, ("db/Orders/GetMine.sql", "-- @unscoped left over\n" + Proven)), "JNT4006"));
    }

    [Fact]
    public void AQueryThatFailedValidation_IsNotCalledDead()
    {
        var result = Run(OrdersAndLines,
            ("db/Orders/GetMine.sql", "-- @unscoped left over\nselect o.nope from orders o where o.tenant_id = @tenantId"));

        Assert.Empty(Messages(result, "JNT4006"));
    }

    [Fact]
    public void TheReason_IsRecorded_AndCountsAsADirective()
    {
        var (model, _) = DirectiveParser.Parse("-- @unscoped nightly totals\nselect 1");

        Assert.Equal("nightly totals", model.UnscopedReason);
        Assert.True(model.HasDirectives);
    }
}
