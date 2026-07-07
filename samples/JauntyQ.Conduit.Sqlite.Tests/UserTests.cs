using Microsoft.Data.Sqlite;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.Sqlite.Tests;

/// <summary>
/// Register/login/get/update over the `users` table. Login is a plain
/// SHA-256 hash compare (no JWT/session machinery - out of scope for this
/// data-layer smoke test, see the test log Part 3 kickoff).
/// Seeded users: jane=1, bob=2, carol=3, dave=4, all password "Password123!".
/// </summary>
public class UserTests : IClassFixture<ConduitSqliteFixture>
{
    private readonly ConduitSqliteFixture _fx;
    private readonly UserRepository _repo;

    public UserTests(ConduitSqliteFixture fx)
    {
        _fx = fx;
        _repo = new UserRepository(fx.Db);
    }

    [Fact]
    public void Register_CreatesUser()
    {
        int id = _repo.Register("erin", "erin@example.com", "Sup3rSecret!", bio: "Erin's bio");

        var user = _repo.GetById(id);
        Assert.NotNull(user);
        Assert.Equal("erin", user!.Username);
        Assert.Equal("erin@example.com", user.Email);
        Assert.Equal("Erin's bio", user.Bio);
    }

    [Fact]
    public void Register_DuplicateUsername_Throws()
    {
        Assert.Throws<SqliteException>(() =>
            _repo.Register("jane", "jane2@example.com", "Sup3rSecret!"));
    }

    [Fact]
    public void Register_DuplicateEmail_Throws()
    {
        Assert.Throws<SqliteException>(() =>
            _repo.Register("jane2", "jane@example.com", "Sup3rSecret!"));
    }

    [Fact]
    public void Login_CorrectPassword_ReturnsUser()
    {
        var user = _repo.Login("jane@example.com", "Password123!");

        Assert.NotNull(user);
        Assert.Equal("jane", user!.Username);
    }

    [Fact]
    public void Login_IncorrectPassword_ReturnsNull()
    {
        var user = _repo.Login("jane@example.com", "WrongPassword");

        Assert.Null(user);
    }

    [Fact]
    public void Login_UnknownEmail_ReturnsNull()
    {
        var user = _repo.Login("nobody@example.com", "Password123!");

        Assert.Null(user);
    }

    [Fact]
    public void GetByUsername_ReturnsSeededUser()
    {
        var user = _repo.GetByUsername("bob");

        Assert.NotNull(user);
        Assert.Equal("bob@example.com", user!.Email);
    }

    [Fact]
    public void UpdateProfile_UpdatesFields()
    {
        int id = _repo.Register("frank", "frank@example.com", "Sup3rSecret!", bio: "old bio");

        _repo.UpdateProfile(id, "frank", "frank@example.com", "new bio", "https://example.com/frank.png");

        var user = _repo.GetById(id);
        Assert.NotNull(user);
        Assert.Equal("new bio", user!.Bio);
        Assert.Equal("https://example.com/frank.png", user.Image);
    }
}
