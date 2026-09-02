using JauntyQ.Schema;
using Xunit;

namespace JauntyQ.Schema.Extraction.Tests;

/// <summary>
/// Spec 013 / T4: folding a database enum member's wire value into a C#
/// identifier. Every case here is a value a real database accepts.
/// </summary>
public class EnumMemberNamingTests
{
    [Theory]
    // Ordinary
    [InlineData("pending", "Pending")]
    [InlineData("shipped", "Shipped")]
    [InlineData("PENDING", "PENDING")]
    // Separators of every kind
    [InlineData("in progress", "InProgress")]
    [InlineData("in-progress", "InProgress")]
    [InlineData("in_progress", "InProgress")]
    [InlineData("a,b", "AB")]
    [InlineData("it's", "ItS")]
    // Leading digit is illegal to start an identifier
    [InlineData("2xl", "_2xl")]
    [InlineData("3", "_3")]
    // Nothing usable at all
    [InlineData("", "_")]
    [InlineData("-", "_")]
    [InlineData("---", "_")]
    [InlineData("!!!", "_")]
    public void FoldsToALegalIdentifier(string value, string expected)
    {
        Assert.Equal(expected, EnumMemberNaming.Fold(value));
    }

    [Fact]
    public void NullFoldsRatherThanThrowing()
    {
        Assert.Equal("_", EnumMemberNaming.Fold(null));
    }

    [Fact]
    public void IsDeterministic()
    {
        // FR-010: the same input must always produce the same identifier,
        // because the snapshot persists one spelling and the emitter resolves
        // the other.
        foreach (string v in new[] { "in progress", "", "2xl", "it's", "a,b" })
            Assert.Equal(EnumMemberNaming.Fold(v), EnumMemberNaming.Fold(v));
    }

    [Fact]
    public void HostileValueCollapsesInsteadOfInjectingCSharp()
    {
        // A member value is arbitrary database content and is emitted as an
        // identifier, so this is a code-injection boundary, not a formatting
        // nicety.
        string folded = EnumMemberNaming.Fold("X { get; } static int Z = Evil(); //");

        Assert.Equal("XGetStaticIntZEvil", folded);
        foreach (char c in folded)
            Assert.True(char.IsLetterOrDigit(c) || c == '_', $"'{c}' is not identifier-safe");
    }

    [Fact]
    public void CollisionsAreProducedNotHidden()
    {
        // The folder must NOT disambiguate: two members that collide have to
        // reach the emitter as a collision so JNT2016 can report both. Silent
        // renaming is the failure mode this design rejects.
        Assert.Equal(EnumMemberNaming.Fold("in progress"), EnumMemberNaming.Fold("in-progress"));
        Assert.Equal(EnumMemberNaming.Fold(""), EnumMemberNaming.Fold("-"));
    }
}
