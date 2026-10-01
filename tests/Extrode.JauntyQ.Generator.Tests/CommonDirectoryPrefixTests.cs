using System.Collections.Immutable;
using Xunit;

namespace Extrode.JauntyQ.Generator.Tests;

public class CommonDirectoryPrefixTests
{
    [Theory]
    [InlineData("db/", "db/GetAll.sql")]
    [InlineData("", "GetAll.sql")]
    [InlineData("db/", "GetAll.sql", "db/Products/GetAll.sql")]
    [InlineData("db/x/", "db/x/A/a.sql", "db/x/B/b.sql")]
    [InlineData("DB/", "DB/A/a.sql", "db/B/b.sql")]
    [InlineData("", "aa/x/E/F.sql", "ab/x/E/G.sql")]
    [InlineData("db/", "db/A/x.sql", "db/sub/B/y.sql")]
    [InlineData("db/", "db/sub/B/y.sql", "db/A/x.sql")]
    [InlineData("db/", "db/sub/B/y.sql", "db/subway/A/x.sql")]
    public void ComputeCommonDirectoryPrefix(string expected, params string[] paths)
        => Assert.Equal(expected, JauntyQGenerator.ComputeCommonDirectoryPrefix(ImmutableArray.Create(paths)));
}
