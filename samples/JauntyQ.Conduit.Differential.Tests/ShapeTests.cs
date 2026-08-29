using Xunit;

namespace JauntyQ.Conduit.Differential.Tests;

[Trait("Category", "Differential")]
public class ShapeTests
{
    [Theory]
    [InlineData(null, Shape.Null)]
    [InlineData(true, Shape.Boolean)]
    [InlineData(42, Shape.Integer)]
    [InlineData(42L, Shape.Integer)]
    [InlineData(4.2d, Shape.Fractional)]
    [InlineData("a", Shape.Text)]
    public void ValuesClassifyIntoTheirShape(object? value, Shape expected)
    {
        Assert.Equal(expected, ValueShape.Classify(value));
    }

    [Fact]
    public void DbNullClassifiesAsNull()
    {
        Assert.Equal(Shape.Null, ValueShape.Classify(DBNull.Value));
    }

    [Fact]
    public void ADecimalClassifiesAsFractional()
    {
        Assert.Equal(Shape.Fractional, ValueShape.Classify(2.5m));
    }

    [Fact]
    public void ATimestampClassifiesAsTemporal()
    {
        Assert.Equal(Shape.Temporal, ValueShape.Classify(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void BytesClassifyAsBinary()
    {
        Assert.Equal(Shape.Binary, ValueShape.Classify(new byte[] { 1, 2 }));
    }

    // The whole reason 018 does not reuse 017's normalizer: these two must stay
    // apart or the boolean-handling category is erased before it is measured.
    [Fact]
    public void ABooleanAndTheIntegerItStandsForStayDifferent()
    {
        Assert.NotEqual(ValueShape.Render(true), ValueShape.Render(1L));
    }

    [Fact]
    public void AnIntegerAndTheFractionalOfEqualValueStayDifferent()
    {
        Assert.NotEqual(ValueShape.Render(2L), ValueShape.Render(2.0m));
    }

    [Fact]
    public void IntAndLongOfTheSameValueAgree()
    {
        Assert.Equal(ValueShape.Render(2L), ValueShape.Render(2));
    }

    [Fact]
    public void TwoDifferentIntegersStayDifferent()
    {
        Assert.NotEqual(ValueShape.Render(2L), ValueShape.Render(3L));
    }

    [Fact]
    public void NullAndAnEmptyStringStayDifferent()
    {
        Assert.NotEqual(ValueShape.Render(null), ValueShape.Render(""));
    }

    [Fact]
    public void TextRendersQuotedSoAnEmptyStringIsVisible()
    {
        Assert.Equal("text:''", ValueShape.Render(""));
    }

    [Fact]
    public void RowsRenderByOrdinalNotByName()
    {
        Assert.Equal("[integer:1, text:'a']", ValueShape.RenderRow([1, "a"]));
    }

    [Fact]
    public void AnOrderedResultKeepsRowOrder()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows = [new object?[] { 2 }, new object?[] { 1 }];

        Assert.NotEqual(
            ValueShape.RenderResult(rows, ordered: true),
            ValueShape.RenderResult([new object?[] { 1 }, new object?[] { 2 }], ordered: true));
    }

    [Fact]
    public void AnUnorderedResultDoesNotSplitOnRowOrder()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows = [new object?[] { 2 }, new object?[] { 1 }];

        Assert.Equal(
            ValueShape.RenderResult(rows, ordered: false),
            ValueShape.RenderResult([new object?[] { 1 }, new object?[] { 2 }], ordered: false));
    }

    [Fact]
    public void NoRowsRendersDistinctlyFromARowHoldingNull()
    {
        Assert.NotEqual(
            ValueShape.RenderResult([], ordered: false),
            ValueShape.RenderResult([new object?[] { null }], ordered: false));
    }
}
