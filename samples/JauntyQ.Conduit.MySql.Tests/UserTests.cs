using MySqlConnector;
using JauntyQ.Conduit.MySql.Tests.Repositories;
using Xunit;

namespace JauntyQ.Conduit.MySql.Tests;

/// <summary>
/// Register/login/get/update over the `users` table. Login is a plain
/// SHA-256 hash compare (no JWT/session machinery - out of scope for this
/// data-layer smoke test, see the test log Part 3 kickoff).
/// Seeded users: jane=1, bob=2, carol=3, dave=4, all password "Password123!".
/// </summary>
public class UserTests : IClassFixture<ConduitMySqlFixture>
{
    private readonly ConduitMySqlFixture _fx;
    private readonly UserRepository _repo;

    public UserTests(ConduitMySqlFixture fx)
    {
        _fx = fx;
        _repo = new UserRepository(fx.Db);
    }

    [SkippableFact]
    public void Register_CreatesUser()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int id = _repo.Register("erin", "erin@example.com", "Sup3rSecret!", bio: "Erin's bio");

        var user = _repo.GetById(id);
        Assert.NotNull(user);
        Assert.Equal("erin", user!.Username);
        Assert.Equal("erin@example.com", user.Email);
        Assert.Equal("Erin's bio", user.Bio);
    }

    [SkippableFact]
    public void Register_DuplicateUsername_Throws()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Throws<MySqlException>(() =>
            _repo.Register("jane", "jane2@example.com", "Sup3rSecret!"));
    }

    [SkippableFact]
    public void Register_DuplicateEmail_Throws()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        Assert.Throws<MySqlException>(() =>
            _repo.Register("jane2", "jane@example.com", "Sup3rSecret!"));
    }

    [SkippableFact]
    public void Login_CorrectPassword_ReturnsUser()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var user = _repo.Login("jane@example.com", "Password123!");

        Assert.NotNull(user);
        Assert.Equal("jane", user!.Username);
    }

    [SkippableFact]
    public void Login_IncorrectPassword_ReturnsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var user = _repo.Login("jane@example.com", "WrongPassword");

        Assert.Null(user);
    }

    [SkippableFact]
    public void Login_UnknownEmail_ReturnsNull()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var user = _repo.Login("nobody@example.com", "Password123!");

        Assert.Null(user);
    }

    [SkippableFact]
    public void GetByUsername_ReturnsSeededUser()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        var user = _repo.GetByUsername("bob");

        Assert.NotNull(user);
        Assert.Equal("bob@example.com", user!.Email);
    }

    [SkippableFact]
    public void UpdateProfile_UpdatesFields()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);

        int id = _repo.Register("frank", "frank@example.com", "Sup3rSecret!", bio: "old bio");

        _repo.UpdateProfile(id, "frank", "frank@example.com", "new bio", "https://example.com/frank.png");

        var user = _repo.GetById(id);
        Assert.NotNull(user);
        Assert.Equal("new bio", user!.Bio);
        Assert.Equal("https://example.com/frank.png", user.Image);
    }
}
