using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

public class CSharpToSqlTypeMapperTests
{
    [Theory]
    [InlineData("int", "int")]
    [InlineData("long", "bigint")]
    [InlineData("short", "smallint")]
    [InlineData("bool", "bit")]
    [InlineData("decimal", "decimal(18,2)")]
    [InlineData("double", "float")]
    [InlineData("float", "real")]
    [InlineData("string", "nvarchar(MAX)")]
    [InlineData("System.DateTime", "datetime2")]
    [InlineData("System.TimeSpan", "time")]
    [InlineData("System.Guid", "uniqueidentifier")]
    [InlineData("byte[]", "varbinary(MAX)")]
    public void Map_KnownTypes(string csharpType, string expectedSqlType)
    {
        Assert.Equal(expectedSqlType, CSharpToSqlTypeMapper.Map(csharpType));
    }

    [Theory]
    [InlineData("int?", "int")]
    [InlineData("decimal?", "decimal(18,2)")]
    [InlineData("bool?", "bit")]
    [InlineData("System.DateTime?", "datetime2")]
    [InlineData("System.Guid?", "uniqueidentifier")]
    public void Map_NullableTypes_StripsNullableSuffix(string csharpType, string expectedSqlType)
    {
        Assert.Equal(expectedSqlType, CSharpToSqlTypeMapper.Map(csharpType));
    }

    [Theory]
    [InlineData("object")]
    [InlineData("SomeCustomType")]
    public void Map_UnknownTypes_ReturnsSqlVariant(string csharpType)
    {
        Assert.Equal("sql_variant", CSharpToSqlTypeMapper.Map(csharpType));
    }
}
