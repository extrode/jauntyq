using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Conduit.Sqlite.Tests.Contracts;
using Xunit;

namespace Conduit.Sqlite.Tests.Http;

public sealed class FavoriteHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly HttpClient _client;

    public FavoriteHttpTests(ConduitWebAppFixture fixture) => _client = fixture.CreateClient();

    private async Task<string> RegisterAndGetToken(string username, string email)
    {
        var body = new RegisterRequestEnvelope(new RegisterRequest(username, email, "testcred0001"));
        var response = await _client.PostAsJsonAsync("/api/users", body);
        var parsed = await response.Content.ReadFromJsonAsync<UserResponseEnvelope>();
        return parsed!.User.Token;
    }

    private HttpRequestMessage WithToken(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        return request;
    }

    private async Task<string> CreateArticle(string token, string title)
    {
        var body = new UpsertArticleRequestEnvelope(new UpsertArticleRequest(title, "desc", "body", Array.Empty<string>()));
        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles", token).Also(m =>
            m.Content = JsonContent.Create(body)));
        var created = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        return created!.Article.Slug;
    }

    [Fact]
    public async Task FavoriteArticle_Valid_SetsFavoritedAndIncrementsCount()
    {
        string authorToken = await RegisterAndGetToken("mira", "mira@example.com");
        string slug = await CreateArticle(authorToken, "Mira Article");
        string readerToken = await RegisterAndGetToken("noel", "noel@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/favorite", readerToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.True(body!.Article.Favorited);
        Assert.Equal(1, body.Article.FavoritesCount);
    }

    [Fact]
    public async Task UnfavoriteArticle_AfterFavoriting_ClearsFavoritedAndDecrementsCount()
    {
        string authorToken = await RegisterAndGetToken("owen", "owen@example.com");
        string slug = await CreateArticle(authorToken, "Owen Article");
        string readerToken = await RegisterAndGetToken("paula", "paula@example.com");
        await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/favorite", readerToken));

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}/favorite", readerToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.False(body!.Article.Favorited);
        Assert.Equal(0, body.Article.FavoritesCount);
    }

    [Fact]
    public async Task FavoriteArticle_WithoutToken_Returns401()
    {
        string authorToken = await RegisterAndGetToken("quinn", "quinn2@example.com");
        string slug = await CreateArticle(authorToken, "Quinn Article");

        var response = await _client.PostAsync($"/api/articles/{slug}/favorite", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FavoriteArticle_UnknownSlug_Returns404()
    {
        string token = await RegisterAndGetToken("ruth", "ruth@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles/no-such-slug/favorite", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnfavoriteArticle_UnknownSlug_Returns404()
    {
        string token = await RegisterAndGetToken("stan", "stan@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, "/api/articles/no-such-slug/favorite", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
