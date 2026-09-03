using Extrode.JauntyQ.Generated;
using Xunit;

namespace Extrode.JauntyQ.Chinook.Sqlite.Tests;

/// <summary>
/// Curated queries against the canonical Chinook 1.4.5 dataset (vendored
/// verbatim, full seed). Expected values were captured by running each
/// query's literal SQL against the same seeded database via Python's sqlite3
/// before being hardcoded here, so every assert reflects upstream's fixed
/// data, not values invented for the test.
/// </summary>
public class ChinookQueriesTests : IClassFixture<ChinookSqliteFixture>
{
    private readonly ChinookSqliteFixture _fx;
    public ChinookQueriesTests(ChinookSqliteFixture fx) => _fx = fx;

    [Fact]
    public void TopArtistsByAlbumCount_IronMaidenLeads()
    {
        var rows = _fx.Db.Artist.GetTopByAlbumCount();
        Assert.Equal(5, rows.Count);
        Assert.Equal((90, "Iron Maiden", 21), (rows[0].ArtistId, rows[0].Name, rows[0].AlbumCount));
        Assert.Equal((22, "Led Zeppelin", 14), (rows[1].ArtistId, rows[1].Name, rows[1].AlbumCount));
        Assert.Equal((150, "U2", 10), (rows[4].ArtistId, rows[4].Name, rows[4].AlbumCount));
    }

    [Fact]
    public void AlbumsByArtist_AcDcHasTwo()
    {
        var rows = _fx.Db.Album.GetByArtist(1);
        Assert.Equal(2, rows.Count);
        Assert.Equal("For Those About To Rock We Salute You", rows[0].Title);
        Assert.Equal("Let There Be Rock", rows[1].Title);
    }

    [Fact]
    public void TracksByAlbum_Album1Has10Tracks()
    {
        var rows = _fx.Db.Track.GetByAlbum(new int?[] { 1 });
        Assert.Equal(10, rows.Count);
        Assert.Equal("For Those About To Rock (We Salute You)", rows[0].Name);
        Assert.Equal("Rock", rows[0].GenreName);
        Assert.Equal(0.99m, rows[0].UnitPrice);
        Assert.All(rows, r => Assert.Equal("MPEG audio file", r.MediaTypeName));
    }

    [Fact]
    public void TopSellingTracks_ReturnsTop10()
    {
        var rows = _fx.Db.Track.GetTopSelling();
        Assert.Equal(10, rows.Count);
        Assert.Equal((2, "Balls to the Wall", 2), (rows[0].TrackId, rows[0].Name, rows[0].UnitsSold));
        Assert.All(rows, r => Assert.Equal(2, r.UnitsSold));
    }

    [Fact]
    public void DirectReports_OfNancyEdwards_AreThree()
    {
        var rows = _fx.Db.Employee.GetDirectReports(new int?[] { 2 });
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 3, 4, 5 }, rows.Select(r => r.EmployeeId).ToArray());
    }

    [Fact]
    public void EmployeesWithManager_SelfJoinLeavesGeneralManagerUnmatched()
    {
        var rows = _fx.Db.Employee.GetWithManager();
        Assert.Equal(8, rows.Count);
        var adams = Assert.Single(rows, r => r.LastName == "Adams");
        Assert.Null(adams.ManagerLastName);
        var peacock = Assert.Single(rows, r => r.LastName == "Peacock");
        Assert.Equal("Edwards", peacock.ManagerLastName);
    }

    [Fact]
    public void CustomersBySupportRep_Rep3Has21()
    {
        var rows = _fx.Db.Customer.GetBySupportRep(new int?[] { 3 });
        Assert.Equal(21, rows.Count);
        Assert.All(rows, r => Assert.Equal(3, r.SupportRepId));
    }

    [Fact]
    public void RevenueByCountry_UsaLeads()
    {
        var rows = _fx.Db.Invoice.GetRevenueByCountry();
        Assert.Equal(10, rows.Count);
        Assert.Equal(("USA", 523.06m, 91), (rows[0].BillingCountry, rows[0].Revenue, rows[0].InvoiceCount));
        Assert.Equal(("Canada", 303.96m, 56), (rows[1].BillingCountry, rows[1].Revenue, rows[1].InvoiceCount));
    }

    [Fact]
    public void InvoicesByCustomer_Customer1HasSeven()
    {
        var rows = _fx.Db.Invoice.GetByCustomer(new[] { 1 });
        Assert.Equal(7, rows.Count);
        Assert.Equal((98, 3.98m), (rows[0].InvoiceId, rows[0].Total));
        Assert.Equal((382, 8.91m), (rows[^1].InvoiceId, rows[^1].Total));
    }

    [Fact]
    public void InvoiceLines_ForInvoice1_AreTwo()
    {
        var rows = _fx.Db.InvoiceLine.GetByInvoice(new[] { 1 });
        Assert.Equal(2, rows.Count);
        Assert.Equal((2, "Balls to the Wall", 1), (rows[0].TrackId, rows[0].TrackName, rows[0].Quantity));
        Assert.Equal(4, rows[1].TrackId);
    }

    [Fact]
    public void GenreRevenue_RockLeads()
    {
        var rows = _fx.Db.Genre.GetRevenue();
        Assert.Equal(5, rows.Count);
        Assert.Equal(("Rock", 826.65m), (rows[0].Name, rows[0].Revenue));
        Assert.Equal(("Latin", 382.14m), (rows[1].Name, rows[1].Revenue));
        Assert.Equal(("Metal", 261.36m), (rows[2].Name, rows[2].Revenue));
    }

    [Fact]
    public void TracksForPlaylist_Playlist1Has3290()
    {
        var rows = _fx.Db.PlaylistTrack.GetTracksForPlaylist(1);
        Assert.Equal(3290, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.PlaylistId));
    }

    [Fact]
    public void PlaylistTrackCounts_MusicPlaylistsLead()
    {
        var rows = _fx.Db.Playlist.GetTrackCounts();
        Assert.Equal(18, rows.Count);
        Assert.Equal((1, "Music", 3290), (rows[0].PlaylistId, rows[0].Name, rows[0].TrackCount));
        Assert.Equal((8, "Music", 3290), (rows[1].PlaylistId, rows[1].Name, rows[1].TrackCount));
        Assert.Equal(213, rows[3].TrackCount);
    }
}
