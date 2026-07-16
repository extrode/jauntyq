using System;
using System.Runtime.ExceptionServices;

namespace JauntyQ.TestInfra;

/// <summary>
/// Shared gate for Docker/Testcontainers-backed fixtures. Compiled into every
/// <c>*.Tests</c> assembly via a linked <c>&lt;Compile&gt;</c> in
/// <c>tests/Directory.Build.props</c> and <c>samples/Directory.Build.props</c>
/// (there is no shared test assembly to host it), so the CI-strictness policy
/// lives in ONE source file rather than being copy-pasted into ~30 fixtures.
/// </summary>
internal static class FixtureGate
{
    /// <summary>
    /// True when running under CI (the <c>GITHUB_ACTIONS</c> env var is set).
    /// In CI, fixture bring-up failures must be fatal: Docker is guaranteed on
    /// GitHub-hosted Linux runners, so a startup failure there is a real
    /// regression, not an "infra is absent" soft-skip. Letting it silently skip
    /// would let the whole Docker-gated matrix go green forever without running.
    /// </summary>
    public static bool StrictCi =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    /// <summary>
    /// Call from a fixture's <b>startup</b> catch block (container start /
    /// connection open only — never wrapping seed-script or product-code
    /// execution). Under CI it rethrows <paramref name="ex"/> preserving its
    /// stack (so CI fails loudly); otherwise it returns the "Docker unavailable"
    /// skip reason for the local soft-skip path.
    /// </summary>
    public static string SkipReasonOrThrow(Exception ex)
    {
        if (StrictCi)
            ExceptionDispatchInfo.Capture(ex).Throw();
        return $"Docker unavailable: {ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>
    /// Guards a fixture-exposed resource (e.g. a <c>Db</c>/<c>Schema</c>
    /// property) that is only meaningful when the backing Docker/Testcontainers
    /// bring-up succeeded. Audit round 12 (cell 2.11-sqlserver-testcontainers):
    /// found live that the entire Sakila sample family (SqlServer/MariaDb/MySql/
    /// Postgres) exposes <c>Available</c>/<c>SkipReason</c> from its fixture but
    /// its test classes use plain <c>[Fact]</c> instead of the
    /// <c>[SkippableFact]</c> + <c>Skip.IfNot(Available, SkipReason)</c> gate
    /// every sibling Docker-backed sample (Conduit/EShopOnWeb/AdventureWorksLite/
    /// Northwind/Postgres/MySql) already uses -- so a transient Testcontainers
    /// resource-contention failure (observed live: a full-solution test run
    /// starting ~15 concurrent Testcontainers) surfaced as a wall of misleading
    /// <see cref="NullReferenceException"/>s off a null <c>Db</c>, hiding the
    /// real <c>SkipReason</c> instead of a clean skip or an informative error.
    /// Retrofitting <c>[SkippableFact]</c> onto pre-existing test methods is out
    /// of scope for a pure-addition audit round, so this closes only the
    /// "silence" half of the defect: route the resource behind a property that
    /// throws an <see cref="InvalidOperationException"/> naming the real
    /// <see cref="SkipReasonOrThrow"/> reason when accessed while unavailable,
    /// instead of leaving a caller to fault on a bare null. The test still
    /// fails when the container never came up (this does not skip it, and does
    /// not change pass/fail outcomes when <paramref name="available"/> is
    /// true) -- only the failure's message stops being misleading.
    /// </summary>
    public static T RequireAvailable<T>(T? value, bool available, string? skipReason, string fixtureName)
        where T : class
    {
        if (available && value is not null)
            return value;

        throw new InvalidOperationException(
            $"{fixtureName} is unavailable ({skipReason ?? "reason unknown"}). " +
            "This test class accesses the fixture's resource unconditionally; " +
            "it should gate with [SkippableFact] + Skip.IfNot(Available, SkipReason) instead.");
    }
}
