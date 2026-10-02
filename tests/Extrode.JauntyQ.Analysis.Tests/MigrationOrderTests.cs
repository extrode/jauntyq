using System;
using Extrode.JauntyQ.Analysis.Migrations;
using Xunit;

namespace Extrode.JauntyQ.Analysis.Tests;

public class MigrationOrderTests
{
    [Theory]
    [InlineData("V1", "V2", -1)]
    [InlineData("V2", "V10", -1)]
    [InlineData("V10", "V9", 1)]
    [InlineData("V07", "V007", -1)]
    [InlineData("V007", "V07", 1)]
    [InlineData("V7", "V7", 0)]
    [InlineData("ab", "ab", 0)]
    [InlineData("a", "b", -1)]
    [InlineData("b", "a", 1)]
    [InlineData("a", "1", 1)]
    [InlineData("1", "a", -1)]
    [InlineData("V1", "V1x", -1)]
    [InlineData("V1x", "V1", 1)]
    [InlineData("x", "xy", -1)]
    [InlineData("V1a", "V1b", -1)]
    [InlineData("V02a", "V2b", 1)]
    public void Compare_OrdersDigitRunsByValue(string a, string b, int sign)
        => Assert.Equal(sign, Math.Sign(MigrationOrder.Compare(a, b)));
}
