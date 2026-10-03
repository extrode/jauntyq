using System.Text;
using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class SequenceAccessorEmissionTests
{
    private static string Emit(string dialect, params string[] names)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        foreach (var n in names)
            schema.Sequences[n] = new SequenceSchema { Name = n };
        var sb = new StringBuilder();
        CodeEmitter.EmitSequenceAccessor(sb, schema);
        return sb.ToString();
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("oracle")]
    public void DialectWithoutSequences_EmitsNothing(string dialect)
    {
        Assert.Equal("", Emit(dialect, "order_seq"));
    }

    [Fact]
    public void HostileSequenceName_IsSkipped()
    {
        string code = Emit("sqlserver", "bad-name", "order_seq");

        Assert.Contains("NextOrderSeq", code);
        Assert.DoesNotContain("bad-name", code);
        Assert.DoesNotContain("NextBadName", code);
    }

    [Fact]
    public void ReservedAndCaseFoldingSequenceNames_AreSkipped()
    {
        string code = Emit("postgres", "select", "Big_Seq", "order_seq");

        Assert.Contains("NextOrderSeq", code);
        Assert.DoesNotContain("NextSelect", code);
        Assert.DoesNotContain("NextBigSeq", code);
    }

    [Fact]
    public void EverySequenceSkipped_EmitsNothing()
    {
        Assert.Equal("", Emit("postgres", "select", "Big_Seq"));
    }

    [Fact]
    public void RowPoco_NonNullBlobColumn_IsRequired()
    {
        var table = new TableSchema { Name = "files" };
        table.Columns["data"] = new ColumnSchema { Name = "data", DbType = "bytea", IsNullable = false };

        Assert.Contains("public required byte[] Data { get; set; }", CodeEmitter.EmitRowPoco("FileRow", table, "postgres"));
    }
}
