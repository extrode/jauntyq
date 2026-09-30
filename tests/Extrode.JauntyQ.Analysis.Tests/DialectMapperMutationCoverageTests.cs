using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

/// <summary>
/// Direct coverage for DialectMapper gaps not exercised by DialectMapperTests:
/// ToPascalCase's digit handling and leading-digit/kept-char boundaries, the
/// enum-resolution path of MapColumnToCSharp/ResolveEnumTypeName, the
/// Precision-vs-MaxLength precedence feeding MapDbTypeToCSharp's length
/// parameter, IsRowVersion's early return, and IsUnmappedDbType's nullable
/// "object?" literal.
/// </summary>
[Trait("Category", "AuditRegression")]
public class DialectMapperMutationCoverageTests
{
    private static ColumnSchema Col(string name = "c", string dbType = "int", bool isNullable = false,
        bool isRowVersion = false, string? enumName = null, int? precision = null, int? maxLength = null) =>
        new()
        {
            Name = name, DbType = dbType, IsNullable = isNullable, IsRowVersion = isRowVersion,
            EnumName = enumName, Precision = precision, MaxLength = maxLength
        };

    // ── ToPascalCase: digit handling ────────────────────────────────────

    [Fact]
    public void ToPascalCase_DigitMidWord_DoesNotStartANewWord()
    {
        // A digit must be appended verbatim AND must not flip startOfWord to
        // true -- otherwise the letter right after it would be wrongly
        // capitalized as if the digit were a separator.
        Assert.Equal("Item2name", DialectMapper.ToPascalCase("item2name"));
    }

    [Theory]
    [InlineData("0abc", "_0abc")]
    [InlineData("9abc", "_9abc")]
    public void ToPascalCase_LeadingDigitBoundaries_GetUnderscorePrefixed(string input, string expected)
    {
        Assert.Equal(expected, DialectMapper.ToPascalCase(input));
    }

    [Theory]
    [InlineData("a", "A")]
    [InlineData("z", "Z")]
    [InlineData("A", "A")]
    [InlineData("Z", "Z")]
    public void ToPascalCase_LetterRangeBoundaries_AreKept(string input, string expected)
    {
        Assert.Equal(expected, DialectMapper.ToPascalCase(input));
    }

    [Theory]
    [InlineData("`")] // just below 'a' (0x60)
    [InlineData("{")] // just above 'z' (0x7B)
    [InlineData("@")] // just below 'A' (0x40)
    [InlineData("[")] // just above 'Z' (0x5B)
    [InlineData("/")] // just below '0' (0x2F)
    [InlineData(":")] // just above '9' (0x3A)
    public void ToPascalCase_JustOutsideKeptRanges_TreatedAsSeparators(string input)
    {
        // A char just outside every kept range contributes nothing -- the
        // whole input reduces to the "no usable characters" fallback.
        Assert.Equal("_", DialectMapper.ToPascalCase(input));
    }

    // ── MapColumnToCSharp: IsRowVersion short-circuit ───────────────────

    [Fact]
    public void MapColumnToCSharp_RowVersionColumn_MapsToNullableByteArray_RegardlessOfDbType()
    {
        var col = Col(dbType: "timestamp", isRowVersion: true);

        Assert.Equal("byte[]?", DialectMapper.MapColumnToCSharp(col));
        Assert.Equal("byte[]?", DialectMapper.MapColumnToCSharp(col, "sqlserver"));
    }

    // ── MapColumnToCSharp / ResolveEnumTypeName: the enum-resolution path ──

    private static DatabaseSchema SchemaWithEnum(string enumName, params string[] memberValues)
    {
        var schema = new DatabaseSchema();
        var en = new EnumSchema { Name = enumName };
        foreach (var v in memberValues)
            en.Members.Add(new EnumMember { Value = v, CSharpName = DialectMapper.ToPascalCase(v) });
        schema.Enums[enumName] = en;
        return schema;
    }

    [Fact]
    public void MapColumnToCSharp_ResolvedEnum_NonNullable_MapsToBareEnumTypeName()
    {
        var schema = SchemaWithEnum("order_status", "pending", "shipped");
        var col = Col(dbType: "order_status", isNullable: false, enumName: "order_status");

        Assert.Equal("OrderStatus", DialectMapper.MapColumnToCSharp(col, "postgres", schema));
    }

    [Fact]
    public void MapColumnToCSharp_ResolvedEnum_Nullable_AppendsQuestionMark()
    {
        var schema = SchemaWithEnum("order_status", "pending", "shipped");
        var col = Col(dbType: "order_status", isNullable: true, enumName: "order_status");

        Assert.Equal("OrderStatus?", DialectMapper.MapColumnToCSharp(col, "postgres", schema));
    }

