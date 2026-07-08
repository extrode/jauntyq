using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>
/// Consumer-gaps-report gap #3: jsonb/json/inet/array Postgres types
/// silently mapped to `object` with no build-time signal. These tests
/// confirm the dedicated mappings and the object-fallback detector.
/// </summary>
public class DialectMapperTests
{
    [Theory]
    [InlineData("json", false, "string")]
    [InlineData("json", true, "string?")]
    [InlineData("jsonb", false, "string")]
    [InlineData("jsonb", true, "string?")]
    public void JsonTypes_MapToString(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable));
    }

    [Theory]
    [InlineData("inet", false, "System.Net.IPAddress")]
    [InlineData("inet", true, "System.Net.IPAddress?")]
    [InlineData("cidr", false, "System.Net.IPAddress")]
    [InlineData("cidr", true, "System.Net.IPAddress?")]
    public void NetworkTypes_MapToIPAddress(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable));
    }

    [Theory]
    [InlineData("text[]", false, "string[]")]
    [InlineData("text[]", true, "string[]?")]
    [InlineData("integer[]", false, "int[]")]
    [InlineData("uuid[]", false, "System.Guid[]")]
    public void ArrayTypes_RecurseOnElementType(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable));
    }

    [Fact]
    public void UnrecognizedArrayElementType_StillDegradesToObjectArray()
    {
        // An array of a type DialectMapper itself doesn't know still falls
        // through to object[], and IsUnmappedDbType must see through the
        // "[]" wrapper to flag it (not treat "object[]" as "mapped").
        Assert.Equal("object[]", DialectMapper.MapDbTypeToCSharp("some_enum_type[]", false));
        Assert.True(DialectMapper.IsUnmappedDbType("some_enum_type[]", false));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("jsonb")]
    [InlineData("inet")]
    [InlineData("cidr")]
    [InlineData("text[]")]
    [InlineData("int")]
    public void MappedTypes_AreNotFlaggedUnmapped(string dbType)
    {
        Assert.False(DialectMapper.IsUnmappedDbType(dbType, false));
    }

    [Fact]
    public void GenuinelyUnknownType_IsFlaggedUnmapped()
    {
        Assert.True(DialectMapper.IsUnmappedDbType("some_enum_type", false));
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("some_enum_type", false));
    }
}
