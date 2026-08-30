using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using JauntyQ.Generator;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// A diagnostic code and a generator directive are each an <b>extensible set whose
/// membership has to be mirrored in N declaration sites that no compiler relates to
/// each other</b>. Adding one means editing the descriptor, the registry test, the
/// public-API baseline, the reference docs, and four separate count claims inside
/// the audit criteria — and nothing but a human checked the last five.
///
/// The result, recorded in that document's own §9, is four consecutive audit rounds
/// (76, 79, 80, 81) each opening by finding the counts stale again. Round 80 named the
/// cause structural rather than careless — the only mechanism that catches the drift is
/// a round's R-ENUM step, which by construction does not run for work done outside a
/// round, and shipping a diagnostic is not a round-only activity. Round 81 (§2.18)
/// closes it here: the counts are now asserted on every test run, so drift is loud at
/// the moment it is introduced rather than at the next round, which may be never.
///
/// These tests read repo files from disk. That is established practice in this suite,
/// not a new dependency — see <c>CoreUnaffectedTests</c>, <c>CoreUnreferencedTests</c>
/// and <c>FixtureContainerBuildSiteTests</c>, all of which walk up to the
/// <c>JauntyQ.slnx</c> marker the same way. <see cref="RepoRoot_Resolves_AndTheDocsAreReadable"/>
/// is the guard that stops the whole class from passing vacuously by reading nothing.
/// </summary>
[Trait("Category", "AuditRegression")]
public class RegistryParityTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadRepoFile(params string[] relative)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(relative)));

    private static string AuditCriteria() => ReadRepoFile("docs", "audit-criteria.md");

    /// <summary>Every JNT id declared as a descriptor on JauntyDiagnostics.</summary>
    private static SortedSet<string> DeclaredCodes()
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in typeof(JauntyDiagnostics)
                     .GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.FieldType == typeof(DiagnosticDescriptor))
                ids.Add(((DiagnosticDescriptor)f.GetValue(null)!).Id);
        return ids;
    }

    /// <summary>
    /// Every directive name in DirectiveParser.KnownDirectives, read reflectively
    /// because the field is private. Reflection is legal here: the core-product ban
    /// on System.Linq and reflection applies to src/, never to test projects.
    /// </summary>
    private static SortedSet<string> DeclaredDirectives()
    {
        Type? t = typeof(JauntyDiagnostics).Assembly
            .GetType("JauntyQ.Generator.Directives.DirectiveParser");
        Assert.NotNull(t);

        FieldInfo? f = t!.GetField("KnownDirectives", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(f);

        var names = (string[])f!.GetValue(null)!;
        Assert.NotEmpty(names);
        return new SortedSet<string>(names, StringComparer.Ordinal);
    }

    private static SortedSet<string> Matches(string text, string pattern, int group = 1)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, pattern, RegexOptions.Multiline))
            found.Add(m.Groups[group].Value);
        return found;
    }

    /// <summary>
    /// Asserts a pattern occurs exactly once and returns its captured number. Exactly
    /// once matters: the audit criteria §9 is a dated log of what the counts WERE
    /// at each past round, and those historical entries must never be rewritten to
    /// match today. A pattern that started matching them too would either fail forever
    /// or, worse, invite someone to "fix" the history.
    /// </summary>
    private static int SoleStatedCount(string text, string pattern, string what)
    {
        MatchCollection ms = Regex.Matches(text, pattern, RegexOptions.Multiline);
        Assert.True(ms.Count == 1,
            $"Expected exactly one live '{what}' claim in the audit criteria, found {ms.Count}. "
            + "The anchor no longer identifies a unique sentence — fix the pattern here rather "
            + "than editing §9's dated historical entries to match.");
        return int.Parse(ms[0].Groups[1].Value);
    }

    // ── The vacuity guard ────────────────────────────────────────────────
    //
    // Without this, a broken RepoRoot() or a renamed doc turns every test below
    // into a comparison of two empty sets, which passes.

    [Fact]
    public void RepoRoot_Resolves_AndTheDocsAreReadable()
    {
        string root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "JauntyQ.slnx")));

        foreach (string[] rel in new[]
                 {
                     new[] { "docs", "audit-criteria.md" },
                     new[] { "docs", "06-reference", "diagnostics.md" },
                     new[] { "docs", "06-reference", "directives.md" },
                     new[] { "tests", "JauntyQ.Generator.Tests", "DiagnosticsRegistryTests.cs" },
                 })
        {
            string path = Path.Combine(root, Path.Combine(rel));
            Assert.True(File.Exists(path), $"missing: {path}");
            Assert.NotEmpty(File.ReadAllText(path));
        }

        // The reflective lookups are the other way this class could read nothing.
        Assert.NotEmpty(DeclaredCodes());
        Assert.NotEmpty(DeclaredDirectives());
    }

    // ── The four count claims inside the audit criteria ──────────────

    [Fact]
    public void AuditCriteria_Section1_StatesTheCurrentDiagnosticCount()
    {
        int stated = SoleStatedCount(
            AuditCriteria(),
            @"(\d+) `JNTxxxx` diagnostic codes \(`JauntyDiagnostics\.cs`",
            "§1 diagnostic count");

        Assert.Equal(DeclaredCodes().Count, stated);
    }

    [Fact]
    public void AuditCriteria_Section1_StatesTheCurrentDirectiveCount()
    {
        int stated = SoleStatedCount(
            AuditCriteria(),
            @"(\d+)\s+generator directives \(`DirectiveParser\.cs`\)",
            "§1 directive count");

        Assert.Equal(DeclaredDirectives().Count, stated);
    }

    [Fact]
    public void AuditCriteria_Section26_StatesTheCurrentDiagnosticCount()
    {
        int stated = SoleStatedCount(
            AuditCriteria(),
            @"^All (\d+) `JNTxxxx` codes\.",
            "§2.6 diagnostic count");

        Assert.Equal(DeclaredCodes().Count, stated);
    }

    [Fact]
    public void AuditCriteria_AppendixB_ProseStatesTheCurrentDiagnosticCount()
    {
        int stated = SoleStatedCount(
            AuditCriteria(),
            @"all (\d+) codes, not just the ones with a",
            "Appendix B prose count");

        Assert.Equal(DeclaredCodes().Count, stated);
    }

    [Fact]
    public void AuditCriteria_Section1_RangesCoverExactlyTheDeclaredCodes()
    {
        string text = AuditCriteria();
        Match bullet = Regex.Match(
            text,
            @"`JNTxxxx` diagnostic codes \(`JauntyDiagnostics\.cs`,(?<ranges>.*?)\);",
            RegexOptions.Singleline);
        Assert.True(bullet.Success, "§1's diagnostic-code bullet no longer parses.");

        // "JNT1001–1008, 2001–2020, ..." — en-dash, first range carries the prefix.
        var covered = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match r in Regex.Matches(bullet.Groups["ranges"].Value,
                     @"(?:JNT)?(\d{4})\s*[–-]\s*(\d{4})"))
        {
            int lo = int.Parse(r.Groups[1].Value), hi = int.Parse(r.Groups[2].Value);
            // "D4", not a bare concatenation: JNT0001 (2026-08-30) is the first code
            // below 1000, and int.Parse("0001") is 1, so "JNT" + i produced "JNT1" and
            // the range silently covered a code that does not exist while missing the
            // one that does. Every code above 1000 was unaffected, which is why this
            // held for 72 codes.
            for (int i = lo; i <= hi; i++)
                covered.Add("JNT" + i.ToString("D4"));
        }
        Assert.NotEmpty(covered);

        var declared = DeclaredCodes();
        Assert.True(covered.SetEquals(declared),
            "§1's stated code ranges do not match the declared descriptors. "
            + "Only in the ranges: " + string.Join(", ", covered.Except(declared)) + ". "
            + "Only in JauntyDiagnostics: " + string.Join(", ", declared.Except(covered)) + ".");
    }

    [Fact]
    public void AuditCriteria_AppendixB_HasARowForEveryDeclaredCode()
    {
        string text = AuditCriteria();
        int idx = text.IndexOf("## Appendix B", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Appendix B heading not found.");

        var rows = Matches(text.Substring(idx), @"^\| (JNT\d{4})");
        var declared = DeclaredCodes();

        Assert.True(rows.SetEquals(declared),
            "Appendix B's row set does not match the declared descriptors. "
            + "Missing rows: " + string.Join(", ", declared.Except(rows)) + ". "
            + "Rows with no descriptor: " + string.Join(", ", rows.Except(declared)) + ".");
    }

    // ── The reference docs ───────────────────────────────────────────────

    [Fact]
    public void DiagnosticsDoc_HasARowForEveryDeclaredCode()
    {
        var rows = Matches(ReadRepoFile("docs", "06-reference", "diagnostics.md"), @"^\| (JNT\d{4})");
        var declared = DeclaredCodes();

        Assert.True(rows.SetEquals(declared),
            "docs/06-reference/diagnostics.md does not document exactly the declared codes. "
            + "Missing: " + string.Join(", ", declared.Except(rows)) + ". "
            + "Documented but not declared: " + string.Join(", ", rows.Except(declared)) + ".");
    }

    [Fact]
    public void DirectivesDoc_HasAnEntryForEveryDeclaredDirective()
    {
        var documented = Matches(
            ReadRepoFile("docs", "06-reference", "directives.md"),
            // Spec 015: '-' is a directive-name character ("@allow-unindexed"),
            // so the heading pattern has to admit one or this test cannot see
            // the directive it is checking for.
            @"^### `-- @([a-z-]+)`");
        var declared = DeclaredDirectives();

        Assert.True(documented.SetEquals(declared),
            "docs/06-reference/directives.md does not document exactly KnownDirectives. "
            + "Missing: " + string.Join(", ", declared.Except(documented)) + ". "
            + "Documented but not known: " + string.Join(", ", documented.Except(declared)) + ".");
    }

    /// <summary>
    /// Spec 015 T8: <c>docs/06-reference/supported-sql.md</c> tells a consumer
    /// which code they will meet for each refused shape, so a code named there
    /// that does not resolve sends them to search the diagnostics table for
    /// something that was renamed or never existed. That is the exact failure
    /// the page was written to end, so it must not be able to reintroduce it.
    ///
    /// Subset, not set-equality: the page is a guide to the SQL surface, not a
    /// second copy of the diagnostics table, and most codes have nothing to do
    /// with grammar.
    /// </summary>
    [Fact]
    public void SupportedSqlDoc_NamesOnlyCodesThatResolve()
    {
        var named = Matches(
            ReadRepoFile("docs", "06-reference", "supported-sql.md"), @"(JNT\d{4})");
        var declared = DeclaredCodes();

        Assert.NotEmpty(named);
        Assert.True(named.IsSubsetOf(declared),
            "docs/06-reference/supported-sql.md names codes that no descriptor declares: "
            + string.Join(", ", named.Except(declared))
            + ". Every code the page sends a consumer to look up has to exist.");
    }

    /// <summary>
    /// The page's whole job is naming the diagnostic for each refused shape, so
    /// a refusal losing its code silently would leave the reader back where the
    /// consumer report started: told no, not told what to search for.
    /// </summary>
    [Fact]
    public void SupportedSqlDoc_NamesEveryGrammarRefusalCode()
    {
        var named = Matches(
            ReadRepoFile("docs", "06-reference", "supported-sql.md"), @"(JNT\d{4})");

        foreach (string required in new[] { "JNT1001", "JNT1006", "JNT1007", "JNT1008", "JNT1009" })
            Assert.True(named.Contains(required),
                $"{required} is a grammar-boundary refusal and is not named in supported-sql.md.");
    }

    // ── The registry test's own coverage ─────────────────────────────────

    [Fact]
    public void EveryDeclaredCode_HasASeverityAssertionInDiagnosticsRegistryTests()
    {
        // DiagnosticsRegistryTests asserts the descriptor COUNT in one place and each
        // code's severity/category in a separate [Theory]. Nothing related the two, so
        // a code could be counted while its severity went unasserted — and two were:
        // JNT3002 (SELECT * Forbidden) and JNT7001 (Identity Return Unavailable), both
        // Error, both load-bearing. Severity is what decides whether a build fails, so
        // an unasserted one can be downgraded to Warning with the suite still green.
        var asserted = Matches(
            ReadRepoFile("tests", "JauntyQ.Generator.Tests", "DiagnosticsRegistryTests.cs"),
            @"InlineData\(""(JNT\d{4})""");
        var declared = DeclaredCodes();

        Assert.True(declared.IsSubsetOf(asserted),
            "These codes have no [InlineData] severity/category assertion in "
            + "DiagnosticsRegistryTests: " + string.Join(", ", declared.Except(asserted))
            + ". Add a row asserting the severity and category each one ships with.");
    }

    private static SortedSet<string> RegistryNamedTestClasses()
    {
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        string registry = ReadRepoFile("docs", "99-reports", "audit-findings-registry.md");

        foreach (string line in registry.Split('\n'))
        {
            if (!line.StartsWith("| AUD-R", StringComparison.Ordinal))
                continue;

            string[] cells = line.Split('|');
            if (cells.Length < 9)
                continue;

            foreach (Match m in Regex.Matches(cells[7], @"\.Tests\.([A-Za-z0-9_]+)"))
                classes.Add(m.Groups[1].Value);
        }

        return classes;
    }

    [Fact]
    public void RegistryNamedTestClasses_AreDiscoverable_AndTheScanIsNotVacuous()
    {
        var classes = RegistryNamedTestClasses();

        Assert.True(classes.Count >= 40,
            "Only " + classes.Count + " test classes were parsed out of the registry's "
            + "regression-test column. The parse has broken, so the trait check below "
            + "would pass by checking almost nothing.");
    }

    [Fact]
    public void EveryRegistryNamedTestClass_CarriesTheAuditRegressionTrait()
    {
        string root = RepoRoot();
        var sources = new List<string>();
        foreach (string dir in new[] { "tests", "samples" })
        {
            string full = Path.Combine(root, dir);
            if (Directory.Exists(full))
                sources.AddRange(Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories));
        }

        var untagged = new List<string>();
        var notFound = new List<string>();

        foreach (string name in RegistryNamedTestClasses())
        {
            var decl = new Regex(
                @"(?:public|internal)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+"
                + Regex.Escape(name) + @"\b");

            bool located = false, tagged = false;
            foreach (string file in sources)
            {
                if (file.Contains("\\obj\\") || file.Contains("/obj/"))
                    continue;

                string text = File.ReadAllText(file);
                Match m = decl.Match(text);
                if (!m.Success)
                    continue;

                located = true;
                int from = Math.Max(0, m.Index - 400);
                if (text.Substring(from, m.Index - from).Contains("AuditRegression"))
                {
                    tagged = true;
                    break;
                }
            }

            if (!located) notFound.Add(name);
            else if (!tagged) untagged.Add(name);
        }

        Assert.True(notFound.Count == 0,
            "The registry names regression tests in classes that no longer exist: "
            + string.Join(", ", notFound)
            + ". A registry row pointing at a deleted test guards nothing.");

        Assert.True(untagged.Count == 0,
            "These test classes are named in the findings registry but do not carry "
            + "[Trait(\"Category\", \"AuditRegression\")]: " + string.Join(", ", untagged)
            + ". The guarded regression suite is selected by that trait, so an untagged "
            + "class is silently excluded from the run that is supposed to catch a "
            + "reverted fix.");
    }
}
