using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Pins the two halves of the version stamp against each other. A checkout must not report the
/// same version as a published package — a consumer resolved a JauntyQ package while building
/// against a local checkout 1,079 commits past its tag, and both said 0.1.0, so the packaged
/// skew that was raising JNT8004 across their repo had no version string to reveal it
/// .
///
/// The stamp is two coupled facts and this suite asserts both, because breaking either silently
/// is easy: <c>&lt;Version&gt;</c> must stay a bare literal on its own line, since publish.yml's
/// tag guard seds the first such line and compares it to the tag; and the -dev suffix must be
/// appended off a tag build and absent on one.
/// </summary>
[Trait("Category", "VersionContract")]
public class VersionStampContractTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// The exact value publish.yml's guard step computes:
    /// <c>sed -n 's/.*&lt;Version&gt;\(.*\)&lt;\/Version&gt;.*/\1/p' Directory.Build.props | head -1</c>.
    /// Reimplemented rather than shelled out so the test runs on Windows too.
    /// </summary>
    private static string PublishGuardVersion()
    {
        string path = Path.Combine(RepoRoot(), "Directory.Build.props");
        Assert.True(File.Exists(path), "'" + path + "' is missing; this suite cannot pass vacuously.");

        foreach (string line in File.ReadAllLines(path))
        {
            Match m = Regex.Match(line, @"<Version>(?<version>.*)</Version>");
            if (m.Success)
                return m.Groups["version"].Value;
        }

        throw new Xunit.Sdk.XunitException("no <Version> element in Directory.Build.props; publish.yml's tag guard would compare the tag against an empty string and every tag would fail to publish.");
    }

    [Fact]
    public void ThePublishGuardStillReadsABareSemver()
    {
        string guarded = PublishGuardVersion();

        Assert.Matches(@"^\d+\.\d+\.\d+$", guarded);
    }

    [Fact]
    public void TheStampedVersionIsTheGuardedOnePlusDev_ExceptOnATagBuild()
    {
        string guarded = PublishGuardVersion();
        bool isTagBuild = string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_REF_TYPE"), "tag", StringComparison.Ordinal);

        string stamped = typeof(JauntyQGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        // SourceLink appends +<commit sha> to the informational version.
        int plus = stamped.IndexOf('+');
        if (plus >= 0)
            stamped = stamped.Substring(0, plus);

        Assert.Equal(isTagBuild ? guarded : guarded + "-dev", stamped);
    }
}
