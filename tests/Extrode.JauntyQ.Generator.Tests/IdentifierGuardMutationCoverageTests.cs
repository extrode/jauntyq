using Extrode.JauntyQ.Generator;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class IdentifierGuardMutationCoverageTests
{
    [Fact]
    public void ToStringLiteral_Null_IsEmpty()
    {
        Assert.Equal("", IdentifierGuard.ToStringLiteral(null));
    }

    [Fact]
    public void ToStringLiteral_EscapesTabAndNul()
    {
        Assert.Equal("a\\tb\\0c", IdentifierGuard.ToStringLiteral("a\tb\0c"));
    }

    [Theory]
    [InlineData("a9")]
    [InlineData("_9")]
    public void IsValidIdentifier_AcceptsTheDigitNine(string name)
    {
        Assert.True(IdentifierGuard.IsValidIdentifier(name));
    }
}
