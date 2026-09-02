using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using JauntyQ.TestInfra;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// <see cref="FixtureGate.SkipReasonOrThrow"/> used to soft-skip on ANY exception
/// when not under CI. A container that timed out because the Docker host was
/// saturated therefore produced the message "Docker unavailable" while Docker was
/// plainly up with 30 other containers running, and the assembly reported success
/// having run nothing. Measured 2026-07-30: two full-solution runs on the same
/// commit skipped 66 and 11 of the same 2960 tests, both green.
///
/// So the gate now classifies. Only a genuinely absent daemon skips; every other
/// failure rethrows. These tests pin both arms, because getting either wrong is
/// silent -- too strict and local development stops working without Docker, too
/// loose and the suite goes back to lying about what it ran.
///
/// 2026-08-17: the classification had one hole left, and it was on this side of
/// the line rather than in the gate -- a wrapped SocketException 10061 sat in
/// AbsentDaemon below, pinning the wrong answer. "Actively refused" describes a
/// port that was not listening and says nothing about WHICH port, so a container's
/// mapped port refusing read as the daemon being gone. Eight full-solution runs on
/// one commit: seven ran everything, one skipped 101 tests across three assemblies
/// and exited 0. A refusal now has to name the daemon's own endpoint to count.
/// </summary>
public class FixtureGateSkipClassificationTests
{
    // Guard: these assertions only mean anything off CI. Under GITHUB_ACTIONS the
    // gate rethrows unconditionally, so the "skips" arm below would fail for the
    // wrong reason and read as a real regression.
    private static bool OffCi => !FixtureGate.StrictCi;

    public static TheoryData<Exception> AbsentDaemon() => new()
    {
        // Testcontainers' AbstractBuilder.Validate(), the ordinary
        // "Docker Desktop is not running" path on a developer machine.
        new ArgumentException(
            "Docker is either not running or misconfigured", "DockerEndpointAuthConfig"),
        new InvalidOperationException("Cannot connect to the Docker daemon at unix:///var/run/docker.sock"),
        // Wrapped one level down: the real ones arrive nested. A refusal counts
        // only when the chain also names the endpoint that refused -- here the
        // Windows named pipe, which no container can be behind.
        new InvalidOperationException("Failed to dial npipe://./pipe/docker_engine",
            new SocketException(10061, "No connection could be made because the target machine actively refused it")),
        new AggregateException(
            new InvalidOperationException("/var/run/docker.sock: The system cannot find the file specified.")),
        new InvalidOperationException("connect tcp 127.0.0.1:2375: connection refused"),
    };

    public static TheoryData<Exception> RealFailure() => new()
    {
        // The one that matters: a lost race on a saturated host.
        new TimeoutException("The container startup timed out after 60 seconds."),
        new OperationCanceledException("bring-up cancelled"),
        // SQL Server under memory pressure, which is what 34 concurrent
        // containers actually produced.
        new InvalidOperationException(
            "There is insufficient system memory in resource pool 'default' to run this query."),
        new InvalidOperationException("manifest for mysql:8.0 not found: manifest unknown"),
        new ArgumentException("some other argument was bad", "someOtherParam"),

        // The 2026-08-17 correction, and the case this file previously had on
        // the WRONG side. A refused connection naming no endpoint is a port
        // that was not listening, and under a full-solution run that port is
        // overwhelmingly a container's -- ryuk's mapped port, dialled the
        // instant after it starts. Classified as an absent daemon it took three
        // whole assemblies (101 tests) out of a run that then reported success.
        new InvalidOperationException("bring-up failed",
            new SocketException(10061, "No connection could be made because the target machine actively refused it")),
        // The same thing on Linux/WSL, and the shape a database container that
        // is up but not yet accepting connections produces.
        new InvalidOperationException("Connection refused 127.0.0.1:55416"),
        new AggregateException(
            new InvalidOperationException("The system cannot find the file specified.")),
    };

    [SkippableTheory]
    [MemberData(nameof(AbsentDaemon))]
    public void AnAbsentDaemon_Skips(Exception ex)
    {
        Skip.IfNot(OffCi, "SkipReasonOrThrow rethrows unconditionally under CI.");

        string reason = FixtureGate.SkipReasonOrThrow(ex);

        Assert.StartsWith("Docker unavailable: ", reason, StringComparison.Ordinal);
        Assert.Contains(ex.GetType().Name, reason, StringComparison.Ordinal);
    }

