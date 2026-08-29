using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Pins the license version numbers to a single pair of constants. The versions are restated in
/// nine places across five files — two headers, two SPDX identifiers, two in-text restatements,
/// the Generated Output Exception's statement of which texts it modifies, and the README's
/// licensing section — and nothing in the build relates them. The ISL family moved to 1.1 on
/// 2026-08-29 while JauntyQ deliberately stayed on 1.0, so a partial bump is now a live risk:
/// a package whose header says one version and whose SPDX identifier says another is a licensing
/// defect that no compiler, packer or publish step would report.
/// </summary>
[Trait("Category", "LicenseContract")]
public class LicenseContractTests
{
    /// <summary>
    /// The version of the ISL-R text in <c>LICENSE.md</c>. Bumping JauntyQ to a new ISL-R means
    /// changing this constant and then fixing every assertion that fails — that list is the
    /// checklist for the sync, and it is the reason this constant exists rather than nine literals.
    /// </summary>
    private const string IslR = "1.0";

    /// <summary>The version of the ISL-EULA text in <c>LICENSE-EULA.md</c>. See <see cref="IslR"/>.</summary>
    private const string IslEula = "1.0";

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(string relative)
    {
        string path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), "'" + path + "' is missing; this suite cannot pass vacuously.");
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>
    /// Every version this pattern captures in <paramref name="file"/> must equal
    /// <paramref name="expected"/>, and there must be at least <paramref name="atLeast"/> of them.
    /// The count is what stops a reworded file from turning an assertion into a no-op.
    /// </summary>
    private static void AllVersionsMatch(string file, string pattern, string expected, int atLeast)
    {
        string text = ReadRepoFile(file);

        List<string> found = Regex.Matches(text, pattern)
            .Select(m => m.Groups["version"].Value)
            .ToList();

        Assert.True(found.Count >= atLeast,
            file + " matched /" + pattern + "/ " + found.Count + " times, expected at least " +
            atLeast + ". The wording moved, so this assertion is no longer reading the version " +
            "it was written to guard.");

        string[] wrong = found.Where(v => v != expected).Distinct().ToArray();

        Assert.True(wrong.Length == 0,
            file + " names ISL version " + string.Join(", ", wrong) + " where the pinned version " +
            "is " + expected + ". A partial bump leaves the package claiming two different " +
            "licenses at once.");
    }

    [Fact]
    public void IslRText_NamesThePinnedVersion_InItsHeaderAndAttributionClause()
    {
        AllVersionsMatch("LICENSE.md", @"\(ISL-R\), Version (?<version>\d+\.\d+)", IslR, atLeast: 2);
    }

    [Fact]
    public void IslRText_CarriesTheMatchingSpdxIdentifier()
    {
        AllVersionsMatch("LICENSE.md", @"LicenseRef-ISL-R-(?<version>\d+\.\d+)", IslR, atLeast: 1);
    }

    [Fact]
    public void EulaText_NamesThePinnedVersion_InItsHeaderAndApplicationNotice()
    {
        AllVersionsMatch("LICENSE-EULA.md", @"\(ISL-EULA\), Version (?<version>\d+\.\d+)", IslEula, atLeast: 2);
    }

    [Fact]
    public void EulaText_CarriesTheMatchingSpdxIdentifier()
    {
        AllVersionsMatch("LICENSE-EULA.md", @"LicenseRef-ISL-EULA-(?<version>\d+\.\d+)", IslEula, atLeast: 1);
    }

    /// <summary>
    /// The Exception names both texts it modifies by version, so it does not survive a bump of
    /// either one untouched. It is the file most likely to be forgotten in a sync — it is not a
    /// license, it is a modifier of two.
    /// </summary>
    [Fact]
    public void OutputException_ModifiesThePinnedVersionsOfBothTexts()
    {
        AllVersionsMatch(
            "LICENSE-OUTPUT-EXCEPTION.md",
            @"version (?<version>\d+\.\d+) \(""ISL-R""\)|ISL-R (?<version>\d+\.\d+) template",
            IslR,
            atLeast: 2);

        AllVersionsMatch(
            "LICENSE-OUTPUT-EXCEPTION.md",
            @"version (?<version>\d+\.\d+) \(""ISL-EULA""\)",
            IslEula,
            atLeast: 1);
    }

    [Fact]
    public void Readme_NamesThePinnedVersions_InItsLicensingSection()
    {
        AllVersionsMatch("README.md", @"\(ISL-R\), Version (?<version>\d+\.\d+)", IslR, atLeast: 1);
        AllVersionsMatch("README.md", @"\(ISL-EULA\), Version (?<version>\d+\.\d+)", IslEula, atLeast: 1);
    }

    /// <summary>
    /// islamiclicense.org serves the latest text at <c>/isl-eula/</c> and freezes each release at
    /// <c>/isl-eula/N.N/</c>. The family shipped 1.1 on 2026-08-29, so an unversioned link now
    /// silently points a reader at terms JauntyQ does not ship. Every link in JauntyQ's own prose
    /// has to carry the version; the links inside the two license texts are part of the upstream
    /// wording and are excluded, because editing them would be a modification of the text.
    /// </summary>
    [Fact]
    public void ProseLinks_ToTheLicenseSite_AreVersionPinned()
    {
        string[] prose = { "README.md", "docs/01-getting-started/README.md" };

        List<string> unpinned = new();

        foreach (string file in prose)
        {
            foreach (Match link in Regex.Matches(
                ReadRepoFile(file), @"https://islamiclicense\.org/isl-(?:r|eula)/(?<tail>[^\s)]*)"))
            {
                if (!Regex.IsMatch(link.Groups["tail"].Value, @"^\d+\.\d+/?$"))
                    unpinned.Add(file + ": " + link.Value);
            }
        }

        Assert.Equal(Array.Empty<string>(), unpinned.ToArray());
    }

    /// <summary>
    /// <c>PackageLicenseFile</c> accepts one file, and the Exception is a second document that
    /// changes the terms of the first. If it stops being packed, every consumer receives a EULA
    /// that reserves rights over generated code which JauntyQ has in fact granted.
    /// </summary>
    [Fact]
    public void EveryPackage_ShipsTheEula_AndTheOutputExceptionBesideIt()
    {
        string props = ReadRepoFile("Directory.Build.props");

        Assert.Contains("<PackageLicenseFile>LICENSE-EULA.md</PackageLicenseFile>", props, StringComparison.Ordinal);

        foreach (string file in new[] { "LICENSE-EULA.md", "LICENSE-OUTPUT-EXCEPTION.md" })
        {
            Assert.Matches(
                @"<None Include=""\$\(MSBuildThisFileDirectory\)" + Regex.Escape(file) +
                @""" Pack=""true"" PackagePath=""\\"" />",
                props);
        }
    }

    /// <summary>
    /// Guards the guard: these files are read by path, so a rename or a truncation would turn
    /// every assertion above into a match against something far shorter than a license.
    /// </summary>
    [Theory]
    [InlineData("LICENSE.md", 20000)]
    [InlineData("LICENSE-EULA.md", 20000)]
    [InlineData("LICENSE-OUTPUT-EXCEPTION.md", 3000)]
    public void LicenseFiles_AreWholeDocuments(string file, int minimumBytes)
    {
        string text = ReadRepoFile(file);

        Assert.True(text.Length >= minimumBytes,
            file + " is " + text.Length + " bytes, far shorter than the document this suite was " +
            "written against; the version assertions are probably matching nothing.");
    }
}
