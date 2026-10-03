using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class WorkflowSkipAuditContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JauntyQ.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Joined(string file) =>
        Regex.Replace(
            Regex.Replace(File.ReadAllText(file).Replace("\r\n", "\n"), @"(?m)^\s*#[^\n]*\n", ""),
            @"[ \t]*\\\n\s*", " ");

    public static IEnumerable<object[]> WorkflowsThatTest() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Where(f => Joined(f).Contains("dotnet test "))
            .Select(f => new object[] { Path.GetFileName(f) });

    private static string Workflow(string name) =>
        Joined(Path.Combine(RepoRoot(), ".github", "workflows", name));

    [Fact]
    public void TheWorkflowsThatGateOnTests_AreAllFound()
    {
        var names = WorkflowsThatTest().Select(r => (string)r[0]).ToArray();

        Assert.Contains("ci.yml", names);
        Assert.Contains("nightly.yml", names);
        Assert.Contains("release.yml", names);
    }

    [Theory]
    [MemberData(nameof(WorkflowsThatTest))]
    public void EveryDotnetTest_LogsTrxToTheAuditedDirectory(string name)
    {
        var runs = Regex.Matches(Workflow(name), @"dotnet test [^\n]*").Select(m => m.Value).ToList();

        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            Assert.Contains("--logger trx", run);
            Assert.Contains("--results-directory artifacts/test-results", run);
        });
    }

    [Theory]
    [MemberData(nameof(WorkflowsThatTest))]
    public void TheSkipAudit_RunsOverThoseResultsAfterTheTests(string name)
    {
        string text = Workflow(name);
        int test = text.IndexOf("dotnet test ", StringComparison.Ordinal);
        var audit = Regex.Match(text, @"deno run -A scripts/skip-audit\.js --results artifacts/test-results --docker-reachable true");

        Assert.True(audit.Success, name + " does not run scripts/skip-audit.js over artifacts/test-results");
        Assert.True(audit.Index > test, name + " runs the skip audit before the tests");
        Assert.Contains("denoland/setup-deno@", text);
    }
}
