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
    /// Gaps confirmed live against real containers (Testcontainers SQL
    /// Server/Postgres/MySQL): each of these dbType strings previously fell
    /// through MapDbTypeToCSharp's switch to "object" despite the provider
    /// returning a perfectly well-typed CLR value.
    /// </summary>
    [Theory]
    [InlineData("double", false, "double")] // MySQL DOUBLE -> MySqlConnector System.Double
    [InlineData("mediumint", false, "int")] // MySQL MEDIUMINT (24-bit) -> System.Int32
    [InlineData("year", false, "int")] // MySQL YEAR -> System.Int32 (not DateTime)
    [InlineData("tinytext", true, "string?")] // MySQL TINYTEXT/MEDIUMTEXT/LONGTEXT -> System.String
    [InlineData("mediumtext", true, "string?")]
    [InlineData("longtext", true, "string?")]
    [InlineData("blob", true, "byte[]?")] // MySQL BLOB family -> System.Byte[]
    [InlineData("tinyblob", true, "byte[]?")]
    [InlineData("mediumblob", true, "byte[]?")]
    [InlineData("longblob", true, "byte[]?")]
    [InlineData("enum", false, "string")] // MySQL ENUM/SET -> System.String (member list not captured)
    [InlineData("set", false, "string")]
    public void PreviouslyUnmappedMySqlTypes_MapToRealClrType(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable));
    }

    [Theory]
    [InlineData("datetimeoffset", false, "System.DateTimeOffset")] // SQL Server
    [InlineData("datetimeoffset", true, "System.DateTimeOffset?")]
    [InlineData("time with time zone", false, "System.DateTimeOffset")] // Postgres
    [InlineData("time with time zone", true, "System.DateTimeOffset?")]
    public void OffsetAwareTemporalTypes_MapToDateTimeOffset_NotDateTimeOrTimeSpan(string dbType, bool isNullable, string expected)
    {
        // Confirmed live: Microsoft.Data.SqlClient hands back DateTimeOffset
        // for datetimeoffset, and Npgsql hands back DateTimeOffset for "time
        // with time zone" too (distinct from plain "time", which stays
        // TimeSpan) -- mapping either to DateTime/TimeSpan would silently
        // drop the UTC offset the column actually carries.
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

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(64)]
    public void MultiBitArrayColumn_DegradesToObjectArray_NotBoolArray(int length)
    {
        // The array-element recursion (MapDbTypeToCSharp's "[]" branch) must
        // forward `length` (and `dialect`) to the recursive call on the
        // element type, not drop them. Before this fix, a Postgres
        // "bit(n>1)[]" column recursed with length=null, which matches
        // SingleBitColumn_MapsToBool's null-length case -- silently mapping
        // a multi-bit array to bool[] instead of degrading to object[] the
        // same way a bare (non-array) bit(n>1) column already correctly does
        // (see MultiBitColumn_DegradesToObject_InsteadOfWrongBool). bool[]
        // would have been wrong the same way plain "bool" was wrong for a
        // multi-bit column: Npgsql hands back a BitArray for bit(n>1), not a
        // bool, so the generated (bool[])reader.GetValue(...) cast would
        // throw at read time.
        Assert.Equal("object[]", DialectMapper.MapDbTypeToCSharp("bit[]", false, length));
        Assert.True(DialectMapper.IsUnmappedDbType("bit[]", false, length));
    }

    /// <summary>
    /// SQL Server's tinyint is unsigned 0-255, stored on the wire as
    /// System.Byte -- confirmed empirically against a real SQL Server 2022
    /// container that reader.GetInt16() throws InvalidCastException against
    /// that provider type (no implicit widening), so the old dialect-blind
    /// "tinyint" -&gt; "short" mapping produced generated code that crashed at
    /// read time. MySQL's tinyint is signed by default (-128..127) and its
    /// GetInt16() already widens correctly -- confirmed live that GetByte()
    /// there throws OverflowException on a negative value -- so MySQL (and
    /// the no-dialect default, for any caller lacking schema/dialect context)
    /// must keep mapping to "short" unchanged.
    /// </summary>
    [Theory]
    [InlineData("sqlserver", false, "byte")]
    [InlineData("sqlserver", true, "byte?")]
    [InlineData("SqlServer", false, "byte")]
    [InlineData("mysql", false, "short")]
    [InlineData("mysql", true, "short?")]
    [InlineData("postgres", false, "short")]
    [InlineData(null, false, "short")]
    [InlineData(null, true, "short?")]
    public void TinyintColumn_MapsByDialect(string? dialect, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp("tinyint", isNullable, dialect: dialect));
        Assert.False(DialectMapper.IsUnmappedDbType("tinyint", isNullable));
    }
}
