using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Testcontainers' <c>Build()</c> runs <c>AbstractBuilder.Validate()</c>, which throws
/// <c>ArgumentException("Docker is either not running or misconfigured", "DockerEndpointAuthConfig")</c>
/// when there is no reachable Docker endpoint. A fixture that calls it from a field
/// initializer therefore throws in its <b>constructor</b> — before xUnit ever reaches
/// <c>InitializeAsync</c>, so the <c>try</c>/<c>catch</c> + <see cref="FixtureGate.SkipReasonOrThrow"/>
/// gate never runs and <c>Skip.IfNot(Available, SkipReason)</c> never gets the chance to
/// skip. Every test in the collection hard-fails instead.
///
/// Measured on a full-solution run with Docker stopped: 463 failures across 16 assemblies,
/// all of them this one defect. The suites that build inside <c>InitializeAsync</c>
/// (Northwind, JauntyQ.Schema.Extraction.Tests, Postgres.Tests, MySql.Tests) skipped cleanly in the same run,
/// which is the contrast that isolates the cause — the gate is fine, the call site was wrong.
///
/// This scans source rather than behaviour because the failure only reproduces with Docker
/// absent, which is exactly the condition CI does not have (see <see cref="FixtureGate.StrictCi"/>).
/// </summary>
public class FixtureContainerBuildSiteTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> TestSources()
    {
        string root = RepoRoot();
        foreach (string area in new[] { "tests", "samples" })
        {
            string dir = Path.Combine(root, area);
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/"))
                    continue;

                // This file quotes the defect verbatim to prove the scan still matches it.
                if (rel.EndsWith("/" + nameof(FixtureContainerBuildSiteTests) + ".cs", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    // A field declaration whose initializer builds a Testcontainers container. The
    // initializer may wrap onto the following line, which is how every instance in this
    // repo was written, so the pattern spans up to one newline.
    private static readonly Regex FieldInitializerBuild = new(
        @"^[ \t]*(?:private|protected|internal|public)[^\r\n=;]*\b\w*Container\b[^\r\n=;]*=\s*(?:\r?\n)?[^\r\n;]*\.Build\(\)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public void NoTestFixture_BuildsAContainer_InAFieldInitializer()
    {
        List<string> offenders = new();
        string root = RepoRoot();

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);
            if (FieldInitializerBuild.IsMatch(text))
                offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }

        Assert.True(
            offenders.Count == 0,
            "Testcontainers Build() must be called inside InitializeAsync's try/catch, not in a "
                + "field initializer -- a field initializer runs in the constructor, where "
                + "FixtureGate.SkipReasonOrThrow cannot catch it, so the whole collection "
                + "hard-fails instead of skipping when Docker is absent. Offenders:\n  "
                + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheScan_ActuallyMatchesTheDefect_AndNotItsFix()
    {
        // Guards the guard: if the regex silently stopped matching, the test above would
        // pass vacuously on a repo full of the defect. These are the exact before/after
        // shapes of this fix.
        const string defect = """
                public sealed class F : IAsyncLifetime
                {
                    private readonly PostgreSqlContainer _container =
                        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
                }
                """;

        const string defectOneLine = """
                public sealed class F : IAsyncLifetime
                {
                    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
                }
                """;

        const string fixedShape = """
                public sealed class F : IAsyncLifetime
                {
                    private PostgreSqlContainer? _container;

                    public async Task InitializeAsync()
                    {
                        try
                        {
                            PostgreSqlContainer container = new PostgreSqlBuilder().Build();
                            _container = container;
                            await container.StartAsync();
                        }
                        catch (Exception ex) { }
                    }
                }
                """;

        Assert.Matches(FieldInitializerBuild, defect);
        Assert.Matches(FieldInitializerBuild, defectOneLine);
        Assert.DoesNotMatch(FieldInitializerBuild, fixedShape);
    }

    // A class that takes a Docker-backed fixture must gate every test method, or the
    // fixture's Available/SkipReason pair is decorative: the method runs regardless and
    // faults on the unavailable resource. Audit round 12 found this across the Sakila
    // family and deferred the retrofit as out of scope for a pure-addition round.
    private static readonly Regex ClassFixtureUse = new(
        @"IClassFixture<(\w+)>", RegexOptions.Compiled);

    // A Docker-backed fixture reaches a test class two ways: IClassFixture<T> on the
    // declaration, or [Collection("N")] where N's definition declares
    // ICollectionFixture<T>. The nine container-backed assemblies retrofitted to shared
    // collections on 2026-07-30 all use the second form -- matching only the first would
    // have dropped 53 test classes out of this guard's scope without failing anything,
    // which is precisely the vacuous-guard failure this class exists to prevent.
    private static readonly Regex CollectionDefinitionDecl = new(
        @"\[CollectionDefinition\(""([^""]+)""\)\][^\[]{0,400}?ICollectionFixture<(\w+)>",
        RegexOptions.Compiled);

    private static readonly Regex CollectionUse = new(
        @"\[Collection\(""([^""]+)""\)\]", RegexOptions.Compiled);

    private static readonly Regex UngatedTestAttribute = new(
        @"^[ \t]*\[(Fact|Theory)\]", RegexOptions.Multiline | RegexOptions.Compiled);

    // Project directory of a source file: the nearest ancestor holding a .csproj. Fixture
    // names repeat across projects (every Conduit dialect has its own ConduitWebAppFixture,
    // only some Docker-backed), so resolution has to be project-scoped or the SQLite
    // siblings get tarred with their Postgres namesakes' brush.
    private static string ProjectDir(string file)
    {
        DirectoryInfo? dir = new FileInfo(file).Directory;
        while (dir != null && !Directory.EnumerateFiles(dir.FullName, "*.csproj").Any())
            dir = dir.Parent;
        return dir?.FullName ?? Path.GetDirectoryName(file)!;
    }

    // Splits a source file at top-level class declarations. A file can mix a Docker-backed
    // class with a plain one -- ProcedureExtractorTests.cs holds three gated Testcontainers
    // classes and one SQLite class whose [Fact]s are entirely correct -- so the unit of
    // judgement is the class, not the file.
    private static IEnumerable<(string Name, string Body)> Classes(string text)
    {
        // \r?$ because in .NET multiline mode $ anchors before \n only, never before \r\n.
        MatchCollection decls = Regex.Matches(text, @"^[ \t]*(?:public|internal)[^\r\n]*\bclass (\w+)[^\r\n]*\r?$", RegexOptions.Multiline);

        // A class's body starts at its attribute block, not at the `public class` line:
        // [Collection("N")] sits ABOVE the declaration, so slicing at the declaration
        // would file it under the PRECEDING class and leave every collection-based test
        // class looking unattributed.
        int[] starts = new int[decls.Count];
        for (int i = 0; i < decls.Count; i++)
            starts[i] = BackUpOverAttributes(text, decls[i].Index);

        for (int i = 0; i < decls.Count; i++)
        {
            int end = i + 1 < decls.Count ? starts[i + 1] : text.Length;
            yield return (decls[i].Groups[1].Value, text[starts[i]..end]);
        }
    }

    // Walks backwards from a class declaration over lines that are wholly an attribute,
    // returning the index the class's own text begins at.
    private static int BackUpOverAttributes(string text, int declIndex)
    {
        int start = declIndex;
        while (start > 0)
        {
            int prevEnd = text.LastIndexOf('\n', start - 1);
            int prevStart = prevEnd < 0 ? 0 : text.LastIndexOf('\n', prevEnd - 1) + 1;
            if (prevEnd < 0)
                break;
            string line = text[prevStart..prevEnd].Trim().TrimEnd('\r');
            if (line.StartsWith('[') && line.EndsWith(']'))
                start = prevStart;
            else
                break;
        }
        return start;
    }

    [Fact]
    public void EveryDockerBackedTestClass_GatesWithSkippableFact()
    {
        string root = RepoRoot();
        List<string> sources = TestSources().ToList();

        // A fixture is Docker-backed when it actually holds a Testcontainers container --
        // not merely when it exposes SkipReason. ConduitSqliteFixture declares
        // `SkipReason => null` for shape-compatibility with its Docker siblings while never
        // being unavailable, and its plain [Fact]s are correct. Since 2026-07-31 the
        // fixtures in JauntyQ.Schema.Extraction.Tests hold no container of their own -- they draw a database
        // from the shared EngineContainers -- so a reference to EngineContainers marks a
        // class Docker-backed too; without that arm the consolidation would silently drop
        // every converted fixture out of this guard's scope.
        HashSet<string> dockerFixtures = new(StringComparer.Ordinal);
        foreach (string file in sources)
        {
            foreach ((string name, string body) in Classes(File.ReadAllText(file)))
            {
                if (Regex.IsMatch(body, @"\b\w+Container\b|\bEngineContainers\b"))
                    dockerFixtures.Add(ProjectDir(file) + "|" + name);
            }
        }

        Assert.NotEmpty(dockerFixtures);

        // collection name -> the fixture its definition supplies, project-scoped for the
        // same reason dockerFixtures is: names repeat across dialect siblings.
        Dictionary<string, string> collectionFixtures = new(StringComparer.Ordinal);
        foreach (string file in sources)
        {
            foreach (Match m in CollectionDefinitionDecl.Matches(File.ReadAllText(file)))
                collectionFixtures[ProjectDir(file) + "|" + m.Groups[1].Value] = m.Groups[2].Value;
        }

        // The retrofit moved the majority of Docker-backed classes onto collections, so
        // if this map is empty the collection arm below is dead and the guard has
        // silently narrowed back to where it was.
        Assert.NotEmpty(collectionFixtures);

        List<string> offenders = new();
        foreach (string file in sources)
        {
            string project = ProjectDir(file);
            foreach ((string name, string body) in Classes(File.ReadAllText(file)))
            {
                bool usesDockerFixture = ClassFixtureUse.Matches(body)
                    .Any(m => dockerFixtures.Contains(project + "|" + m.Groups[1].Value));

                if (!usesDockerFixture)
                {
                    usesDockerFixture = CollectionUse.Matches(body).Any(m =>
                        collectionFixtures.TryGetValue(project + "|" + m.Groups[1].Value, out string? fx)
                        && dockerFixtures.Contains(project + "|" + fx));
                }

                if (usesDockerFixture && UngatedTestAttribute.IsMatch(body))
                    offenders.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')} ({name})");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A test class taking a Docker-backed fixture must use [SkippableFact]/[SkippableTheory] "
                + "with Skip.IfNot(Available, SkipReason); a plain [Fact] runs even when the container "
                + "never came up. Offenders:\n  "
                + string.Join("\n  ", offenders));
    }

    // A fixture that applies a checked-in .sql seed script must set CommandTimeout on the
    // command it runs each batch with. CommandTimeout is per batch, not per script, and
    // every client here defaults to 30s: SqlClient, Npgsql and MySqlConnector alike. That
    // is ample for a batch of DDL and fatal for a batch of bulk INSERTs -- Northwind's
    // Orders batch is 830 statements in 303,884 bytes, measured at ~4.4s on an idle host
    // and ~17.7s under a full-solution run, and it failed the whole 69-test assembly with
    // "Execution Timeout Expired" on 2026-07-30. Seventeen fixtures already set 300; the
    // ones below do not, and are a ratchet rather than a clean bill of health.
    private static readonly Regex ReadsSeedScript = new(
        @"ReadAllText(?:Async)?\([^)]*\.sql|""[\w.]+\.sql""", RegexOptions.Compiled);

    // An ASSIGNMENT, not a mention. Written as a substring check first, this matched the
    // word inside the explanatory comment directly above the fix, so deleting the fix left
    // the guard green -- caught by perturbing it, which is the whole reason perturbing is
    // mandatory.
    private static readonly Regex SetsCommandTimeout = new(
        @"\bCommandTimeout\s*=\s*\d+", RegexOptions.Compiled);

    // Eight entries when this guard was written on 2026-07-30, drained to zero the same
    // day. The set stays declared so the ratchet's shape survives: an entry may only be
    // ADDED here with a dated justification, and the companion test below fails if an
    // entry goes stale. Paths are repo-relative with forward slashes.
    private static readonly HashSet<string> SeedTimeoutDebt = new(StringComparer.Ordinal);

    [Fact]
    public void EverySeedApplyingFixture_SetsCommandTimeout()
    {
        string root = RepoRoot();
        List<string> offenders = new();
        List<string> scanned = new();

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);
            if (!Regex.IsMatch(text, @"\b\w*Container\b") || !text.Contains("ExecuteNonQuery", StringComparison.Ordinal))
                continue;
            if (!ReadsSeedScript.IsMatch(text))
                continue;

            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            scanned.Add(rel);

            if (!SetsCommandTimeout.IsMatch(text) && !SeedTimeoutDebt.Contains(rel))
                offenders.Add(rel);
        }

        // A scan that matched nothing would pass vacuously, and this one has three
        // conditions to get wrong at once.
        Assert.True(scanned.Count >= 10, $"expected the scan to find the seed-applying fixtures, saw {scanned.Count}");

        Assert.True(
            offenders.Count == 0,
            "A fixture applying a .sql seed script must set cmd.CommandTimeout -- the 30s "
                + "client default is per batch, and a bulk-INSERT batch exceeds it under a "
                + "loaded host, failing the entire assembly at fixture init. Use 300, as the "
                + "other seed-applying fixtures do. Offenders:\n  "
                + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSeedTimeoutDebtList_IsAccurate_AndOnlyShrinks()
    {
        // Guards the guard the other way: a stale entry here would silently excuse a file
        // that has since been fixed, or one that no longer exists, and the ratchet would
        // stop ratcheting.
        string root = RepoRoot();
        foreach (string rel in SeedTimeoutDebt)
        {
            string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), $"debt list names a file that no longer exists: {rel}");
            Assert.False(
                SetsCommandTimeout.IsMatch(File.ReadAllText(full)),
                $"{rel} now sets CommandTimeout -- remove it from SeedTimeoutDebt so the ratchet tightens");
        }
    }

    // FixtureGate.SkipReasonOrThrow's own doc comment states the policy: call it from a
    // fixture's STARTUP catch -- "container start / connection open only -- never wrapping
    // seed-script or product-code execution". 22 of the 23 Docker-backed fixtures broke it,
    // running the seed inside the same try, so a genuine seed or product failure was caught,
    // relabelled "Docker unavailable" and reported as a SKIP. That is how a full-solution run
    // reports success while a variable slice of it never executed: two runs on the same commit
    // on 2026-07-30 skipped 66 and 11 of the same 2960 tests, both "successful".
    //
    // Matches an ExecuteNonQuery lying between StartAsync() and the catch that calls
    // SkipReasonOrThrow. Lazy quantifiers throughout so it binds to the NEAREST catch and
    // cannot span from one fixture's try into a later fixture's catch in the same file --
    // Explain.Tests puts several in one file.
    private static readonly Regex SeedInsideSkipCatch = new(
        @"StartAsync\(\)(?:(?!StartAsync\(\)).)*?ExecuteNonQuery(?:(?!StartAsync\(\)).)*?catch\s*\(Exception[^)]*\)\s*\{(?:(?!\bcatch\b).)*?SkipReasonOrThrow",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [Fact]
    public void NoFixture_RunsItsSeedScript_InsideTheDockerSkipCatch()
    {
        string root = RepoRoot();
        List<string> offenders = new();
        int scanned = 0;

        foreach (string file in TestSources())
        {
            string text = File.ReadAllText(file);
            if (!text.Contains("SkipReasonOrThrow", StringComparison.Ordinal))
                continue;

            scanned++;
            if (SeedInsideSkipCatch.IsMatch(text))
                offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }

        // The whole point is that this scan reaches every Docker-backed fixture; if it
        // enumerated nothing it would pass vacuously.
        Assert.True(scanned >= 20, $"expected to scan the Docker-backed fixtures, saw {scanned}");

        Assert.True(
            offenders.Count == 0,
            "A fixture's soft-skip catch must cover container bring-up ONLY. Applying the seed "
                + "script inside it means a real seed or product failure is caught and reported "
                + "as a \"Docker unavailable\" skip, so the suite goes green without running. "
                + "Close the try after StartAsync(), return from the catch, and run the seed "
                + "after it -- see NorthwindFixture. Offenders:\n  "
                + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSeedInsideSkipCatch_ScanMatchesTheDefect_AndNotItsFix()
    {
        // Guards the guard. These are the exact before/after shapes of the 2026-07-30 fix.
        const string defect = """
                try
                {
                    var container = new MsSqlBuilder().Build();
                    await container.StartAsync();
                    cmd.CommandText = ddl;
                    await cmd.ExecuteNonQueryAsync();
                    Available = true;
                }
                catch (Exception ex)
                {
                    Available = false;
                    SkipReason = FixtureGate.SkipReasonOrThrow(ex);
                }
                """;

        const string fixedShape = """
                try
                {
                    var container = new MsSqlBuilder().Build();
                    await container.StartAsync();
                }
                catch (Exception ex)
                {
                    Available = false;
                    SkipReason = FixtureGate.SkipReasonOrThrow(ex);
                    return;
                }

                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync();
                Available = true;
                """;

        Assert.Matches(SeedInsideSkipCatch, defect);
        Assert.DoesNotMatch(SeedInsideSkipCatch, fixedShape);
    }

    // Since 2026-07-31 (the plan) the
    // fixtures in THIS assembly share one container per engine via EngineContainers,
    // one database per fixture. The consolidation only holds if a new fixture cannot
    // quietly regress to building its own container: that single call site is
    // EngineContainers.cs, and everything else in tests/JauntyQ.Schema.Extraction.Tests goes through it.
    // Samples keep building their own containers -- their suites run in separate
    // processes where nothing can be shared -- so the scan is scoped to this project.
    private static readonly Regex TestcontainersBuilderUse = new(
        @"new \w+Builder\(\)", RegexOptions.Compiled);

    [Fact]
    public void OnlyEngineContainers_BuildsAContainer_InThisAssembly()
    {
        string root = RepoRoot();
        string projectDir = Path.Combine(root, "tests", "JauntyQ.Schema.Extraction.Tests");
        List<string> offenders = new();
        int scanned = 0;

        foreach (string file in TestSources())
        {
            if (!file.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                continue;
            string name = Path.GetFileName(file);
            if (name == "EngineContainers.cs")
                continue;

            scanned++;
            if (TestcontainersBuilderUse.IsMatch(File.ReadAllText(file)))
                offenders.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
        }

        Assert.True(scanned >= 15, $"expected the scan to reach this project's sources, saw {scanned}");

        Assert.True(
            offenders.Count == 0,
            "Fixtures in JauntyQ.Schema.Extraction.Tests must draw a per-fixture database from the shared "
                + "EngineContainers (await EngineContainers.<Engine>, then CreateDatabaseAsync) "
                + "instead of building their own container -- one container per fixture is what "
                + "put a full-solution run at 17 concurrent containers. Offenders:\n  "
                + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheBuilderUseScan_MatchesTheDefect_AndNotItsFix()
    {
        // Guards the guard, same as the scans above.
        const string defect = "_container = new MsSqlBuilder().Build();";
        const string fixedShape = """
                var engine = await EngineContainers.MsSql;
                ConnectionString = await EngineContainers.CreateDatabaseAsync(engine, "fx_example");
                """;

        Assert.Matches(TestcontainersBuilderUse, defect);
        Assert.DoesNotMatch(TestcontainersBuilderUse, fixedShape);
    }

    [Fact]
    public void RepoRoot_Resolves_AndTheScanSeesRealFiles()
    {
        // A scan that silently enumerates nothing would also pass vacuously.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "JauntyQ.slnx")));
        Assert.True(TestSources().Count() > 100, "expected the scan to reach the repo's test sources");
    }
}
