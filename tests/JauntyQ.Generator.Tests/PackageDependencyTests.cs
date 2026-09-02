using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class PackageDependencyTests : IClassFixture<PackageDependencyTests.PackedFixture>
{
    private readonly PackedFixture _packed;

    public PackageDependencyTests(PackedFixture packed) => _packed = packed;

    public static IEnumerable<object[]> ExpectedDependencies() =>
    [
        ["JauntyQ.Runtime", Array.Empty<string>()],
        ["JauntyQ.SqlParser", Array.Empty<string>()],
        ["JauntyQ.Schema", new[] { "System.Text.Json" }],
        ["JauntyQ.Analysis", new[] { "JauntyQ.Schema", "JauntyQ.SqlParser", "System.Text.Json" }],
        ["JauntyQ.Schema.Extraction", new[]
        {
            "JauntyQ.Schema", "Microsoft.Data.SqlClient", "Microsoft.Data.Sqlite",
            "MySqlConnector", "Npgsql", "SQLitePCLRaw.bundle_e_sqlite3",
        }],
        ["JauntyQ.Cli.Core", new[] { "JauntyQ.Analysis", "JauntyQ.Schema", "JauntyQ.Schema.Extraction", "JauntyQ.SqlParser" }],
    ];

    [Theory]
    [MemberData(nameof(ExpectedDependencies))]
    public void PackedNuspec_DeclaresExactlyThePlannedDependencies(string packageId, string[] expected)
    {
        var groups = _packed.DependencyGroups(packageId);

        Assert.NotEmpty(groups);
        foreach (var (framework, ids) in groups)
            Assert.True(expected.OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)),
                $"{packageId} [{framework}] declares [{string.Join(", ", ids)}], expected [{string.Join(", ", expected)}]");
    }

    [Fact]
    public void GeneratorPackage_DeclaresNoDependencies()
    {
        Assert.Empty(_packed.DependencyGroups("JauntyQ.Generator").SelectMany(g => g.ids));
    }

    [Fact]
    public void FreeTool_BundlesOnlyCoreAssemblies()
    {
        Assert.Equal(
            ["JauntyQ.Analysis.dll", "JauntyQ.Cli.Core.dll", "JauntyQ.Cli.dll", "JauntyQ.Schema.Extraction.dll", "JauntyQ.Schema.dll", "JauntyQ.SqlParser.dll"],
            _packed.BundledJauntyAssemblies("JauntyQ.Cli"));
    }

    [Fact]
    public void FreeTool_DeclaresTheDotnetToolPackageType_AndNoDependencies()
    {
        Assert.Contains("DotnetTool", _packed.PackageTypes("JauntyQ.Cli"));
        Assert.Empty(_packed.DependencyGroups("JauntyQ.Cli"));
    }

    [Fact]
    public void ExtractionPackage_CarriesOnlyItsOwnAssembly()
    {
        Assert.Equal(["JauntyQ.Schema.Extraction.dll"], _packed.LibEntries("JauntyQ.Schema.Extraction"));
    }

    [Theory]
    [MemberData(nameof(AllPackages))]
    public void EveryPackage_CarriesTheLicenceTheExceptionAndTheNotice(string packageId)
    {
        var files = _packed.RootFiles(packageId);

        Assert.Contains("LICENSE.md", files);
        Assert.Contains("EXCEPTION.md", files);
        Assert.Contains("NOTICE.md", files);
    }

    [Theory]
    [MemberData(nameof(AllPackages))]
    public void EveryPackage_DeclaresLicenseMdAsItsLicenceFile(string packageId)
    {
        Assert.Equal("LICENSE.md", _packed.LicenseElement(packageId));
    }

    public static IEnumerable<object[]> AllPackages() => PackedFixture.Projects.Select(p => new object[] { p });

    [Fact]
    public void EveryCorePackable_OptsInThroughItsOwnCsproj()
    {
        foreach (var project in new[] { "JauntyQ.Schema", "JauntyQ.SqlParser", "JauntyQ.Analysis", "JauntyQ.Schema.Extraction", "JauntyQ.Cli.Core" })
        {
            string csproj = File.ReadAllText(Path.Combine(PackedFixture.RepoRoot(), "src", project, project + ".csproj"));
            Assert.Contains("<IsPackable>true</IsPackable>", csproj);
            Assert.Contains($"<PackageId>{project}</PackageId>", csproj);
        }
    }

    public sealed class PackedFixture : IDisposable
    {
        public static readonly string[] Projects =
        [
            "JauntyQ.Runtime", "JauntyQ.Generator", "JauntyQ.SqlParser", "JauntyQ.Schema",
            "JauntyQ.Analysis", "JauntyQ.Schema.Extraction", "JauntyQ.Cli.Core", "JauntyQ.Cli",
        ];

        public string OutputDir { get; }

        public PackedFixture()
        {
            OutputDir = Path.Combine(RepoRoot(), "tmp", "package-dependency-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(OutputDir);
            foreach (var project in Projects)
                Pack(project);
        }

        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private void Pack(string project)
        {
            var psi = new ProcessStartInfo("dotnet",
                $"pack \"{Path.Combine(RepoRoot(), "src", project, project + ".csproj")}\" -c Debug -o \"{OutputDir}\" --nologo -v q -p:IncludeSymbols=false")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, $"dotnet pack {project} failed ({p.ExitCode}):\n{stdout}\n{stderr}");
        }

        private string NupkgPath(string packageId) =>
            Directory.GetFiles(OutputDir, packageId + ".*.nupkg")
                .Single(f => !f.EndsWith(".snupkg", StringComparison.Ordinal)
                          && Path.GetFileName(f).StartsWith(packageId + ".", StringComparison.Ordinal)
                          && char.IsDigit(Path.GetFileName(f)[packageId.Length + 1]));

        private XDocument Nuspec(string packageId)
        {
            using var zip = ZipFile.OpenRead(NupkgPath(packageId));
            var nuspecEntry = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var stream = nuspecEntry.Open();
            return XDocument.Load(stream);
        }

        public IReadOnlyList<(string framework, string[] ids)> DependencyGroups(string packageId)
        {
            var doc = Nuspec(packageId);
            XNamespace ns = doc.Root!.Name.Namespace;
            return doc.Descendants(ns + "group")
                .Select(g => (
                    g.Attribute("targetFramework")?.Value ?? "",
                    g.Elements(ns + "dependency").Select(d => d.Attribute("id")!.Value).ToArray()))
                .ToList();
        }

        public string[] PackageTypes(string packageId)
        {
            var doc = Nuspec(packageId);
            XNamespace ns = doc.Root!.Name.Namespace;
            return doc.Descendants(ns + "packageType").Select(t => t.Attribute("name")!.Value).ToArray();
        }

        public string? LicenseElement(string packageId)
        {
            var doc = Nuspec(packageId);
            XNamespace ns = doc.Root!.Name.Namespace;
            return doc.Descendants(ns + "license").SingleOrDefault()?.Value;
        }

        public string[] RootFiles(string packageId)
        {
            using var zip = ZipFile.OpenRead(NupkgPath(packageId));
            return zip.Entries
                .Where(e => !e.FullName.Contains('/'))
                .Select(e => e.FullName)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }

        public string[] BundledJauntyAssemblies(string packageId)
        {
            using var zip = ZipFile.OpenRead(NupkgPath(packageId));
            return zip.Entries
                .Where(e => e.FullName.StartsWith("tools/", StringComparison.Ordinal)
                         && e.FullName.EndsWith(".dll", StringComparison.Ordinal)
                         && Path.GetFileName(e.FullName).StartsWith("JauntyQ.", StringComparison.Ordinal))
                .Select(e => Path.GetFileName(e.FullName))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }

        public string[] LibEntries(string packageId)
        {
            using var zip = ZipFile.OpenRead(NupkgPath(packageId));
            return zip.Entries
                .Where(e => e.FullName.StartsWith("lib/", StringComparison.Ordinal) && e.FullName.EndsWith(".dll", StringComparison.Ordinal))
                .Select(e => Path.GetFileName(e.FullName))
                .OrderBy(x => x)
                .ToArray();
        }

        public void Dispose()
        {
            try { Directory.Delete(OutputDir, recursive: true); } catch (IOException) { }
        }
    }
}
