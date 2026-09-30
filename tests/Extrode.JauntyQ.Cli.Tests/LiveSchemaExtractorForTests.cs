using Extrode.JauntyQ.Schema.Extraction;
using Xunit;

namespace Extrode.JauntyQ.Cli.Tests;

public class LiveSchemaExtractorForTests
{
    [Theory]
    [InlineData("postgres", typeof(PostgresExtractor))]
    [InlineData("sqlserver", typeof(SqlServerExtractor))]
    [InlineData("mysql", typeof(MySqlExtractor))]
    [InlineData("sqlite", typeof(SqliteExtractor))]
    public void ExtractorFor_MapsEachDialectToItsExtractor(string dialect, Type expected)
    {
        Assert.IsType(expected, LiveSchema.ExtractorFor(dialect));
    }

    [Fact]
    public void ExtractorFor_UnwiredDialect_ThrowsNamingTheDialect()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => LiveSchema.ExtractorFor("oracle"));

        Assert.Equal("dialect", ex.ParamName);
        Assert.Equal("oracle", ex.ActualValue);
        Assert.StartsWith("No extractor is wired for this dialect.", ex.Message);
    }
}
