using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class ProcCallOutputParameterTests
{
    private static string Emit(int outLength = 50, int inOutLength = 20)
    {
        var proc = new ProcedureSchema { Name = "P" };
        proc.Params.Add(new ProcedureParam { Name = "Code", DbType = "nvarchar", Direction = ProcedureParamDirection.In, MaxLength = 10 });
        proc.Params.Add(new ProcedureParam { Name = "OName", DbType = "nvarchar", Direction = ProcedureParamDirection.Out, MaxLength = outLength });
        proc.Params.Add(new ProcedureParam { Name = "OCount", DbType = "int", Direction = ProcedureParamDirection.Out });
        proc.Params.Add(new ProcedureParam { Name = "IoLabel", DbType = "nvarchar", Direction = ProcedureParamDirection.InOut, MaxLength = inOutLength });
        proc.Params.Add(new ProcedureParam { Name = "IoQty", DbType = "int", Direction = ProcedureParamDirection.InOut });
        proc.Results.Add(new ColumnSchema { Name = "Id", DbType = "int" });
        var schema = new DatabaseSchema { Dialect = "sqlserver" };
        schema.Procedures["P"] = proc;
        return CodeEmitter.EmitProcCall("E", "P", proc, "sqlserver", schema).Replace("\r\n", "\n");
    }

    [Fact]
    public void OutAndInOutParameters_AreSizedBoundAndReadBackIntoTheTuple()
    {
        string code = Emit();

        Assert.Contains("__p1.Direction = ParameterDirection.Output;\n                __p1.Size = 50;\n", code);
        Assert.Contains("__p2.Direction = ParameterDirection.Output;\n                __cmd.Parameters.Add(__p2);", code);
        Assert.Contains("__p3.Direction = ParameterDirection.InputOutput;\n                __p3.Size = 20;\n", code);
        Assert.Contains("__p3.Value = (object?)ioLabel ?? DBNull.Value;", code);
        Assert.Contains("__p4.Value = ioQty;", code);
        Assert.Contains("return (__results, __oNameOut, __oCountOut, __ioLabelOut, __ioQtyOut);", code);
    }

    [Fact]
    public void ZeroMaxLength_EmitsNoSize()
    {
        string code = Emit(0, 0);

        Assert.DoesNotContain(".Size", code);
    }
}
