using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Approval guard on the public API surface of the shipped assemblies. Any
/// added/removed/renamed public (or protected) type or member changes the
/// computed signature and fails this test, forcing the change to be deliberate
/// — and, per the project's testing bar, to arrive with a test. Update the
/// baseline by deleting the .approved.txt file and re-running (it bootstraps),
/// then review the diff before committing.
/// </summary>
public class PublicApiSurfaceTests
{
    [Fact]
    public void JauntyQ_Generator_PublicApi_MatchesApproved()
        => AssertApproved(typeof(JauntyQGenerator).Assembly, "Extrode.JauntyQ.Generator.PublicApi.approved.txt");

    private static void AssertApproved(Assembly assembly, string approvedFileName)
    {
        string actual = DumpPublicApi(assembly);
        string approvedPath = Path.Combine(ApprovedDir(), approvedFileName);

        if (!File.Exists(approvedPath))
        {
            File.WriteAllText(approvedPath, actual);
            Assert.Fail($"Baseline did not exist; wrote it to '{approvedPath}'. Review and commit it, then re-run.");
        }

        string approved = Normalize(File.ReadAllText(approvedPath));
        if (Normalize(actual) != approved)
        {
            string receivedPath = approvedPath.Replace(".approved.txt", ".received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail(
                $"Public API of {assembly.GetName().Name} changed. Diff '{approvedFileName}' against the .received.txt " +
                "beside it. If the change is intended, add a test for the new surface and update the baseline.");
        }
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();

    /// <summary>Locates the source-tree folder that holds the approved baseline.</summary>
    private static string ApprovedDir()
    {
        // A git worktree's ".git" is a file (a gitlink to the main repo's git
        // dir), not a directory, so this must accept either form to find the
        // repo root when tests run inside a worktree.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".git")) &&
               !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        Assert.True(dir != null, "Could not locate repository root (no .git found walking up).");
        string target = Path.Combine(dir!.FullName, "tests", "Extrode.JauntyQ.Generator.Tests", "PublicApi");
        Directory.CreateDirectory(target);
        return target;
    }

    private static string DumpPublicApi(Assembly assembly)
    {
        var sb = new StringBuilder();
        foreach (var t in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            sb.Append(Kind(t)).Append(' ').Append(t.FullName).Append('\n');
            var members = t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(IsApiVisible)
                .Select(Signature)
                .Where(s => s != null)
                .OrderBy(s => s, StringComparer.Ordinal);
            foreach (var m in members)
                sb.Append("    ").Append(m).Append('\n');
        }
        return sb.ToString();
    }

    private static string Kind(Type t) =>
        t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" :
        typeof(Delegate).IsAssignableFrom(t) ? "delegate" : "class";

    private static bool IsApiVisible(MemberInfo m) => m switch
    {
        MethodInfo mi => (mi.IsPublic || mi.IsFamily || mi.IsFamilyOrAssembly) && !mi.IsSpecialName,
        ConstructorInfo ci => ci.IsPublic || ci.IsFamily || ci.IsFamilyOrAssembly,
        PropertyInfo pi => (pi.GetMethod?.IsPublic ?? false) || (pi.GetMethod?.IsFamily ?? false)
                           || (pi.SetMethod?.IsPublic ?? false) || (pi.SetMethod?.IsFamily ?? false),
        FieldInfo fi => (fi.IsPublic || fi.IsFamily || fi.IsFamilyOrAssembly) && !fi.IsSpecialName,
        EventInfo ei => (ei.AddMethod?.IsPublic ?? false) || (ei.AddMethod?.IsFamily ?? false),
        _ => false,
    };

    private static string? Signature(MemberInfo m) => m switch
    {
        MethodInfo mi => $"method {mi.Name}({Params(mi.GetParameters())}):{Short(mi.ReturnType)}",
        ConstructorInfo ci => $"ctor .ctor({Params(ci.GetParameters())})",
        PropertyInfo pi => $"prop {pi.Name}:{Short(pi.PropertyType)}",
        FieldInfo fi => $"field {fi.Name}:{Short(fi.FieldType)}",
        EventInfo ei => $"event {ei.Name}",
        _ => null,
    };

    private static string Params(ParameterInfo[] ps) => string.Join(",", ps.Select(p => Short(p.ParameterType)));
    private static string Short(Type t) => t.IsGenericType
        ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(Short))}>"
        : t.Name;
}
