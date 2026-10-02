using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class DiagnosticInfoTests
{
    [Fact]
    public void ForValidation_KeepsAMessageWithBracesUnformatted()
    {
        var error = new ValidationError(JauntyDiagnostics.JNT8004, "column {0} and {x} are not placeholders");

        var diagnostic = DiagnosticInfo.ForValidation(error).ToDiagnostic();

        Assert.Equal("column {0} and {x} are not placeholders", diagnostic.GetMessage());
        Assert.Equal(JauntyDiagnostics.JNT8004.Category, diagnostic.Descriptor.Category);
        Assert.Equal(JauntyDiagnostics.JNT8004.Title.ToString(), diagnostic.Descriptor.Title.ToString());
    }

    [Fact]
    public void ForValidation_KeepsAWellFormedPlaceholderUnformatted()
    {
        var error = new ValidationError(JauntyDiagnostics.JNT8004, "column {0} is not a placeholder");

        Assert.Equal("column {0} is not a placeholder", DiagnosticInfo.ForValidation(error).ToDiagnostic().GetMessage());
    }
}
