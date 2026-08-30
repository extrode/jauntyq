using System.Text.RegularExpressions;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// The last unreached surface of AUD-R88-03. `DialectNameParityTests` in
/// JauntyQ.Cli.Tests pins five of the dialect-name sites and records three it
/// could not reach, of which the three <c>CodeEmitter</c> switches (Part4, Part6,
/// Part8) are the ones that matter most: a dialect missing from those emits
/// <em>wrong SQL</em> rather than skipping a feature.
///
/// That test's stated reason — the generator is consumed as an analyzer and is
/// not referenced by that assembly — is true of that assembly but not of this
/// one, which references the generator directly and holds its
/// <c>InternalsVisibleTo</c>. The real obstacle is narrower: all three switches
/// are <c>private</c>, so no visibility grant reaches them and they cannot be
/// invoked.
///
/// So this scans source, following <c>FixtureContainerBuildSiteTests</c>, which
/// scans for the same reason: the property is "every site names every dialect",
/// and no behavioural call can observe a site that was never written.
/// </summary>
[Trait("Category", "AuditRegression")]
public class CodeEmitterDialectParityTests
{
    /// <summary>
    /// Written out rather than read from <c>DialectMapper</c>, for the reason
    /// <c>DialectNameParityTests.Canonical</c> gives: an expectation derived from
    /// a participant cannot detect that participant changing. Adding a fifth
    /// dialect is meant to fail here until someone updates this line, which is
    /// the moment the three emitters get considered.
    /// </summary>
    private static readonly string[] Canonical = { "sqlserver", "postgres", "mysql", "sqlite" };

    /// <summary>The three files, each with the switch this pins.</summary>
    public static TheoryData<string> SwitchFiles() => new()
    {
        "CodeEmitter.Part4.cs",
        "CodeEmitter.Part6.cs",
        "CodeEmitter.Part8.cs",
    };

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string SourcePath(string file) =>
        Path.Combine(RepoRoot(), "src", "JauntyQ.Generator", file);

    /// <summary>
    /// The dialect switch alone. Part4 carries a second switch over C# type names
    /// (<c>"int"</c>, <c>"long"</c>, …) further down the same file, so a
    /// whole-file scan would read those as dialects and the assertion would be
    /// about nothing. Bounded from the <c>dialect.ToLowerInvariant()</c> /
    /// <c>dialect?.ToLowerInvariant()</c> subject to the arm that closes it —
    /// every one of the three ends in <c>default:</c> or <c>_ =&gt;</c>.
    /// </summary>
    private static string DialectSwitchRegion(string file)
    {
        string text = File.ReadAllText(SourcePath(file));

        Match subject = Regex.Match(text, @"dialect\??\.ToLowerInvariant\(\)");
        Assert.True(subject.Success, $"{file}: no dialect switch subject found");

        string after = text.Substring(subject.Index);
        Match close = Regex.Match(after, @"default\s*:|_\s*=>");
        Assert.True(close.Success, $"{file}: dialect switch has no default/discard arm to bound it");

        return after.Substring(0, close.Index);
    }

    /// <summary>
    /// Case labels and switch-expression arms in the region, as written. Requires
    /// the whole literal to be lowercase letters, which is what keeps the
    /// interpolated SQL fragments in the arm bodies ("OUTPUT INSERTED.…",
    /// "\nRETURNING …") out of the result.
    /// </summary>
    private static HashSet<string> DialectLabels(string file) =>
        Regex.Matches(DialectSwitchRegion(file), "\"([a-z]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

    [Theory]
    [MemberData(nameof(SwitchFiles))]
    public void EverySwitch_NamesEveryCanonicalDialect(string file)
    {
        var labels = DialectLabels(file);

        foreach (string dialect in Canonical)
            Assert.True(labels.Contains(dialect), $"{file}: dialect switch does not name '{dialect}'");
    }

    /// <summary>
    /// The direction that catches the drift this was written for. The positive
    /// check above still passes when a fifth dialect is added to one emitter and
    /// not the others; an exact set comparison does not.
    /// </summary>
    [Theory]
    [MemberData(nameof(SwitchFiles))]
    public void EverySwitch_NamesNothingBeyondTheCanonicalFour(string file)
    {
        Assert.Equal(Canonical.ToHashSet(), DialectLabels(file));
    }

    /// <summary>
    /// A scan that read an empty or missing region would satisfy every assertion
    /// above vacuously, so the extraction is asserted to have found real content.
    /// </summary>
    [Theory]
    [MemberData(nameof(SwitchFiles))]
    public void TheScan_ReachesRealSource(string file)
    {
        Assert.True(File.Exists(SourcePath(file)), $"{file}: not found under src/JauntyQ.Generator");
        Assert.True(DialectSwitchRegion(file).Length > 100, $"{file}: switch region suspiciously short");
        Assert.Equal(Canonical.Length, DialectLabels(file).Count);
    }
}
