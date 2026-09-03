using Extrode.JauntyQ.Generated;
using Npgsql;
using Xunit;

namespace Extrode.JauntyQ.Pagila.Postgres.Tests;

/// <summary>
/// Curated queries against the canonical, untrimmed Pagila dataset (vendored
/// verbatim from devrimgunduz/pagila; PostgreSQL 18 + pgvector). Expected
/// values were captured by running each query's literal SQL against the same
/// seeded database via psql before being hardcoded here — the seed is checked
/// into upstream, so the data is fixed. The payment queries deliberately hit
/// the PARTITIONED parent (55 monthly partitions, 2022-01..2026-07); the two
/// raw-SQL tests at the bottom cover the tsvector full-text column and the
/// rental_by_category materialized view, which generated queries cannot
/// express.
/// </summary>
public class PagilaQueriesTests : IClassFixture<PagilaPostgresFixture>
{
    private readonly PagilaPostgresFixture _fx;
    public PagilaQueriesTests(PagilaPostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public void RevenueByMonth_Staff1_SpansAll55Partitions()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Payment.GetRevenueByMonthForStaff(1);
        Assert.Equal(55, rows.Count);
        Assert.Equal((2022, 1, 1547.48m), (rows[0].Year, rows[0].Month, rows[0].Revenue));
        Assert.Equal((2026, 7, 1220.85m), (rows[^1].Year, rows[^1].Month, rows[^1].Revenue));
    }

    [SkippableFact]
    public void FilmsByRating_R_UsesGeneratedEnum()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetByRating(MpaaRating.R);
        Assert.Equal(195, rows.Count);
        Assert.Equal((8, "AIRPORT POLLOCK"), (rows[0].FilmId, rows[0].Title));
        Assert.All(rows, r => Assert.Equal(MpaaRating.R, r.Rating));
    }

    [SkippableFact]
    public void FilmsByActor_Returns42Films()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetByActor(107);
        Assert.Equal(42, rows.Count);
        Assert.Equal("BED HIGHBALL", rows[0].Title);
    }

    [SkippableFact]
    public void TopCustomersBySpend_KarlSealLeads()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Customer.GetTopBySpend();
        Assert.Equal(5, rows.Count);
        Assert.Equal((526, "SEAL", 358.12m), (rows[0].CustomerId, rows[0].LastName, rows[0].TotalSpend));
        Assert.Equal((295, "BATES", 322.12m), (rows[4].CustomerId, rows[4].LastName, rows[4].TotalSpend));
    }

    [SkippableFact]
    public void CustomerByUuid_FindsMary()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        // Customer 1's uuid comes from the vendored COPY data (the uuidv7()
        // column default only applies to new inserts), so it is fixed.
        var rows = _fx.Db.Customer.GetByUuid(Guid.Parse("019faa23-3a86-7f23-86c4-ef6274588090"));
        var mary = Assert.Single(rows);
        Assert.Equal((1, "MARY"), (mary.CustomerId, mary.FirstName));
    }

    [SkippableFact]
    public void CategoryRevenue_ActionLeads()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Category.GetRevenue();
        Assert.Equal(16, rows.Count);
        Assert.Equal(("Action", 26505.44m), (rows[0].Name, rows[0].Revenue));
        Assert.Equal(("Animation", 26460.31m), (rows[1].Name, rows[1].Revenue));
        Assert.Equal(("Children", 26399.88m), (rows[2].Name, rows[2].Revenue));
    }

    [SkippableFact]
    public void RentalHistory_Customer459_Returns77Rentals()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Rental.GetHistoryByCustomer(new[] { 459 });
        Assert.Equal(77, rows.Count);
        Assert.Equal("FREAKY POCUS", rows[0].Title);
        Assert.All(rows, r => Assert.Equal(459, r.CustomerId));
    }

    [SkippableFact]
    public void MostProlificActors_GinaDegeneresLeads()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Actor.GetMostProlific();
        Assert.Equal(5, rows.Count);
        Assert.Equal((107, "DEGENERES", 42), (rows[0].ActorId, rows[0].LastName, rows[0].FilmCount));
        Assert.Equal((23, "KILMER", 37), (rows[4].ActorId, rows[4].LastName, rows[4].FilmCount));
    }

    [SkippableFact]
    public void FulltextTsvectorSearch_RawPassthrough()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        // tsvector cannot be expressed through generated queries (JNT2007
        // pins the column as unmapped); raw SQL over the same connection is
        // the supported escape hatch and proves the canonical fulltext
        // column + its trigger-maintained content actually work.
        using var cmd = new NpgsqlCommand(
            "select count(*) from film where fulltext @@ to_tsquery('english', 'drama & dog')",
            _fx.Connection);
        Assert.Equal(8L, (long)cmd.ExecuteScalar()!);
    }

    [SkippableFact]
    public void RentalByCategoryMaterializedView_RawPassthrough()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        using var cmd = new NpgsqlCommand(
            "select category, round(total_sales, 2) from rental_by_category order by total_sales desc limit 1",
            _fx.Connection);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Action", reader.GetString(0));
        Assert.Equal(26505.44m, reader.GetDecimal(1));
    }
}