    [SkippableTheory]
    [MemberData(nameof(RealFailure))]
    public void AnythingElse_Rethrows_PreservingTheOriginalException(Exception ex)
    {
        Skip.IfNot(OffCi, "SkipReasonOrThrow rethrows unconditionally under CI.");

        Exception thrown = Assert.ThrowsAny<Exception>(() => FixtureGate.SkipReasonOrThrow(ex));

        // Rethrown, not wrapped: the fixture's stack must survive so the failure
        // is diagnosable rather than just loud.
        Assert.Same(ex, thrown);
    }

    /// <summary>
    /// The two arms above pin what the gate does with an exception. This pins
    /// that Testcontainers still PRODUCES the exception the skipping arm keys on,
    /// which nothing else checks and no local run would reveal.
    ///
    /// The Docker-is-off path is the one case that must skip rather than fail,
    /// and it is recognised by two markers owned by a third party: the
    /// <c>DockerEndpointAuthConfig</c> <see cref="ArgumentException.ParamName"/>
    /// and the "Docker is either not running or misconfigured" message. Both are
    /// Testcontainers' to rename. If an upgrade renames either, every arm of
    /// <see cref="FixtureGate.SkipReasonOrThrow"/> still passes -- and every
    /// Docker-backed suite turns red on a developer machine with Docker Desktop
    /// closed, with nothing pointing at the upgrade that caused it.
    ///
    /// Verified against the shipped assembly rather than by taking the daemon
    /// down, because a bogus DOCKER_HOST does not simulate absence: measured
    /// 2026-08-17, Testcontainers 3.10.0 skips an unavailable endpoint provider
    /// and falls through to the next, so the suite ran normally.
    /// </summary>
    [Fact]
    public void Testcontainers_StillProducesTheMarkersTheSkipArmKeysOn()
    {
        // IContainer, not TestcontainersSettings: the latter's static constructor
        // probes every Docker endpoint, which is exactly the work this test is
        // meant to be independent of.
        Assembly testcontainers = typeof(DotNet.Testcontainers.Containers.IContainer).Assembly;

        // Validate() gets the ParamName from nameof(DockerEndpointAuthConfig), so
        // the property surviving is what keeps the gate's first branch reachable.
        bool namesTheAuthConfig = testcontainers
            .GetExportedTypes()
            .SelectMany(t => t.GetProperties())
            .Any(p => p.Name == "DockerEndpointAuthConfig");

        Assert.True(
            namesTheAuthConfig,
            $"{testcontainers.GetName().Name} {testcontainers.GetName().Version} no longer exposes a " +
            "DockerEndpointAuthConfig property, so the ArgumentException ParamName that FixtureGate " +
            "treats as an absent daemon can no longer be produced. Update IsDockerAbsent.");

        // A compiled string literal lives in the #US heap as UTF-16 and is not
        // reachable by reflection, so the assembly image is searched directly.
        byte[] image = File.ReadAllBytes(testcontainers.Location);
        byte[] message = Encoding.Unicode.GetBytes("Docker is either not running or misconfigured");

        Assert.True(
            Contains(image, message),
            $"{testcontainers.GetName().Name} {testcontainers.GetName().Version} no longer contains the " +
            "\"Docker is either not running or misconfigured\" message that FixtureGate treats as an absent " +
            "daemon. Every Docker-backed suite will now fail rather than skip when Docker is off. " +
            "Update IsDockerAbsent to the new wording.");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j])
                j++;

            if (j == needle.Length)
                return true;
        }

        return false;
    }

    [Fact]
    public void UnderCi_EvenAnAbsentDaemon_Rethrows()
    {
        // CI is guaranteed Docker, so an absent daemon there is a broken runner,
        // not an environment to accommodate. Asserted against the real env var so
        // the two arms above cannot both be vacuous on a CI machine.
        Exception ex = new ArgumentException(
            "Docker is either not running or misconfigured", "DockerEndpointAuthConfig");

        if (FixtureGate.StrictCi)
            Assert.Same(ex, Assert.ThrowsAny<Exception>(() => FixtureGate.SkipReasonOrThrow(ex)));
        else
            Assert.StartsWith("Docker unavailable: ", FixtureGate.SkipReasonOrThrow(ex), StringComparison.Ordinal);
    }
}
