using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Extrode.JauntyQ.Schema.Extraction;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class MySqlReaderHelperTests
{
    private static DbDataReader ReaderOver(object value)
    {
        var table = new DataTable();
        table.Columns.Add("c", value is DBNull ? typeof(string) : value.GetType());
        table.Rows.Add(value);
        var reader = table.CreateDataReader();
        reader.Read();
        return reader;
    }

    [Fact]
    public async Task NullableInt64_NullColumn_IsNull()
    {
        using var reader = ReaderOver(DBNull.Value);

        Assert.Null(await MySqlExtractor.NullableInt64Async(reader, 0));
    }

    [Fact]
    public async Task NullableInt64_ValueColumn_IsTheValue()
    {
        using var reader = ReaderOver(42L);

        Assert.Equal(42L, await MySqlExtractor.NullableInt64Async(reader, 0));
    }

    [Fact]
    public async Task StringOr_NullColumn_IsTheFallback()
    {
        using var reader = ReaderOver(DBNull.Value);

        Assert.Equal("IN", await MySqlExtractor.StringOrAsync(reader, 0, "IN"));
    }

    [Fact]
    public async Task StringOr_ValueColumn_IsTheValue()
    {
        using var reader = ReaderOver("OUT");

        Assert.Equal("OUT", await MySqlExtractor.StringOrAsync(reader, 0, "IN"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, 0)]
    [InlineData(65535L, 65535)]
    [InlineData(2147483647L, 2147483647)]
    [InlineData(2147483648L, -1)]
    [InlineData(4294967295L, -1)]
    public void NarrowLength_OnlyValuesAboveIntMaxAreUnbounded(long? raw, int? expected)
    {
        Assert.Equal(expected, MySqlExtractor.NarrowLength(raw));
    }
}
