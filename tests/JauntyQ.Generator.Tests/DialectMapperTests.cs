using JauntyQ.Analysis;
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

    /// <summary>
    /// Torture-test finding: MariaDB is intentionally absent -- it declares
    /// "dialect": "mysql" since it's wire/SQL-compatible with MySQL for
    /// everything the generator emits. Any other unrecognized string (typo,
    /// unsupported engine) is unknown.
    /// </summary>
    [Theory]
    [InlineData("sqlserver", true)]
    [InlineData("SqlServer", true)]
    [InlineData("postgres", true)]
    [InlineData("sqlite", true)]
    [InlineData("mysql", true)]
    [InlineData("mariadb", false)]
    [InlineData("oracle", false)]
    [InlineData("", false)]
    public void IsKnownDialect_RecognizesExactlyTheFourSupportedStrings(string dialect, bool expected)
    {
        Assert.Equal(expected, DialectMapper.IsKnownDialect(dialect));
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

    /// <summary>
    /// SQL Server's BIT is always single-bit, but MySQL and PostgreSQL both
    /// allow BIT(n > 1). Confirmed empirically against real MySQL 8 and
    /// PostgreSQL 16 containers: neither MySqlConnector (returns ulong) nor
    /// Npgsql (returns BitArray) hands back a bool for those, so mapping to
    /// "bool" would silently read the wrong value or throw at the
    /// (bool)reader.GetValue(...) cast the generator emits for a bool column.
    /// length=null (SQL Server, and any caller without column metadata) keeps
    /// the historical single-bit behavior.
    /// </summary>
    [Theory]
    [InlineData(null, false, "bool")]
    [InlineData(null, true, "bool?")]
    [InlineData(1, false, "bool")]
    [InlineData(1, true, "bool?")]
    public void SingleBitColumn_MapsToBool(int? length, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp("bit", isNullable, length));
        Assert.False(DialectMapper.IsUnmappedDbType("bit", isNullable, length));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(64)]
    public void MultiBitColumn_DegradesToObject_InsteadOfWrongBool(int length)
    {
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("bit", false, length));
        Assert.True(DialectMapper.IsUnmappedDbType("bit", false, length));
    }

    [Fact]
    public void BitVarying_AlreadyDegradesToObject()
    {
        // Postgres "bit varying(n)" was already unmapped before this fix
        // (no case matched the "bit varying" string); this pins that it
        // stays that way rather than accidentally starting to match "bit".
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("bit varying", false, 10));
        Assert.True(DialectMapper.IsUnmappedDbType("bit varying", false, 10));
    }
}
