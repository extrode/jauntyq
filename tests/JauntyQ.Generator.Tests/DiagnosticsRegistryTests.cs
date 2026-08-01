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
    [InlineData("JNT1004", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1005", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1006", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1007", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT2001", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2002", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2003", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2004", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2005", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2006", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2007", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2008", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2009", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2010", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2011", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2012", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2013", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2014", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2015", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2016", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2017", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2018", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2019", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2020", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT3001", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT3003", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3004", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3005", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3006", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3009", DiagnosticSeverity.Error, "JauntyQ.Projection")]
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
    [InlineData("JNT8006", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8007", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8008", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT9001", DiagnosticSeverity.Warning, "JauntyQ.Migrations")]
    [InlineData("JNT9002", DiagnosticSeverity.Error, "JauntyQ.Migrations")]
    [InlineData("JNT9003", DiagnosticSeverity.Error, "JauntyQ.Migrations")]
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
        Assert.Equal(57, DiagnosticFields.Length); // +JNT2020 upsert skipped for expression-only unique key (2026-08-01); +JNT2019 upsert skipped for prefix-only unique key (2026-07-30); +JNT2018 non-atomic key-targeted MySQL upsert (2026-07-30, AUD-R4-16); +JNT2016 duplicate enum member name, +JNT2017 enum type name collision (2026-07-29, spec 013 enum capture); +JNT2015 table skipped for an unquotable identifier (2026-07-29, AUD-R64-01 T8 residual); +JNT2012 reserved parameter name prefix, +JNT2013 duplicate parameter name, +JNT2014 table skipped for colliding column names (2026-07-28 round-75 audit, closing the round-67/round-69 residuals and AUD-R75-03); +JNT2011 duplicate column property name (2026-07-19 round-64 audit); +JNT2010 duplicate entity accessor name (2026-07-18 round-46 audit); +JNT2009 duplicate sequence accessor name (2026-07-18 round-44 audit); +JNT1007 unsupported subquery, +JNT1006 unsupported UNION (2026-07-16); +JNT1005 nesting too deep, +JNT2008 duplicate generated file, +JNT3009 duplicate result column (2026-07-11 round-2 audit); +JNT1004 unsupported character (2026-07-11); +JNT3008 unrecognized directive (2026-07-10); +JNT8001..8005 performance (2026-07-05); +JNT2004 illegal identifier (2026-07-05); +JNT3003 invalid directive combination (2026-07-05); +JNT2005 stored procedure not found (2026-07-05); +JNT3004/3005/3006 expression projections + JNT7002 dialect gating (2026-07-06); +JNT3007 IN-subquery column count (2026-07-06); +JNT2006 entity/row POCO name collision (2026-07-08); +JNT2007 unmapped column type (2026-07-08); +JNT7003 unknown dialect (2026-07-09); +JNT1002 unterminated token (2026-07-09); +JNT1003 input too large (2026-07-09); +JNT9003 missing/unknown dialect for DDL schema source (2026-07-09); +JNT9004 migration risky impact (2026-07-10); +JNT8006 join not a foreign key + JNT8007 unindexed order by (2026-07-10); +JNT8008 N+1 heuristic (2026-07-10); +JNT6002 multiple schema snapshots (2026-07-10)
    }
}
