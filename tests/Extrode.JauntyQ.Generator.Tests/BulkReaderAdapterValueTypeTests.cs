using Extrode.JauntyQ.Generator;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class BulkReaderAdapterValueTypeTests
{
    [Theory]
    [InlineData("mysql", "bigint unsigned")]
    [InlineData("sqlserver", "real")]
    public void NonNullableValueColumn_IsReturnedWithoutTheDBNullCoalesce(string dialect, string dbType)
    {
        var schema = new DatabaseSchema { Dialect = dialect };
        var table = new TableSchema { Name = "metrics" };
        table.Columns["v"] = new ColumnSchema { Name = "v", DbType = dbType };
        schema.Tables["metrics"] = table;

        string code = CodeEmitter.EmitBulkInsert("Metrics", "Metric", table, dialect, schema);

        Assert.Contains("                    case 0: return _current.V;\n", code.Replace("\r\n", "\n"));
    }
}
