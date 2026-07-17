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

    /// <summary>
    /// MySQL's UNSIGNED modifier widens int/bigint/smallint's positive range
    /// beyond the equivalent signed CLR type's max (e.g. INT UNSIGNED's
    /// 4294967295 overflows System.Int32) -- MySqlConnector reports these as
    /// System.UInt32/UInt64/UInt16 on the wire. Regression for the bug where
    /// the extractor dropped the modifier entirely and everything mapped to
    /// the signed type, silently failing at read time above the signed max
    /// with zero build-time signal. Gated on dialect: "mysql" is required,
    /// mirroring how the extractor is the only source that ever produces this
    /// DbType shape.
    /// </summary>
    [Theory]
    [InlineData("int unsigned", false, "uint")]
    [InlineData("int unsigned", true, "uint?")]
    [InlineData("INT UNSIGNED", false, "uint")]
    [InlineData("integer unsigned", false, "uint")]
    [InlineData("bigint unsigned", false, "ulong")]
    [InlineData("bigint unsigned", true, "ulong?")]
    [InlineData("smallint unsigned", false, "ushort")]
    [InlineData("smallint unsigned", true, "ushort?")]
    public void UnsignedIntegerColumn_MapsToWideningClrType(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable, dialect: "mysql"));
        Assert.False(DialectMapper.IsUnmappedDbType(dbType, isNullable, dialect: "mysql"));
    }

    [Theory]
    [InlineData("tinyint unsigned", false, "short")]
    [InlineData("mediumint unsigned", false, "int")]
    public void UnsignedIntegerColumn_AlreadyFitsSignedMapping_Unchanged(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable, dialect: "mysql"));
    }

    [Fact]
    public void UnsignedIntegerColumn_WithLengthFacet_StillMapsToWideningClrType()
    {
        // The DbType format the extractor actually produces has no "(n)"
        // facet for MySQL (that only appears in COLUMN_TYPE, not DATA_TYPE),
        // but NormalizeDbType must handle either order robustly.
        Assert.Equal("uint", DialectMapper.MapDbTypeToCSharp("int(10) unsigned", false, dialect: "mysql"));
        Assert.Equal("uint", DialectMapper.MapDbTypeToCSharp("int(10) unsigned zerofill", false, dialect: "mysql"));
    }

    /// <summary>
    /// Regression: the unsigned routing must never fire for a non-MySQL
    /// dialect, even when the DbType string happens to contain the word
    /// "unsigned" (e.g. DDL ported verbatim from MySQL into SQLite, which has
    /// no real UNSIGNED wire semantics). Before this dialect gate, such a
    /// column silently mapped to uint/ulong/ushort, but Microsoft.Data.Sqlite
    /// boxes the value as a plain long -- the emitted "(ulong)reader.GetValue(i)"
    /// cast threw InvalidCastException on every read. It must keep degrading
    /// to the same "object" + JNT2007 signal any other unrecognized type gets.
    /// </summary>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    [InlineData(null)]
    public void UnsignedLookingDbType_NonMySqlDialect_StaysUnmapped(string? dialect)
    {
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("int unsigned", false, dialect: dialect));
        Assert.True(DialectMapper.IsUnmappedDbType("int unsigned", false, dialect: dialect));
    }

    // Round 13 (2026-07-17), mandate cell "2.8-remaining DbType families":
    // §2.8 requires confirming nullable AND non-nullable both map correctly
    // for every recognized DbType string, not just the 6 integer families
    // that already get the dedicated 4-boundary-probe JNT5002 treatment.
    // Every alias below already has a case in MapDbTypeToCSharp's switch --
    // this theory is the first place asserting BOTH nullability states for
    // every one of them in a single, exhaustive, discoverable table (several
    // aliases per family had never been direct-unit-tested at all before;
    // others had only one nullability state covered elsewhere in this file).
    [Theory]
    // --- signed int family (non-unsigned; "tinyint" excluded here --
    // it's dialect-conditional and already fully covered by
    // TinyintColumn_MapsByDialect above) ---
    [InlineData("int", false, "int")] [InlineData("int", true, "int?")]
    [InlineData("int4", false, "int")] [InlineData("int4", true, "int?")]
    [InlineData("integer", false, "int")] [InlineData("integer", true, "int?")]
    [InlineData("serial", false, "int")] [InlineData("serial", true, "int?")]
    [InlineData("mediumint", false, "int")] [InlineData("mediumint", true, "int?")]
    [InlineData("year", false, "int")] [InlineData("year", true, "int?")]
    [InlineData("bigint", false, "long")] [InlineData("bigint", true, "long?")]
    [InlineData("int8", false, "long")] [InlineData("int8", true, "long?")]
    [InlineData("bigserial", false, "long")] [InlineData("bigserial", true, "long?")]
    [InlineData("smallint", false, "short")] [InlineData("smallint", true, "short?")]
    [InlineData("int2", false, "short")] [InlineData("int2", true, "short?")]
    // Round 19 (AUD-R19-01): "smallserial" was the one serial-family alias
    // missing from this exhaustive table -- "serial" and "bigserial" (both
    // above) already had cases in MapDbTypeToCSharp's switch, but
    // "smallserial" fell through to the "object"/"object?" fallback until
    // this round's fix, so it belongs beside its "smallint"/"int2" siblings.
    [InlineData("smallserial", false, "short")] [InlineData("smallserial", true, "short?")]
    // --- string family ---
    [InlineData("varchar", false, "string")] [InlineData("varchar", true, "string?")]
    [InlineData("text", false, "string")] [InlineData("text", true, "string?")]
    [InlineData("nvarchar", false, "string")] [InlineData("nvarchar", true, "string?")]
    [InlineData("ntext", false, "string")] [InlineData("ntext", true, "string?")]
    [InlineData("character varying", false, "string")] [InlineData("character varying", true, "string?")]
    [InlineData("char", false, "string")] [InlineData("char", true, "string?")]
    [InlineData("nchar", false, "string")] [InlineData("nchar", true, "string?")]
    [InlineData("character", false, "string")] [InlineData("character", true, "string?")]
    [InlineData("citext", false, "string")] [InlineData("citext", true, "string?")]
    [InlineData("tinytext", false, "string")] // nullable=true for the 3 *text variants already covered by PreviouslyUnmappedMySqlTypes_MapToRealClrType
    [InlineData("mediumtext", false, "string")]
    [InlineData("longtext", false, "string")]
    [InlineData("enum", true, "string?")] // nullable=false for enum/set already covered above
    [InlineData("set", true, "string?")]
    // --- bool family (bit(<=1) already covered by SingleBitColumn_MapsToBool) ---
    [InlineData("bool", false, "bool")] [InlineData("bool", true, "bool?")]
    [InlineData("boolean", false, "bool")] [InlineData("boolean", true, "bool?")]
    // --- fixed-point / money family ---
    [InlineData("decimal", false, "decimal")] [InlineData("decimal", true, "decimal?")]
    [InlineData("numeric", false, "decimal")] [InlineData("numeric", true, "decimal?")]
    [InlineData("money", false, "decimal")] [InlineData("money", true, "decimal?")]
    [InlineData("smallmoney", false, "decimal")] [InlineData("smallmoney", true, "decimal?")]
    // --- double-precision floating point family ---
    [InlineData("float", false, "double")] [InlineData("float", true, "double?")]
    [InlineData("double precision", false, "double")] [InlineData("double precision", true, "double?")]
    [InlineData("float8", false, "double")] [InlineData("float8", true, "double?")]
    [InlineData("double", true, "double?")] // nullable=false for "double" already covered above
    // --- single-precision floating point family ---
    [InlineData("real", false, "float")] [InlineData("real", true, "float?")]
    [InlineData("float4", false, "float")] [InlineData("float4", true, "float?")]
    // --- date/time-without-offset family (datetimeoffset / time with time
    // zone already fully covered by OffsetAwareTemporalTypes_...) ---
    [InlineData("datetime", false, "System.DateTime")] [InlineData("datetime", true, "System.DateTime?")]
    [InlineData("timestamp", false, "System.DateTime")] [InlineData("timestamp", true, "System.DateTime?")]
    [InlineData("datetime2", false, "System.DateTime")] [InlineData("datetime2", true, "System.DateTime?")]
    [InlineData("date", false, "System.DateTime")] [InlineData("date", true, "System.DateTime?")]
    [InlineData("timestamp without time zone", false, "System.DateTime")] [InlineData("timestamp without time zone", true, "System.DateTime?")]
    [InlineData("timestamp with time zone", false, "System.DateTime")] [InlineData("timestamp with time zone", true, "System.DateTime?")]
    [InlineData("smalldatetime", false, "System.DateTime")] [InlineData("smalldatetime", true, "System.DateTime?")]
    [InlineData("time", false, "System.TimeSpan")] [InlineData("time", true, "System.TimeSpan?")]
    [InlineData("time without time zone", false, "System.TimeSpan")] [InlineData("time without time zone", true, "System.TimeSpan?")]
    // --- GUID family ---
    [InlineData("uniqueidentifier", false, "System.Guid")] [InlineData("uniqueidentifier", true, "System.Guid?")]
    [InlineData("uuid", false, "System.Guid")] [InlineData("uuid", true, "System.Guid?")]
    // --- binary family (blob/tinyblob/mediumblob/longblob nullable=true
    // already covered by PreviouslyUnmappedMySqlTypes_MapToRealClrType) ---
    [InlineData("bytea", false, "byte[]")] [InlineData("bytea", true, "byte[]?")]
    [InlineData("varbinary", false, "byte[]")] [InlineData("varbinary", true, "byte[]?")]
    [InlineData("binary", false, "byte[]")] [InlineData("binary", true, "byte[]?")]
    [InlineData("image", false, "byte[]")] [InlineData("image", true, "byte[]?")]
    [InlineData("blob", false, "byte[]")]
    [InlineData("tinyblob", false, "byte[]")]
    [InlineData("mediumblob", false, "byte[]")]
    [InlineData("longblob", false, "byte[]")]
    public void AllRecognizedDbTypeFamilies_MapNullableAndNonNullableCorrectly(string dbType, bool isNullable, string expected)
    {
        Assert.Equal(expected, DialectMapper.MapDbTypeToCSharp(dbType, isNullable));
        // Every one of these has a real case in the switch -- none should
        // ever be flagged as an unmapped/fallback type.
        Assert.False(DialectMapper.IsUnmappedDbType(dbType, isNullable));
    }

    // Round 14 (AUD-R14-01): direct unit coverage for the new public helper
    // itself, complementing the generator-level JNT2007-message assertions
    // in UnmappedColumnTypeTests.cs.
    [Theory]
    [InlineData("hierarchyid", true)]
    [InlineData("HIERARCHYID", true)] // case-insensitive
    [InlineData("hierarchyid(255)", true)] // parenthesized facet stripped, same as NormalizeDbType does for every other type
    [InlineData("geography", true)]
    [InlineData("geometry", true)]
    [InlineData("int", false)]
    [InlineData("varchar", false)]
    [InlineData("sql_variant", false)] // unmapped on SQL Server too, but not a CLR UDT
    [InlineData("xml", false)]
    public void IsKnownSqlServerClrUdtType_RecognizesOnlyHierarchyidGeographyGeometry(string dbType, bool expected)
    {
        Assert.Equal(expected, DialectMapper.IsKnownSqlServerClrUdtType(dbType));
    }
}
