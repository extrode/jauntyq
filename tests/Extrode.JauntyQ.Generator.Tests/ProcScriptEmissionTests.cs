using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class ProcScriptEmissionTests
{
    private static string Generate(Func<string, string, string>? edit, params (string Path, string Text)[] extra)
        => GeneratedOutputApprovalTests.Generate(
            Path.Combine(GeneratedOutputApprovalTests.ApprovedDir(), "sqlserver"), edit, extra).Replace("\r\n", "\n");

    [Fact]
    public void ParameterlessProc_DeclaresNoParameterBlock()
    {
        string output = Generate(null,
            ("db/tables/Customers/ProcAllIds.sql", "-- @proc\nselect id from customers"),
            ("db/tables/Customers/ProcPurge.sql", "-- @proc\ndelete from customers"));

        Assert.Contains("@\"CREATE OR ALTER PROCEDURE [Customers_ProcAllIds]AS\nBEGIN\n", output);
        Assert.Contains("@\"CREATE OR ALTER PROCEDURE [Customers_ProcPurge]AS\nBEGIN\n", output);
    }

    [Fact]
    public void QueryParameterUnboundToAColumn_TakesTheTypeOfTheSameNamedProjectionColumn()
    {
        string output = Generate(null, ("db/tables/Customers/ProcPositive.sql", "-- @proc\nselect id from customers where @Id > 0"));

        Assert.Contains("[Customers_ProcPositive]\n    @Id int\nAS", output);
    }

    [Fact]
    public void DecimalColumnWithoutRecordedPrecision_KeepsTheMapperDefault()
    {
        string output = Generate(
            (file, text) => file == "jaunty.schema.json" ? text.Replace("\"precision\": 12,", "\"precision\": 0,") : text);

        Assert.Contains("[Customers_Rich]\n    @Balance decimal(18,2)\nAS", output);
    }
}
