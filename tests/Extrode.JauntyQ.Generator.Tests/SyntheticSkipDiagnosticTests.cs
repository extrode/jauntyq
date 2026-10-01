using System.Collections.Generic;
using Extrode.JauntyQ.Analysis;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class SyntheticSkipDiagnosticTests
{
    private static readonly AutoCrud.SyntheticQuery Synth =
        new("Lateral", "GetAll", "SELECT id\nFROM lateral", "lateral");

    private static readonly List<ValidationError> Errors = new()
    {
        new ValidationError(JauntyDiagnostics.JNT1009, "LATERAL joins are not supported"),
        new ValidationError(JauntyDiagnostics.JNT2002, "Column 'id' does not exist in any referenced table"),
    };

    [Fact]
    public void IsAJnt2027Warning()
    {
        var d = JauntyQGenerator.SyntheticSkipDiagnostic(Synth, Errors, "db/");

        Assert.Equal("JNT2027", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Message_NamesTheMethodTheTableAndEveryError()
    {
        string m = JauntyQGenerator.SyntheticSkipDiagnostic(Synth, Errors, "db/").GetMessage();

        Assert.Equal(
            "No auto-CRUD 'Lateral.GetAll' is generated for table 'lateral': the SQL JauntyQ generated for it " +
            "fails JauntyQ's own validation (JNT1009: LATERAL joins are not supported; JNT2002: Column 'id' does " +
            "not exist in any referenced table). This is a JauntyQ bug, not a problem with your schema; please " +
            "report it with the table's definition. To get the method meanwhile, write the query by hand as " +
            "'db/Lateral/GetAll.sql', quoting any identifier the error names.",
            m);
    }

    [Fact]
    public void Message_WithNoQueryRoot_DescribesThePathInstead()
    {
        string m = JauntyQGenerator.SyntheticSkipDiagnostic(Synth, Errors, "").GetMessage();

        Assert.Contains("write the query by hand as 'Lateral/GetAll.sql' under your SQL query root,", m);
    }
}
