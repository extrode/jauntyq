using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class NormalizerTests
{
    [Theory]
    [InlineData((short)42)]
    [InlineData(42)]
    [InlineData(42L)]
    [InlineData((byte)42)]
    public void EveryIntegerWidthNormalizesToTheSameValue(object value)
    {
        Assert.Equal(42L, RowNormalizer.Normalize(value));
    }

    [Fact]
    public void AnUnsignedLongWithinRangeBecomesTheSameLong()
    {
        Assert.Equal(42L, RowNormalizer.Normalize(42UL));
    }

    [Fact]
    public void AnUnsignedLongPastLongMaxIsKeptAsText()
    {
        Assert.Equal("18446744073709551615", RowNormalizer.Normalize(ulong.MaxValue));
    }

    [Theory]
    [InlineData(true, 1L)]
    [InlineData(false, 0L)]
    public void BooleansBecomeTheIntegerTheOtherEnginesReturn(bool value, long expected)
    {
        Assert.Equal(expected, RowNormalizer.Normalize(value));
    }

    [Fact]
    public void ABooleanAndTheIntegerItStandsForAgree()
    {
        Assert.Equal(RowNormalizer.Normalize(1), RowNormalizer.Normalize(true));
    }

    [Fact]
    public void TrailingZerosOnADecimalAreScaleNotValue()
    {
        Assert.Equal(RowNormalizer.Normalize(1.1m), RowNormalizer.Normalize(1.10m));
    }

    [Fact]
    public void TrailingSpacesFromAFixedWidthColumnAreTrimmed()
    {
        Assert.Equal("bob", RowNormalizer.Normalize("bob   "));
    }

    [Fact]
    public void LeadingSpacesAreData()
    {
        Assert.Equal("   bob", RowNormalizer.Normalize("   bob"));
    }

    [Fact]
    public void DbNullAndNullBothNormalizeToNull()
    {
        Assert.Null(RowNormalizer.Normalize(null));
        Assert.Null(RowNormalizer.Normalize(DBNull.Value));
    }

    [Fact]
    public void TheSameInstantWithDifferentKindsAgrees()
    {
        var utc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        Assert.Equal(RowNormalizer.Normalize(utc), RowNormalizer.Normalize(unspecified));
    }

    [Fact]
    public void SubSecondPrecisionIsBelowTheComparedResolution()
    {
        var whole = new DateTime(2026, 1, 2, 3, 4, 5);
        var fractional = whole.AddMilliseconds(400);

        Assert.Equal(RowNormalizer.Normalize(whole), RowNormalizer.Normalize(fractional));
    }

    // The rules above erase differences. These pin what they must never erase:
    // R4 says a differing value is a divergence, and a normalizer that widened
    // into any of these pairs would report agreement that is not there.

    [Fact]
    public void TwoDifferentIntegersStayDifferent()
    {
        Assert.NotEqual(RowNormalizer.Normalize(42), RowNormalizer.Normalize(43));
    }

    [Fact]
    public void TwoDifferentDecimalsStayDifferent()
    {
        Assert.NotEqual(RowNormalizer.Normalize(1.10m), RowNormalizer.Normalize(1.2m));
    }

    [Fact]
    public void AStringDifferingBeforeItsTrailingSpaceStaysDifferent()
    {
        Assert.NotEqual(RowNormalizer.Normalize("a "), RowNormalizer.Normalize("b "));
    }

    [Fact]
    public void ANullAndAZeroStayDifferent()
    {
        Assert.NotEqual(RowNormalizer.Normalize(0), RowNormalizer.Normalize(null));
    }

    [Fact]
    public void ANullAndAnEmptyStringStayDifferent()
    {
        Assert.NotEqual(RowNormalizer.Normalize(""), RowNormalizer.Normalize(null));
    }

    [Fact]
    public void TwoDifferentSecondsStayDifferent()
    {
        var one = new DateTime(2026, 1, 2, 3, 4, 5);

        Assert.NotEqual(RowNormalizer.Normalize(one), RowNormalizer.Normalize(one.AddSeconds(1)));
    }

    [Fact]
    public void RenderRowIsColumnOrderIndependent()
    {
        var forward = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 };
        var reversed = new Dictionary<string, object?> { ["b"] = 2, ["a"] = 1 };

        Assert.Equal(RowNormalizer.RenderRow(forward), RowNormalizer.RenderRow(reversed));
    }

    [Fact]
    public void RenderRowQuotesStringsSoAnEmptyStringIsVisible()
    {
        var row = new Dictionary<string, object?> { ["bio"] = "", ["image"] = null };

        Assert.Equal("{bio='', image=null}", RowNormalizer.RenderRow(row));
    }
}
