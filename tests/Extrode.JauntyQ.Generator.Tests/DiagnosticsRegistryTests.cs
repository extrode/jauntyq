using System.Reflection;
using Microsoft.CodeAnalysis;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

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
    // Error, and the severity is the entire point of the code: Roslyn already
    // reports a generator that throws, as CS8785, and CS8785 is a Warning. A
    // Warning here would restate the thing that made the failure invisible.
    [InlineData("JNT0001", DiagnosticSeverity.Error, "JauntyQ.Internal")]
    [InlineData("JNT1001", DiagnosticSeverity.Warning, "JauntyQ.Parsing")]
    [InlineData("JNT1002", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1003", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1004", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1005", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1006", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1007", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    [InlineData("JNT1008", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
    // Split out of JNT2001: missing grammar and a missing relation call for
    // opposite responses from the reader, so they cannot share a code.
    [InlineData("JNT1009", DiagnosticSeverity.Error, "JauntyQ.Parsing")]
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
    // Error, unlike the JNT2014/JNT2015 skips beside it: those report methods
    // the generator declined to invent, this reports a write the consumer wrote
    // by hand against a relation the engine will refuse.
    [InlineData("JNT2021", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2022", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    // Spec 014. JNT2023 is an Error because nothing can be emitted for either
    // side of a name collision; the other two are Warnings because the rest of
    // the schema must still generate.
    [InlineData("JNT2023", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT2024", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2025", DiagnosticSeverity.Warning, "JauntyQ.Schema")]
    [InlineData("JNT2026", DiagnosticSeverity.Error, "JauntyQ.Schema")]
    [InlineData("JNT3001", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT3002", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3003", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3004", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3005", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3006", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3007", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3008", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT3009", DiagnosticSeverity.Error, "JauntyQ.Projection")]
    [InlineData("JNT3010", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    // Warning: the file still generates and the last directive line still wins.
    [InlineData("JNT3011", DiagnosticSeverity.Warning, "JauntyQ.Projection")]
    [InlineData("JNT7001", DiagnosticSeverity.Error, "JauntyQ.Dialect")]
    [InlineData("JNT7002", DiagnosticSeverity.Error, "JauntyQ.Dialect")]
    [InlineData("JNT7003", DiagnosticSeverity.Error, "JauntyQ.Dialect")]
    [InlineData("JNT4003", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT4004", DiagnosticSeverity.Error, "JauntyQ.Parameters")]
    [InlineData("JNT5001", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT5002", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT5003", DiagnosticSeverity.Error, "JauntyQ.ValueSafety")]
    [InlineData("JNT6001", DiagnosticSeverity.Error, "JauntyQ.Configuration")]
    [InlineData("JNT6002", DiagnosticSeverity.Warning, "JauntyQ.Configuration")]
    // Warning: a typo in the file written to quieten the build must not be
    // louder than the warnings it was written to quieten.
    [InlineData("JNT6003", DiagnosticSeverity.Warning, "JauntyQ.Configuration")]
    [InlineData("JNT8001", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8002", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8003", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8004", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8005", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8006", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8007", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8008", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8009", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8010", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8011", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    // Warning, not Error: a stale suppression is untidy, not wrong.
    [InlineData("JNT8012", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT8013", DiagnosticSeverity.Warning, "JauntyQ.Performance")]
    [InlineData("JNT9001", DiagnosticSeverity.Warning, "JauntyQ.Migrations")]
    [InlineData("JNT9002", DiagnosticSeverity.Error, "JauntyQ.Migrations")]
    [InlineData("JNT9003", DiagnosticSeverity.Error, "JauntyQ.Migrations")]
    [InlineData("JNT9004", DiagnosticSeverity.Warning, "JauntyQ.Migrations")]
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
        Assert.Equal(75, DiagnosticFields.Length); // +JNT8013 unnecessary N+1 acceptance (2026-09-18, -- @allow-n-plus-one); +JNT2026 duplicate return-value parameter (2026-08-31, finishing ProcedureParamDirection.ReturnValue); +JNT0001 generator internal error (2026-08-30, a consumer consumer report item 1 — a generator that throws was reported only as a CS8785 warning); +JNT2023 function name collision, +JNT2024 unmappable function signature, +JNT2025 table-valued parameter not supported (2026-08-30, spec 014 functions and UDTs); +JNT6003 invalid acceptance file (2026-08-25, spec 016 jaunty.accept.json); +JNT3011 duplicate directive (2026-08-25, non-repeatable directive written twice); +JNT2022 lossy identifier rename (2026-08-18, non-ASCII column/table names); +JNT5003 literal type mismatch (2026-08-17, `bit` outside MySQL); +JNT1009 unsupported syntax, +JNT2021 write to view, +JNT8012 unnecessary unindexed acceptance (2026-08-17, spec 015); +JNT8011 predicate drift, +JNT3010 mirrored query not comparable (2026-08-03); +JNT8009 unstable pagination, +JNT8010 unordered pagination (2026-08-03); +JNT1008 multiple statements in one file (2026-08-02); +JNT2020 upsert skipped for expression-only unique key (2026-08-01); +JNT2019 upsert skipped for prefix-only unique key (2026-07-30); +JNT2018 non-atomic key-targeted MySQL upsert (2026-07-30, AUD-R4-16); +JNT2016 duplicate enum member name, +JNT2017 enum type name collision (2026-07-29, spec 013 enum capture); +JNT2015 table skipped for an unquotable identifier (2026-07-29, AUD-R64-01 T8 residual); +JNT2012 reserved parameter name prefix, +JNT2013 duplicate parameter name, +JNT2014 table skipped for colliding column names (2026-07-28 round-75 audit, closing the round-67/round-69 residuals and AUD-R75-03); +JNT2011 duplicate column property name (2026-07-19 round-64 audit); +JNT2010 duplicate entity accessor name (2026-07-18 round-46 audit); +JNT2009 duplicate sequence accessor name (2026-07-18 round-44 audit); +JNT1007 unsupported subquery, +JNT1006 unsupported UNION (2026-07-16); +JNT1005 nesting too deep, +JNT2008 duplicate generated file, +JNT3009 duplicate result column (2026-07-11 round-2 audit); +JNT1004 unsupported character (2026-07-11); +JNT3008 unrecognized directive (2026-07-10); +JNT8001..8005 performance (2026-07-05); +JNT2004 illegal identifier (2026-07-05); +JNT3003 invalid directive combination (2026-07-05); +JNT2005 stored procedure not found (2026-07-05); +JNT3004/3005/3006 expression projections + JNT7002 dialect gating (2026-07-06); +JNT3007 IN-subquery column count (2026-07-06); +JNT2006 entity/row POCO name collision (2026-07-08); +JNT2007 unmapped column type (2026-07-08); +JNT7003 unknown dialect (2026-07-09); +JNT1002 unterminated token (2026-07-09); +JNT1003 input too large (2026-07-09); +JNT9003 missing/unknown dialect for DDL schema source (2026-07-09); +JNT9004 migration risky impact (2026-07-10); +JNT8006 join not a foreign key + JNT8007 unindexed order by (2026-07-10); +JNT8008 N+1 heuristic (2026-07-10); +JNT6002 multiple schema snapshots (2026-07-10)
    }
}
