using Xunit;

namespace JauntyQ.Sqlite.Tests;

/// <summary>
/// AUD-R50-01 end-to-end regression, live against in-process SQLite: a SQL
/// string literal that spans lines (db/tables/Categories/GetLabeledName.sql)
/// must round-trip byte-for-byte. Before the fix, the readable-codegen
/// continuation-line indentation (IndentSqlContinuationLines) injected its
/// alignment spaces INSIDE the literal in the emitted verbatim CommandText,
/// so the engine received — and returned — a padded literal.
/// </summary>
[Trait("Category", "AuditRegression")]
public class MultiLineLiteralTests : IClassFixture<SqliteFixture>
{
    private readonly SqliteFixture _fx;
    public MultiLineLiteralTests(SqliteFixture fx) => _fx = fx;

    [Fact]
    public void MultiLineLiteral_RoundTripsExactly()
    {
        var row = _fx.Db.Categories.GetLabeledName(1);
        Assert.NotNull(row);
        Assert.NotNull(row!.Labeled);
        // Normalize CRLF (in case the .sql file's line endings differ per
        // platform) but preserve everything else — the injected padding this
        // guards against is spaces, which normalization does not touch.
        string labeled = row.Labeled!.Replace("\r\n", "\n");
        Assert.Equal("Beverages[line1\nline2]", labeled);
    }
}
