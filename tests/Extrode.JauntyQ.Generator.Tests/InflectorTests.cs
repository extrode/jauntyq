using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class InflectorTests
{
    [Theory]
    [InlineData("Shippers", "Shipper")]
    [InlineData("Products", "Product")]
    [InlineData("Employees", "Employee")]
    [InlineData("Categories", "Category")]
    [InlineData("Territories", "Territory")]
    [InlineData("OrderDetails", "OrderDetail")]
    [InlineData("Addresses", "Address")]
    [InlineData("Boxes", "Box")]
    public void Singularize_EnglishPlurals(string plural, string singular)
        => Assert.Equal(singular, Inflector.Singularize(plural));

    [Theory]
    [InlineData("Region")]   // already singular
    [InlineData("Status")]   // -us is not plural-s
    [InlineData("Analysis")] // -is is not plural-s
    [InlineData("Address")]  // -ss is not plural-s
    public void Singularize_LeavesNonPluralsAlone(string name)
        => Assert.Equal(name, Inflector.Singularize(name));

    [Fact]
    public void RowTypeName_UsesSingular_WhenDistinct()
        => Assert.Equal("Shipper", Inflector.RowTypeName("Shippers"));

    [Fact]
    public void RowTypeName_FallsBackToRowSuffix_OnCollision()
    {
        Assert.Equal("RegionRow", Inflector.RowTypeName("Region"));
        Assert.Equal("StatusRow", Inflector.RowTypeName("Status"));
    }
}
