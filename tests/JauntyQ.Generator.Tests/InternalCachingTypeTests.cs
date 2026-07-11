using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Direct coverage for the generator's internal cache-plumbing types
/// (exposed via InternalsVisibleTo). Their equality/hash contracts decide
/// whether the Roslyn incremental pipeline treats a re-run as unchanged, so a
/// regression here silently breaks incremental caching — worth testing head-on
/// rather than only through a driver.
/// </summary>
public class InternalCachingTypeTests
{
    private static DiagnosticDescriptor Desc(string id, string msg, DiagnosticSeverity sev) =>
        new(id, "title", msg, "JauntyQ", sev, isEnabledByDefault: true);

    // ── DiagnosticInfo ─────────────────────────────────────────────────────
    [Fact]
    public void DiagnosticInfo_From_RehydratesToDiagnosticWithArg()
    {
        var info = DiagnosticInfo.From(Desc("JNT9999", "value '{0}' is bad", DiagnosticSeverity.Error), "abc");
        Diagnostic d = info.ToDiagnostic();

        Assert.Equal("JNT9999", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("abc", d.GetMessage());
    }

    [Fact]
    public void DiagnosticInfo_ForValidation_MapsSeverity_AndTakesNoArgPath()
    {
        var warn = DiagnosticInfo.ForValidation(new ValidationError(Desc("JNTW", "w", DiagnosticSeverity.Warning), "warn msg"));
        var err = DiagnosticInfo.ForValidation(new ValidationError(Desc("JNTE", "e", DiagnosticSeverity.Error), "err msg"));

        // arg is null on the ForValidation path → the no-arg Diagnostic.Create branch.
        Assert.Equal(DiagnosticSeverity.Warning, warn.ToDiagnostic().Severity);
        Assert.Equal(DiagnosticSeverity.Error, err.ToDiagnostic().Severity);
        Assert.Equal("warn msg", warn.ToDiagnostic().GetMessage());
    }

    [Fact]
    public void DiagnosticInfo_Equals_And_GetHashCode_HonorValueSemantics()
    {
        var d = Desc("JNT9999", "m {0}", DiagnosticSeverity.Error);
        var a = DiagnosticInfo.From(d, "x");
        var same = DiagnosticInfo.From(d, "x");
        var otherArg = DiagnosticInfo.From(d, "y");
        var otherId = DiagnosticInfo.From(Desc("JNT8888", "m {0}", DiagnosticSeverity.Error), "x");

        Assert.True(a.Equals(same));
        Assert.True(a.Equals((object)same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());

        Assert.False(a.Equals(otherArg));
        Assert.False(a.Equals(otherId));
        Assert.False(a.Equals((object?)null));
        Assert.False(a.Equals("not a DiagnosticInfo"));
    }

    // ── FileSummary ────────────────────────────────────────────────────────
    [Fact]
    public void FileSummary_Equality_IsFieldwise()
    {
        var s = new FileSummary("Users", "GetAll", claims: true, emitted: true, canonicalTable: "users");
        var same = new FileSummary("Users", "GetAll", claims: true, emitted: true, canonicalTable: "users");

        Assert.True(s.Equals(same));
        Assert.True(s.Equals((object)same));
        Assert.Equal(s.GetHashCode(), same.GetHashCode());

        Assert.False(s.Equals(new FileSummary("Orders", "GetAll", true, true, "users")));
        Assert.False(s.Equals(new FileSummary("Users", "GetOne", true, true, "users")));
        Assert.False(s.Equals(new FileSummary("Users", "GetAll", false, true, "users")));
        Assert.False(s.Equals(new FileSummary("Users", "GetAll", true, false, "users")));
        Assert.False(s.Equals(new FileSummary("Users", "GetAll", true, true, canonicalTable: null)));
        Assert.False(s.Equals((object?)null));

        // Null canonical table is a valid, hashable shape.
        var noTable = new FileSummary("Users", "GetAll", true, true, canonicalTable: null);
        _ = noTable.GetHashCode();
    }

    // ── FileResult ─────────────────────────────────────────────────────────
    [Fact]
    public void FileResult_None_IsAnEmptyNonEmittingResult()
    {
        var r = FileResult.None("Users", "GetAll", claims: true);

        Assert.Null(r.HintName);
        Assert.Null(r.Source);
        Assert.True(r.Diagnostics.IsEmpty);
        Assert.Equal("Users", r.Summary.EntityName);
        Assert.True(r.Summary.Claims);
        Assert.False(r.Summary.Emitted);
    }

    [Fact]
    public void FileResult_WithDiagnostics_CarriesThemAndClaimsWithoutEmitting()
    {
        var diag = ImmutableArray.Create(DiagnosticInfo.From(Desc("JNT9999", "m", DiagnosticSeverity.Error), "x"));
        var r = FileResult.WithDiagnostics("Users", "GetAll", diag);

        Assert.Single(r.Diagnostics);
        Assert.True(r.Summary.Claims);
        Assert.False(r.Summary.Emitted);
        Assert.Null(r.Source);
    }

    // ── FileSummaryArrayComparer ───────────────────────────────────────────
    [Fact]
    public void FileSummaryArrayComparer_TreatsEqualShapesAsUnchanged()
    {
        var cmp = FileSummaryArrayComparer.Instance;
        var a = ImmutableArray.Create(new FileSummary("Users", "GetAll", true, true, "users"));
        var b = ImmutableArray.Create(new FileSummary("Users", "GetAll", true, true, "users"));
        var longer = a.Add(new FileSummary("Orders", "GetAll", false, true, "orders"));

        Assert.True(cmp.Equals(a, b));
        Assert.Equal(cmp.GetHashCode(a), cmp.GetHashCode(b));

        Assert.False(cmp.Equals(a, longer));               // length differs
        Assert.False(cmp.Equals(a, ImmutableArray.Create(  // element differs
            new FileSummary("Users", "GetOne", true, true, "users"))));
    }

    [Fact]
    public void FileSummaryArrayComparer_HandlesDefaultArrays()
    {
        var cmp = FileSummaryArrayComparer.Instance;
        ImmutableArray<FileSummary> def = default;
        var nonEmpty = ImmutableArray.Create(new FileSummary("U", "G", false, false, null));

        Assert.True(cmp.Equals(def, default));      // both default → equal
        Assert.False(cmp.Equals(def, nonEmpty));    // one default → not equal
        Assert.Equal(0, cmp.GetHashCode(def));      // default hashes to 0
    }
}
