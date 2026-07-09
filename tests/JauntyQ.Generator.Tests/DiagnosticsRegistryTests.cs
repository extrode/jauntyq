using System.Reflection;
using Microsoft.CodeAnalysis;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class DiagnosticsRegistryTests
{
    private static readonly FieldInfo[] DiagnosticFields =
        typeof(JauntyDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .ToArray();

    [Fact]
    public void AllDescriptors_HaveJNTPrefix()
    {
        foreach (var field in DiagnosticFields)
        {
            var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;
            Assert.StartsWith("JNT", descriptor.Id);
        }
    }

    [Fact]
    public void AllDescriptors_HaveUniqueIds()
    {
        var ids = DiagnosticFields
            .Select(f => ((DiagnosticDescriptor)f.GetValue(null)!).Id)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void AllDescriptors_FieldNameMatchesId()
    {
        foreach (var field in DiagnosticFields)
        {
            var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;
            Assert.Equal(field.Name, descriptor.Id);
        }
    }

    [Fact]
    public void AllDescriptors_AreEnabled()
    {
        foreach (var field in DiagnosticFields)
        {
            var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;
            Assert.True(descriptor.IsEnabledByDefault, $"{descriptor.Id} should be enabled by default");
        }
    }

    [Theory]
    [InlineData("JNT1001", DiagnosticSeverity.Warning, "JauntyQ.Parsing")]
    [InlineData("JNT1002", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1003", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT2001", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2002", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2003", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2004", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2005", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2006", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2007", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT3001", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT3003", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3004", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3005", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3006", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT7002", DiagnosticSeverity.Error, "JauntyQ.Dialect")]
    [InlineData("JNT7003", DiagnosticSeverity.Error, "JauntyQ.Dialect")]
    [InlineData("JNT4003", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT4004", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT5001", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT5002", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT6001", DiagnosticSeverity.Error, "JauntyQ.Configuration")]
    [InlineData("JNT8001", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8002", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8003", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8004", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8005", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT9001", DiagnosticSeverity.Warning, "JauntyQ.Migrations")]
    [InlineData("JNT9002", DiagnosticSeverity.Error, "JauntyQ.Migrations")]
    public void Descriptor_HasCorrectSeverityAndCategory(string id, DiagnosticSeverity severity, string category)
    {
        var field = DiagnosticFields.Single(f => f.Name == id);
        var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;

        Assert.Equal(id, descriptor.Id);
        Assert.Equal(severity, descriptor.DefaultSeverity);
        Assert.Equal(category, descriptor.Category);
    }

    [Fact]
    public void Registry_ContainsExpectedCount()
    {
        Assert.Equal(32, DiagnosticFields.Length); // +JNT8001..8005 performance (2026-07-05); +JNT2004 illegal identifier (2026-07-05); +JNT3003 invalid directive combination (2026-07-05); +JNT2005 stored procedure not found (2026-07-05); +JNT3004/3005/3006 expression projections + JNT7002 dialect gating (2026-07-06); +JNT3007 IN-subquery column count (2026-07-06); +JNT2006 entity/row POCO name collision (2026-07-08); +JNT2007 unmapped column type (2026-07-08); +JNT7003 unknown dialect (2026-07-09); +JNT1002 unterminated token (2026-07-09); +JNT1003 input too large (2026-07-09)
    }
}
