using System;
using JauntyQ.TestInfra;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Audit round 12 (cell 2.11-sqlserver-testcontainers, sibling-sweep across the
/// whole Sakila sample family): live pre-round guard run hit a real Docker
/// resource-contention blip against a Sakila.SqlServer.Tests Testcontainer, and
/// it surfaced as 15 misleading <see cref="NullReferenceException"/>s instead of
/// a clean skip or an informative error, because SakilaQueriesTests.cs (and its
/// MariaDb/MySql/Postgres siblings) use plain <c>[Fact]</c> against a fixture
/// whose <c>Db</c> stayed null, rather than gating with <c>[SkippableFact]</c> +
/// <c>Skip.IfNot(Available, SkipReason)</c> like every other Docker-backed
/// sample in the repo (Conduit/EShopOnWeb/AdventureWorksLite/Northwind/Postgres/
/// MySql all do). Retrofitting <c>[SkippableFact]</c> onto those 60 pre-existing
/// test methods is out of scope for a pure-addition audit round, so the actual
/// fix (<see cref="FixtureGate.RequireAvailable{T}"/>, now used by all four
/// Sakila fixtures' <c>Db</c> property) only closes the "silence" half: it makes
/// the still-real failure legible (naming the real SkipReason) instead of a bare
/// NRE. This directly unit-tests that guard.
/// </summary>
[Trait("Category", "AuditRegression")]
public class FixtureGateRequireAvailableTests
{
    private sealed class Widget { }

    [Fact]
    public void RequireAvailable_WhenAvailableAndNonNull_ReturnsValue()
    {
        var widget = new Widget();

        var result = FixtureGate.RequireAvailable(widget, available: true, skipReason: null, fixtureName: "WidgetFixture");

        Assert.Same(widget, result);
    }

    [Fact]
    public void RequireAvailable_WhenUnavailable_ThrowsInvalidOperationExceptionNamingRealReason()
    {
        // This is the exact shape of the live bug: a fixture whose Testcontainer
        // failed to start leaves its resource null. Before this fix, a caller
        // touching that resource got a bare NullReferenceException with no clue
        // why. RequireAvailable must throw something that actually says why --
        // never NullReferenceException, and the message must carry the real
        // SkipReason through, not swallow it the way the pre-fix Sakila fixtures
        // effectively did.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixtureGate.RequireAvailable<Widget>(
                value: null,
                available: false,
                skipReason: "Docker unavailable: DockerException: connection refused",
                fixtureName: "WidgetFixture"));

        Assert.Contains("WidgetFixture", ex.Message);
        Assert.Contains("Docker unavailable: DockerException: connection refused", ex.Message);
    }

    [Fact]
    public void RequireAvailable_WhenUnavailable_NeverThrowsNullReferenceException()
    {
        // The whole point of the fix: swap a confusing NRE for a legible error.
        var ex = Record.Exception(() =>
            FixtureGate.RequireAvailable<Widget>(null, available: false, skipReason: "x", fixtureName: "WidgetFixture"));

        Assert.NotNull(ex);
        Assert.IsNotType<NullReferenceException>(ex);
    }

    [Fact]
    public void RequireAvailable_WhenAvailableTrueButValueStillNull_Throws()
    {
        // Defensive: Available and a null resource should never coexist in a
        // correctly-written fixture, but if they ever did, this must still
        // throw an informative exception rather than let a null through to
        // the caller (which is exactly how the original bug manifested).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixtureGate.RequireAvailable<Widget>(null, available: true, skipReason: null, fixtureName: "WidgetFixture"));

        Assert.Contains("WidgetFixture", ex.Message);
    }

    [Fact]
    public void RequireAvailable_WhenUnavailable_WithNullSkipReason_StillProducesReadableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixtureGate.RequireAvailable<Widget>(null, available: false, skipReason: null, fixtureName: "WidgetFixture"));

        Assert.Contains("WidgetFixture", ex.Message);
        Assert.Contains("reason unknown", ex.Message);
    }
}
