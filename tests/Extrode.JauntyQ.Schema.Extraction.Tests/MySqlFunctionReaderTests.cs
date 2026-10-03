using System;
using System.Data;
using System.Threading.Tasks;
using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.Schema.Extraction;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class MySqlFunctionReaderTests
{
    [Fact]
    public async Task NullReturnAndParameterTypes_AreRecordedAsEmpty()
    {
        var table = new DataTable();
        table.Columns.Add("fn_name", typeof(string));
        table.Columns.Add("return_type", typeof(string));
        table.Columns.Add("return_max_length", typeof(long));
        table.Columns.Add("return_precision", typeof(long));
        table.Columns.Add("return_scale", typeof(long));
        table.Columns.Add("param_name", typeof(string));
        table.Columns.Add("param_type", typeof(string));
        table.Columns.Add("param_max_length", typeof(long));
        table.Columns.Add("param_precision", typeof(long));
        table.Columns.Add("param_scale", typeof(long));
        table.Rows.Add("f", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
        table.Rows.Add("f", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, "@a", DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
        var schema = new DatabaseSchema { Dialect = "mysql" };

        await MySqlExtractor.ReadFunctionsAsync(table.CreateDataReader(), "db", schema);

        var fn = Assert.Single(schema.Functions);
        Assert.Equal("f()", fn.Key);
        Assert.Equal("", fn.Value.Return.DbType);
        Assert.Equal("", Assert.Single(fn.Value.Params).DbType);
    }
}