    [Fact]
    public void ResolveEnumTypeName_NoSchema_ReturnsNullWithoutTouchingEnumName()
    {
        // Must not dereference `schema` when it's null, even though
        // column.EnumName is set -- the pre-013 fallback contract for
        // callers with no schema in hand (identity/param type inference).
        var col = Col(enumName: "order_status");

        Assert.Null(DialectMapper.ResolveEnumTypeName(col, null));
    }

    [Fact]
    public void ResolveEnumTypeName_SchemaProvided_ButNoEnumName_ReturnsNullWithoutLookup()
    {
        // Must not call schema.Enums.TryGetValue(null) when column.EnumName
        // is null/empty -- an ordinary non-enum column with a schema in hand.
        var schema = SchemaWithEnum("order_status", "pending");
        var col = Col(enumName: null);

        Assert.Null(DialectMapper.ResolveEnumTypeName(col, schema));
    }

    [Fact]
    public void ResolveEnumTypeName_EnumNameNotFoundInSchema_ReturnsNull()
    {
        var schema = SchemaWithEnum("order_status", "pending");
        var col = Col(enumName: "nonexistent_enum");

        Assert.Null(DialectMapper.ResolveEnumTypeName(col, schema));
    }

    [Fact]
    public void ResolveEnumTypeName_EnumWithNoMembers_TreatedAsUncaptured()
    {
        var schema = SchemaWithEnum("order_status"); // zero members
        var col = Col(enumName: "order_status");

        Assert.Null(DialectMapper.ResolveEnumTypeName(col, schema));
    }

    // ── MapColumnToCSharp: Precision takes precedence over MaxLength ────

    [Fact]
    public void MapColumnToCSharp_BothPrecisionAndMaxLengthSet_PrecisionWins()
    {
        // bit(5) (multi-bit -> object) vs bit(1) (single-bit -> bool):
        // Precision=5, MaxLength=1 must resolve via Precision, not MaxLength.
        var col = Col(dbType: "bit", precision: 5, maxLength: 1);

        Assert.Equal("object", DialectMapper.MapColumnToCSharp(col));
    }

    [Fact]
    public void MapColumnToCSharp_OnlyMaxLengthSet_FallsBackToMaxLength()
    {
        var col = Col(dbType: "bit", precision: null, maxLength: 5);

        Assert.Equal("object", DialectMapper.MapColumnToCSharp(col));
    }

    // ── MapDbTypeToCSharp: mysql "unsigned" gate only fires on a literal match ──

    [Fact]
    public void MySqlDialect_PlainIntWithNoUnsignedModifier_MapsAsOrdinarySignedInt()
    {
        // Guards the isMySql && dbType.Contains("unsigned") gate itself: a
        // plain mysql int column must map through the ordinary signed path,
        // not the UInt32 path reserved for a declared type that actually
        // says "unsigned".
        Assert.Equal("int", DialectMapper.MapDbTypeToCSharp("int", isNullable: false, dialect: "mysql"));
    }

    // ── IsUnmappedDbType: the nullable "object?" literal ────────────────

    [Fact]
    public void GenuinelyUnknownType_Nullable_MapsToNullableObject_AndIsFlaggedUnmapped()
    {
        Assert.Equal("object?", DialectMapper.MapDbTypeToCSharp("some_enum_type", true));
        Assert.True(DialectMapper.IsUnmappedDbType("some_enum_type", true));
    }

    // ── NormalizeDbType: the "(" boundary must be ">= 0", not "> 0" ─────

    [Fact]
    public void NormalizeDbType_ParenAtIndexZero_IsStillStripped()
    {
        // Guards parenIndex >= 0 vs. > 0: a leading "(" (parenIndex == 0)
        // must still trigger the strip, leaving "" as the base type, which
        // then degrades through the array branch to a plain "object" rather
        // than incorrectly retaining the literal text and matching the "[]"
        // suffix as if it were an array of the (nonexistent) base type.
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("(x)[]", isNullable: false));
    }

    // ── StripMySqlUnsignedModifier: the "unsigned" boundary must be ">= 0", not "> 0" ──

    [Fact]
    public void StripMySqlUnsignedModifier_UnsignedAtIndexZero_IsStillStripped()
    {
        // Guards unsignedIndex >= 0 vs. > 0: "unsigned[]" has the modifier
        // at index 0, so the strip must still fire, leaving "" as the base
        // type -- which then degrades to "object" through the array branch,
        // not "object[]" from the un-stripped literal falsely matching the
        // "[]" suffix check on its own account.
        Assert.Equal("object", DialectMapper.MapDbTypeToCSharp("unsigned[]", isNullable: false, dialect: "mysql"));
    }
}
