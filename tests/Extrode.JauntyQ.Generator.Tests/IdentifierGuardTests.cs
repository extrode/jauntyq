using Extrode.JauntyQ.Analysis;
using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

/// <summary>
/// Build-time code-injection defenses. Names sourced from .sql files and
/// schema snapshots reach emitted C#; these tests pin the two guarantees that
/// keep a hostile name from injecting code: IsValidIdentifier rejects
/// non-identifiers, and ToPascalCase collapses any input to a safe identifier.
/// </summary>
public class IdentifierGuardTests
{
    [Theory]
    [InlineData("ProductId")]
    [InlineData("_underscore")]
    [InlineData("a1b2")]
    [InlineData("X")]
    public void IsValidIdentifier_AcceptsBareIdentifiers(string name)
    {
        Assert.True(IdentifierGuard.IsValidIdentifier(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1leading")]
    [InlineData("has space")]
    [InlineData("X { get; } static int Z = Evil(); //")]
    [InlineData("a\"b")]
    [InlineData("a;b")]
    [InlineData("a.b")]
    public void IsValidIdentifier_RejectsEverythingElse(string? name)
    {
        Assert.False(IdentifierGuard.IsValidIdentifier(name));
    }

    [Theory]
    [InlineData("class", "@class")]
    [InlineData("int", "@int")]
    [InlineData("public", "@public")]
    [InlineData("Product", "Product")] // not a keyword: unchanged
    public void Escape_PrefixesKeywordsOnly(string name, string expected)
    {
        Assert.Equal(expected, IdentifierGuard.Escape(name));
    }

    [Fact]
    public void ToStringLiteral_EscapesQuotesAndBackslashes()
    {
        // A bracket identifier could carry a quote; encoded, it can't break out
        // of the "..." literal in the generated __Columns array.
        Assert.Equal("a\\\"b", IdentifierGuard.ToStringLiteral("a\"b"));
        Assert.Equal("a\\\\b", IdentifierGuard.ToStringLiteral("a\\b"));
        Assert.Equal("a\\r\\nb", IdentifierGuard.ToStringLiteral("a\r\nb"));
    }

    [Theory]
    [InlineData('\u0085')] // NEL
    [InlineData('\u2028')] // LINE SEPARATOR
    [InlineData('\u2029')] // PARAGRAPH SEPARATOR
    public void ToStringLiteral_EscapesUnicodeLineTerminators(char lineTerminator)
    {
        // AUD-R62-01: these three code points are C# New_Line_Characters --
        // illegal unescaped inside a regular "..." literal even though they
        // are >= 0x20, so the old `c < 0x20` fallback let them pass through
        // untouched. A quoted SQL alias is free to contain them (that's the
        // point of quoting), so ToStringLiteral must escape them like \r/\n
        // rather than treat them as ordinary printable characters.
        string input = "a" + lineTerminator + "b";
        string result = IdentifierGuard.ToStringLiteral(input);
        Assert.DoesNotContain(lineTerminator, result);
        Assert.Equal($"a\\u{(int)lineTerminator:x4}b", result);
    }

    [Theory]
    [InlineData("Order Details", "OrderDetails")]   // real table name with a space
    [InlineData("product_name", "ProductName")]     // snake_case
    [InlineData("customer", "Customer")]
    public void ToPascalCase_PreservesLegitimateNames(string input, string expected)
    {
        Assert.Equal(expected, DialectMapper.ToPascalCase(input));
    }

    [Fact]
    public void ToPascalCase_NeutralizesInjectionPayload()
    {
        // A hostile alias must collapse to a plain identifier — no braces,
        // semicolons, or code survive into the emitted member name.
        string result = DialectMapper.ToPascalCase("X { get; } public static int Z = Evil(); //");
        Assert.True(IdentifierGuard.IsValidIdentifier(result),
            $"ToPascalCase output '{result}' must be a valid identifier");
        Assert.DoesNotContain("{", result);
        Assert.DoesNotContain(";", result);
        Assert.DoesNotContain("=", result);
        Assert.DoesNotContain("(", result);
    }

    [Fact]
    public void ToPascalCase_LeadingDigitBecomesLegal()
    {
        string result = DialectMapper.ToPascalCase("123abc");
        Assert.True(IdentifierGuard.IsValidIdentifier(result));
        Assert.StartsWith("_", result);
    }

    [Fact]
    public void ToPascalCase_AllPunctuationReturnsUnderscore()
    {
        Assert.Equal("_", DialectMapper.ToPascalCase("!@#$%"));
    }
}
