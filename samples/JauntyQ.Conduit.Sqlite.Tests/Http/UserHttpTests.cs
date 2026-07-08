using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JauntyQ.Conduit.Sqlite.Tests.Contracts;
using Xunit;

namespace JauntyQ.Conduit.Sqlite.Tests.Http;

public sealed class UserHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private const string DefaultTestCredential = "testcred0001";

    private readonly HttpClient _client;

    public UserHttpTests(ConduitWebAppFixture fixture) => _client = fixture.CreateClient();

    private static RegisterRequestEnvelope Register(string username, string email, string credential = DefaultTestCredential) =>
        new(new RegisterRequest(username, email, credential));

    [Fact]
    public async Task Register_ValidUser_Returns201WithToken()
    {
        var response = await _client.PostAsJsonAsync("/api/users", Register("alice", "alice@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        Assert.NotNull(body);
        Assert.Equal("alice", body!.User.Username);
        Assert.Equal("alice@example.com", body.User.Email);
        Assert.False(string.IsNullOrWhiteSpace(body.User.Token));
    }

    [Fact]
    public async Task Register_DuplicateUsername_Returns422()
    {
        await _client.PostAsJsonAsync("/api/users", Register("bob", "bob1@example.com"));
        var response = await _client.PostAsJsonAsync("/api/users", Register("bob", "bob2@example.com"));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Contains("username", body!.Errors.Keys);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns422()
    {
        await _client.PostAsJsonAsync("/api/users", Register("carol1", "carol@example.com"));
        var response = await _client.PostAsJsonAsync("/api/users", Register("carol2", "carol@example.com"));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Contains("email", body!.Errors.Keys);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/users", Register("dave", "dave@example.com", "testcred0002"));
        string registerBody = await registerResponse.Content.ReadAsStringAsync();
        Assert.True(registerResponse.StatusCode == HttpStatusCode.Created, $"register failed: {(int)registerResponse.StatusCode} {registerBody}");

        var response = await _client.PostAsJsonAsync("/api/users/login", new LoginRequestEnvelope(new LoginRequest("dave@example.com", "testcred0002")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.User.Token));
    }

    [Fact]
    public async Task Login_WrongCredential_Returns401()
    {
        await _client.PostAsJsonAsync("/api/users", Register("erin", "erin@example.com", "testcred0003"));

        var response = await _client.PostAsJsonAsync("/api/users/login", new LoginRequestEnvelope(new LoginRequest("erin@example.com", "testcred0004")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithToken_ReturnsUser()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/users", Register("frank", "frank@example.com"));
        var registered = await registerResponse.Content.ReadFromJsonAsync<UserResponseEnvelope>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", registered!.User.Token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        Assert.Equal("frank", body!.User.Username);
    }

    [Fact]
    public async Task UpdateUser_ChangesBioAndPersists()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/users", Register("grace", "grace@example.com"));
        var registered = await registerResponse.Content.ReadFromJsonAsync<UserResponseEnvelope>();

        using var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/user")
        {
            Content = JsonContent.Create(new UpdateUserRequestEnvelope(new UpdateUserRequest(null, null, null, "new bio", null))),
        };
        updateRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", registered!.User.Token);
        var updateResponse = await _client.SendAsync(updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        Assert.Equal("new bio", updated!.User.Bio);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/user");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", updated.User.Token);
        var getResponse = await _client.SendAsync(getRequest);
        var current = await getResponse.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        Assert.Equal("new bio", current!.User.Bio);
    }
}
