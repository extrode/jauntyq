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
}
