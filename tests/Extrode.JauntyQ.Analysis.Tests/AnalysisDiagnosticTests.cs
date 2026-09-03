using Extrode.JauntyQ.Analysis;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>Public-API coverage for <see cref="AnalysisDiagnostic"/>.</summary>
public class AnalysisDiagnosticTests
{
    [Fact]
    public void ToString_IsCodeColonMessage()
    {
        var d = new AnalysisDiagnostic("JNT9002", "Migration invalid", AnalysisSeverity.Error);
        Assert.Equal("JNT9002: Migration invalid", d.ToString());
    }

    [Fact]
    public void ErrorAndWarning_FactoriesSetSeverity()
    {
        var e = AnalysisDiagnostic.Error("JNT9002", "boom");
        var w = AnalysisDiagnostic.Warning("JNT9001", "heads up");

        Assert.Equal(AnalysisSeverity.Error, e.Severity);
        Assert.Equal(AnalysisSeverity.Warning, w.Severity);
        Assert.Equal("JNT9002", e.Code);
        Assert.Equal("heads up", w.Message);
    }
}
