using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class EmitterMutationShardTests
{
    private static readonly string[] Shards = { "emitter-1", "emitter-2", "emitter-3", "emitter-4" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static List<string> MutateEntries(string shard)
    {
        string path = Path.Combine(RepoRoot(), "tests", "Extrode.JauntyQ.Generator.Tests", "stryker-config." + shard + ".json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("stryker-config").GetProperty("mutate")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
    }

    private static string[] EmitterFiles() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), "src", "Extrode.JauntyQ.Generator"), "*.cs")
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("CodeEmitter", StringComparison.Ordinal) || n.StartsWith("JauntyQGenerator", StringComparison.Ordinal))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void EveryEntry_IsAPlainFileName_NotAPattern()
    {
        foreach (string shard in Shards)
            Assert.All(MutateEntries(shard), entry =>
            {
                Assert.StartsWith("**/", entry);
                Assert.DoesNotMatch(@"[\*\?\[\]\{\}]", entry.Substring(3));
            });
    }

    [Fact]
    public void EveryEmitterFile_IsInExactlyOneShard()
    {
        string[] listed = Shards.SelectMany(MutateEntries).Select(e => e.Substring(3)).ToArray();

        Assert.Empty(listed.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Equal(EmitterFiles(), listed.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheWorkflowMatrix_RunsEveryShard()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "mutation-emitter.yml"));

        Assert.Contains("shard: [" + string.Join(", ", Shards) + "]", workflow, StringComparison.Ordinal);
        Assert.Contains("--config-file stryker-config.${{ matrix.shard }}.json", workflow, StringComparison.Ordinal);
    }
}
