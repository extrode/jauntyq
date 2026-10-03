using Microsoft.CodeAnalysis;
using Xunit;
using static Extrode.JauntyQ.Generator.Tests.Scoping.ScopeHarness;

namespace Extrode.JauntyQ.Generator.Tests.Scoping;

public class ScopeRefusalTests
{
    private static bool EmitsMethod(GeneratorDriverRunResult result, string method)
    {
        foreach (var r in result.Results)
            foreach (var s in r.GeneratedSources)
                if (s.HintName.StartsWith("Orders." + method + ".", StringComparison.Ordinal))
                    return true;
        return false;
    }

    [Fact]
    public void AnUnprovenReach_IsAnError_AndNoMethodIsEmitted()
    {
        var result = Run(OrdersAndLines, ("db/Orders/GetByNote.sql", "select o.id from orders o where o.note = @note"));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "JNT4005");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "Scoped table 'orders' (as 'o') is read in FROM without a filter on its scope column 'tenant_id'. "
            + "Add o.tenant_id = @tenantId as a top-level AND condition of the WHERE clause. "
            + "If this query must reach every tenant's rows, mark it -- @unscoped <reason>.",
            diagnostic.GetMessage());
        Assert.False(EmitsMethod(result, "GetByNote"));
    }

    [Fact]
    public void AProvenReach_IsEmitted()
    {
        var result = Run(OrdersAndLines,
            ("db/Orders/GetByNote.sql", "select o.id from orders o where o.tenant_id = @tenantId and o.note = @note"));

        Assert.Empty(Messages(result, "JNT4005"));
        Assert.True(EmitsMethod(result, "GetByNote"));
    }

    [Fact]
    public void AnUnprovenReachInAProjectionExists_IsRefused()
    {
        var result = Run(OrdersAndLines, ("db/Customers/WithOrders.sql",
            "-- @params otherId:int\nselect c.id, exists(select 1 from orders o where o.id = @otherId) as has_order from customers c where c.id = @id"));

        Assert.Contains(" in an EXISTS expression without a filter", Assert.Single(Messages(result, "JNT4005")));
    }

    [Fact]
    public void WithNoScopeFile_NothingIsChecked()
    {
        var result = Run(null, ("db/Orders/GetByNote.sql", "select o.id from orders o where o.note = @note"));

        Assert.Empty(Messages(result, "JNT4005"));
        Assert.True(EmitsMethod(result, "GetByNote"));
    }

    [Fact]
    public void OneScopedTable_IsEnoughToCheck()
    {
        var result = Run(@"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" } ] }",
            ("db/Orders/GetLines.sql", "select l.qty from orders o join order_lines l on l.order_id = o.id where o.id = @id"));

        Assert.StartsWith("Scoped table 'orders' (as 'o')", Assert.Single(Messages(result, "JNT4005")));
    }

    [Fact]
    public void EachUnprovenReach_IsItsOwnError()
    {
        var result = Run(OrdersAndLines,
            ("db/Orders/GetLines.sql", "select l.qty from orders o join order_lines l on l.order_id = o.id where o.id = @id"));

        Assert.Equal(2, Messages(result, "JNT4005").Count);
    }

    [Fact]
    public void ARefusedFile_StillHoldsItsSlot_SoTheSyntheticDoesNotReturn()
    {
        var result = Run(new[] { (ScopePath, OrdersAndLines) }, SchemaJson, null, true,
            ("db/Orders/GetAll.sql", "select id, tenant_id, note from orders"));

        Assert.StartsWith("Scoped table 'orders' is read in FROM without", Assert.Single(Messages(result, "JNT4005")));
        Assert.False(EmitsMethod(result, "GetAll"));
        Assert.True(EmitsMethod(result, "GetById"));
    }
}
