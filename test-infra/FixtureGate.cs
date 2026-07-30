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
    /// execution; <c>FixtureContainerBuildSiteTests</c> enforces that). Rethrows
    /// <paramref name="ex"/> preserving its stack under CI, and locally too
    /// unless the daemon is genuinely absent; otherwise it returns the "Docker
    /// unavailable" skip reason for the local soft-skip path.
    /// </summary>
    public static string SkipReasonOrThrow(Exception ex)
    {
        if (StrictCi || !IsDockerAbsent(ex))
            ExceptionDispatchInfo.Capture(ex).Throw();
        return $"Docker unavailable: {ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>
    /// True only when <paramref name="ex"/> says there is no Docker daemon to
    /// talk to. Everything else — a bring-up timeout, cancellation, an image
    /// pull failure, the host running out of memory — is a real failure of a run
    /// that was supposed to happen, and must not be laundered into a skip.
    ///
    /// This distinction is the whole point. Before 2026-07-30 any exception
    /// became "Docker unavailable", so a container that merely lost a race on a
    /// saturated host produced that message while Docker was plainly up with 30
    /// other containers running, and the assembly reported success. Two runs on
    /// the same commit skipped 66 and 11 of the same 2960 tests, both green.
    ///
    /// Classified from the exception chain rather than by probing the daemon:
    /// this file is linked into every test assembly (see the Directory.Build.props
    /// pair) and most of them do not reference Testcontainers or Docker.DotNet,
    /// so there is no client here to ask.
    /// </summary>
    private static bool IsDockerAbsent(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            // Testcontainers' AbstractBuilder.Validate() throws exactly this when
            // no endpoint resolves, which is the ordinary "Docker Desktop is not
            // running" path on a developer machine.
            if (e is ArgumentException { ParamName: "DockerEndpointAuthConfig" })
                return true;

            string m = e.Message;
            if (m.Contains("Docker is either not running or misconfigured", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
                || m.Contains("docker daemon is not running", StringComparison.OrdinalIgnoreCase)
                // Unix socket refused, and the Windows named pipe not existing.
                || m.Contains("No connection could be made because the target machine actively refused it", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
                || m.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
