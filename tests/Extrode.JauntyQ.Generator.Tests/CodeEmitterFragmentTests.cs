using System.Text;
using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CodeEmitterFragmentTests
{
    [Theory]
    [InlineData("byte[]", 50, null, null, false, "p.Size = v.Length > 50 ? v.Length : 50;\n")]
    [InlineData("byte[]?", 10, null, null, false, "p.Size = v == null ? 10 : (v.Length > 10 ? v.Length : 10);\n")]
    [InlineData("string", 0, null, null, true, "p.Size = 0;\n")]
    [InlineData("string?", -1, null, null, false, "p.Size = -1;\n")]
    [InlineData("decimal?", null, 12, 2, false, "p.Precision = 12;\np.Scale = 2;\n")]
    [InlineData("decimal", null, 0, 0, false, "")]
    [InlineData("int", 4, 10, 0, true, "")]
    public void EmitParameterSizing_SizesTextBinaryAndDecimalOnly(string type, int? maxLength, int? precision, int? scale, bool write, string expected)
    {
        var sb = new StringBuilder();

        CodeEmitter.EmitParameterSizing(sb, type, maxLength, precision, scale, write, "p", "v", indent: "");

        Assert.Equal(expected, sb.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void EmitColumnNames_UsesTheSourceNameWhenPresentAndTheColumnNameOtherwise()
    {
        var projection = new ProjectionModel
        {
            Columns =
            {
                new ProjectionColumn { Name = "Id" },
                new ProjectionColumn { Name = "Total", SourceName = "sum_total" },
            },
        };
        var sb = new StringBuilder();

        CodeEmitter.EmitColumnNames(sb, "M", projection);

        Assert.Contains("__MColumns = { \"Id\", \"sum_total\" }", sb.ToString());
    }

    [Fact]
    public void GetReaderCall_EnumWithoutColumnIdentity_NamesTheOrdinal()
    {
        var schema = new DatabaseSchema { Dialect = "postgres" };
        schema.Enums["mood"] = new EnumSchema { Name = "mood", Members = { new EnumMember { Value = "ok", CSharpName = "Ok" } } };
        var column = new ColumnSchema { Name = "m", DbType = "mood", EnumName = "mood" };
        schema.Tables["t"] = new TableSchema { Name = "t", Columns = { ["m"] = column } };
        string type = DialectMapper.MapColumnToCSharp(column, "postgres", schema);

        Assert.Contains("\"ordinal 3\"", CodeEmitter.GetReaderCall(type, 3, schema));
    }

    [Theory]
    [InlineData("-- a\r\n\r\nselect 1\r\nfrom t", "select 1\nfrom t")]
    [InlineData("-- only\n\n", "")]
    public void StripLeadingSqlComments_DropsLeadingCommentLinesAndNormalisesLineEnds(string sql, string expected)
    {
        Assert.Equal(expected, CodeEmitter.StripLeadingSqlComments(sql));
    }
}
