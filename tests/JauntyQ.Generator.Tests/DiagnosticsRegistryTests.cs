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
    [InlineData("JNT2001", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2002", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2003", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT3001", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT4003", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT4004", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT5001", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT5002", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT6001", DiagnosticSeverity.Error, "JauntyQ.Configuration")]
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
        Assert.Equal(12, DiagnosticFields.Length); // +JNT5001/JNT5002 value safety (2026-07-05)
    }
}
