using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Sakila.MySql.Tests;

/// <summary>
/// The 15 canonical Sakila queries from the handoff notes
/// Part 2, run against real MySQL. Golden values are the same ones already
/// verified against Postgres and SQLite (Extrode.JauntyQ.Sakila.Postgres.Tests /
/// Extrode.JauntyQ.Sakila.Sqlite.Tests) — see the test log.
/// </summary>
public class SakilaQueriesTests : IClassFixture<SakilaMySqlFixture>
{
    private readonly SakilaMySqlFixture _fx;
    public SakilaQueriesTests(SakilaMySqlFixture fx) => _fx = fx;

    [SkippableFact]
    public void RentalHistoryByCustomer_Returns35Rentals()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Rental.GetHistoryByCustomer(381);
        Assert.Equal(35, rows.Count);
        Assert.Equal(169, rows[0].RentalId);
        Assert.Equal("GRIT CLOCKWORK", rows[0].Title);
        Assert.Equal(15747, rows[^1].RentalId);
        Assert.Equal("FORWARD TEMPLE", rows[^1].Title);
    }

    [SkippableFact]
    public void TopRevenueByCategory_Action_ReturnsTop5()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetTopRevenueByCategory(1);
        Assert.Equal(5, rows.Count);
        Assert.Equal(911, rows[0].FilmId);
        Assert.Equal("TRIP NEWTON", rows[0].Title);
        Assert.Equal(14.97m, rows[0].Revenue);
        Assert.Equal(5.97m, rows[4].Revenue);
    }

    [SkippableFact]
    public void FilmsByActor_Returns42Films()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetByActor(107);
        Assert.Equal(42, rows.Count);
        Assert.Contains(rows, f => f.Title == "BED HIGHBALL");
        Assert.Contains(rows, f => f.Title == "WINDOW SIDE");
    }

    [SkippableFact]
    public void StoreRevenueByMonth_Store1_Returns7Months()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Payment.GetStoreRevenueByMonth(1);
        Assert.Equal(7, rows.Count);
        Assert.Equal((2022, 1, 59.87m), (rows[0].Year, rows[0].Month, rows[0].Revenue));
        Assert.Equal((2022, 5, 226.47m), (rows[4].Year, rows[4].Month, rows[4].Revenue));
        Assert.Equal((2022, 6, 182.52m), (rows[5].Year, rows[5].Month, rows[5].Revenue));
    }

    [SkippableFact]
    public void OverdueRentals_Returns10Rows()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Rental.GetOverdue();
        Assert.Equal(10, rows.Count);
        Assert.Contains(rows, r => r.RentalId == 11672 && r.LastName == "SOUTH");
        Assert.Contains(rows, r => r.RentalId == 15875 && r.LastName == "MITCHELL");
    }

    [SkippableFact]
    public void UnrentedFilms_Returns480Films()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetUnrented();
        Assert.Equal(480, rows.Count);
    }

    [SkippableFact]
    public void TopCustomersBySpend_ReturnsTop5()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Customer.GetTopBySpend();
        Assert.Equal(5, rows.Count);
        Assert.Equal((181, "BRADLEY", 174.66m), (rows[0].CustomerId, rows[0].LastName, rows[0].TotalSpend));
        Assert.Equal((241, "LARSON", 122.66m), (rows[4].CustomerId, rows[4].LastName, rows[4].TotalSpend));
    }

    [SkippableFact]
    public void CoActors_OfActor107_Returns136()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Actor.GetCoActors(107);
        Assert.Equal(136, rows.Count);
    }

    [SkippableFact]
    public void CategoryRevenue_Returns16Categories()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Category.GetRevenue();
        Assert.Equal(16, rows.Count);
        Assert.Equal(("Sci-Fi", 292.32m), (rows[0].Name, rows[0].Revenue));
        Assert.Equal(("Action", 105.61m), (rows[^1].Name, rows[^1].Revenue));
    }

    [SkippableFact]
    public void AvgRentalDuration_Returns16Categories()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Category.GetAvgRentalDuration();
        Assert.Equal(16, rows.Count);
        var action = Assert.Single(rows, r => r.Name == "Action");
        Assert.Equal(3.82, Math.Round(action.AvgDays!.Value, 2));
    }

    [SkippableFact]
    public void PaymentsByStore_Store1_Returns19Customers()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Customer.GetPaymentsByStore(1);
        Assert.Equal(19, rows.Count);
        Assert.Equal(("BLAKELY", 97.72m), (rows[0].LastName, rows[0].TotalPaid));
        Assert.Equal(("WOFFORD", 107.73m), (rows[^1].LastName, rows[^1].TotalPaid));
    }

    [SkippableFact]
    public void FilmCountsByLanguage_EnglishHas1000()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Language.GetFilmCounts();
        Assert.Equal(6, rows.Count);
        var english = Assert.Single(rows, r => r.Name.Trim() == "English");
        Assert.Equal(1000, english.FilmCount);
        Assert.All(rows, r => Assert.True(r.Name.Trim() == "English" || r.FilmCount == 0));
    }

    [SkippableFact]
    public void StaffPaymentSummary_Returns2Staff()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Staff.GetPaymentSummary();
        Assert.Equal(2, rows.Count);
        Assert.Equal((382, 1551.18m), (rows[0].PaymentCount, rows[0].TotalAmount));
        Assert.Equal((406, 1668.94m), (rows[1].PaymentCount, rows[1].TotalAmount));
    }

    [SkippableFact]
    public void MostRentedFilms_ReturnsTop10()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Film.GetMostRented();
        Assert.Equal(10, rows.Count);
        Assert.Equal(("GOODFELLAS SALUTE", 6), (rows[0].Title, rows[0].RentalCount));
        Assert.Equal(4, rows[^1].RentalCount);
    }

    [SkippableFact]
    public void CustomersNeverRented_ReturnsEmpty()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var rows = _fx.Db.Customer.GetNeverRented();
        Assert.Empty(rows);
    }
}
