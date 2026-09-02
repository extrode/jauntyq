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
/// each other</b>: the descriptor, the registry test, the public-API baseline and the
/// reference docs. Nothing but a human checked the last two, and the counts drifted
/// more than once. These tests assert the mirrors on every test run, so drift is loud
/// at the moment it is introduced.
///
/// These tests read repo files from disk. That is established practice in this suite,
/// not a new dependency: <c>CoreUnaffectedTests</c>, <c>CoreUnreferencedTests</c>
/// and <c>FixtureContainerBuildSiteTests</c> all walk up to the <c>JauntyQ.slnx</c>
/// marker the same way. <see cref="RepoRoot_Resolves_AndTheDocsAreReadable"/>
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
}
