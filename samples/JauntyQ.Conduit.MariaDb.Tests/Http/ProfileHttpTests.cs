using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JauntyQ.Conduit.MariaDb.Tests.Contracts;
using Xunit;

namespace JauntyQ.Conduit.MariaDb.Tests.Http;

public sealed class ProfileHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly ConduitWebAppFixture _fixture;
    private readonly HttpClient _client;

    public ProfileHttpTests(ConduitWebAppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    private static RegisterRequestEnvelope Register(string username, string email) =>
        new(new RegisterRequest(username, email, "testcred0001"));

    private async Task<string> RegisterAndGetToken(string username, string email)
    {
        var response = await _client.PostAsJsonAsync("/api/users", Register(username, email));
        var body = await response.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        return body!.User.Token;
    }

    private HttpRequestMessage WithToken(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        return request;
    }

    [Fact]
    public async Task GetProfile_UnknownUser_Returns404()
    {
        if (!_fixture.Available) return;

        var response = await _client.GetAsync("/api/profiles/nobody");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithoutAuth_ReturnsNotFollowing()
    {
        if (!_fixture.Available) return;

        await RegisterAndGetToken("henry", "henry@example.com");

        var response = await _client.GetAsync("/api/profiles/henry");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProfileResponseEnvelope>();
        Assert.Equal("henry", body!.Profile.Username);
        Assert.False(body.Profile.Following);
    }

    [Fact]
    public async Task Follow_Then_Unfollow_TogglesFollowing()
    {
        if (!_fixture.Available) return;

        string ivyToken = await RegisterAndGetToken("ivy", "ivy@example.com");
        await RegisterAndGetToken("jack", "jack@example.com");

        var followResponse = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/profiles/jack/follow", ivyToken));
        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);
        var followed = await followResponse.Content.ReadFromJsonAsync<ProfileResponseEnvelope>();
        Assert.True(followed!.Profile.Following);

        var unfollowResponse = await _client.SendAsync(WithToken(HttpMethod.Delete, "/api/profiles/jack/follow", ivyToken));
        Assert.Equal(HttpStatusCode.OK, unfollowResponse.StatusCode);
        var unfollowed = await unfollowResponse.Content.ReadFromJsonAsync<ProfileResponseEnvelope>();
        Assert.False(unfollowed!.Profile.Following);
    }

    [Fact]
    public async Task Follow_WithoutToken_Returns401()
    {
        if (!_fixture.Available) return;

        await RegisterAndGetToken("kim", "kim@example.com");

        var response = await _client.PostAsync("/api/profiles/kim/follow", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Follow_UnknownUser_Returns404()
    {
        if (!_fixture.Available) return;

        string token = await RegisterAndGetToken("liam", "liam@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/profiles/nobody/follow", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
