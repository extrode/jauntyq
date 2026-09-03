using Extrode.JauntyQ.Generated;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Extrode.JauntyQ.Northwind.Tests;

/// <summary>
/// Live coverage for Tier 2 value safety against Northwind:
/// Shippers.CompanyName is nvarchar(40), so the generated write path must
/// throw client-side (no server round-trip) for a 41-char value, and the
/// happy path must be unaffected by the new parameter sizing.
/// </summary>
[Collection("Northwind")]
public class Tier2LiveTests
{
    private readonly NorthwindFixture _fixture;
    public Tier2LiveTests(NorthwindFixture fixture) => _fixture = fixture;

    private static JauntyDb FreshDb() => new(new SqlConnection(NorthwindFixture.ConnectionString));

    [SkippableFact]
    public void OversizeWriteValue_ThrowsClientSide_WithColumnAndLimit()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();
        string tooLong = new string('X', 41);

        var ex = Assert.Throws<ArgumentException>(() => db.Shippers.Insert(tooLong, "(555) 000-0000"));

        Assert.Contains("41 characters", ex.Message);
        Assert.Contains("Shippers.CompanyName", ex.Message);
        Assert.Contains("max length (40)", ex.Message);
        Assert.Equal("CompanyName", ex.ParamName);
    }

    [SkippableFact]
    public void OversizeWriteValue_PocoUpdate_AlsoThrows()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();
        var shipper = db.Shippers.GetById(1);
        Assert.NotNull(shipper);

        shipper.CompanyName = new string('Y', 41);
        var ex = Assert.Throws<ArgumentException>(() => db.Shippers.Update(shipper));
        Assert.Contains("Shippers.CompanyName", ex.Message);
    }

    [SkippableFact]
    public void ExactLimitValue_WritesAndReadsBack()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();
        string exact40 = new string('Z', 40);

        using var tx = db.BeginTransaction();
        int newId = db.Shippers.Insert(exact40, "(555) 111-2222");
        var inserted = db.Shippers.GetById(newId);
        Assert.NotNull(inserted);
        Assert.Equal(exact40, inserted.CompanyName);
        // dispose without commit rolls back
    }

    [SkippableFact]
    public void OversizeComparisonValue_MatchesNothing_NoThrow()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        var db = FreshDb();

        // Read path must never truncate (a truncated key could match the
        // wrong row) and must never throw: oversize keys simply find nothing.
        var result = db.Customers.GetById(new string('Q', 50));
        Assert.Null(result);
    }
}
