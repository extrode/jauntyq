using Microsoft.CodeAnalysis;
using JauntyQ.Generator;
using Xunit;

namespace JauntyQ.Generator.Tests;

/// <summary>Public-API coverage for <see cref="ValidationError"/>.</summary>
public class ValidationErrorTests
{
    [Fact]
    public void ToString_IsCodeColonMessage()
    {
        var err = new ValidationError(JauntyDiagnostics.JNT2001, "Table 'x' does not exist");
        Assert.Equal("JNT2001: Table 'x' does not exist", err.ToString());
    }

    [Fact]
    public void Severity_FollowsDescriptorDefault()
    {
        var error = new ValidationError(JauntyDiagnostics.JNT2001, "m");     // Error descriptor
        var warning = new ValidationError(JauntyDiagnostics.JNT8001, "m");   // Warning descriptor

        Assert.Equal(ValidationSeverity.Error, error.Severity);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Equal("JNT2001", error.Code);
        Assert.Same(JauntyDiagnostics.JNT2001, error.Descriptor);
    }
}
