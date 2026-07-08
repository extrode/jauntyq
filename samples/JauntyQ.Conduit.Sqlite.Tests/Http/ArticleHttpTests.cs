using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JauntyQ.Conduit.Sqlite.Tests.Contracts;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using JauntyQ.Generated;
using Microsoft.Data.Sqlite;
using Xunit;

namespace JauntyQ.Conduit.Sqlite.Tests.Http;

public sealed class ArticleHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly ConduitWebAppFixture _fixture;
    private readonly HttpClient _client;

    public ArticleHttpTests(ConduitWebAppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

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

    // Article write endpoints don't exist yet (Milestone 8), so read-endpoint
    // tests seed data directly through the repository layer, on a fresh
    // connection sharing the fixture's named in-memory database.
    private string SeedArticle(string authorUsername, string title, params string[] tags)
    {
        using var connection = new SqliteConnection(_fixture.ConnectionString);
        connection.Open();
        var db = new JauntyDb(connection);
        var users = new UserRepository(db);
        var articles = new ArticleRepository(db);

        var author = users.GetByUsername(authorUsername)!;
        return articles.Create(author.Id, title, "desc", "body", tags, DateTime.UtcNow.ToString("O"));
    }

    [Fact]
    public async Task GetArticle_UnknownSlug_Returns404()
    {
        var response = await _client.GetAsync("/api/articles/no-such-slug");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetArticle_KnownSlug_ReturnsArticle()
    {
        await RegisterAndGetToken("mona", "mona@example.com");
        string slug = SeedArticle("mona", "Mona's First Article", "dragons");

        var response = await _client.GetAsync($"/api/articles/{slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.Equal(slug, body!.Article.Slug);
        Assert.Equal("mona", body.Article.Author.Username);
        Assert.Contains("dragons", body.Article.TagList);
    }

    [Fact]
    public async Task ListArticles_FilterByAuthor_ReturnsOnlyThatAuthorsArticles()
    {
        await RegisterAndGetToken("nora", "nora@example.com");
        await RegisterAndGetToken("oscar", "oscar@example.com");
        SeedArticle("nora", "Nora Article One");
        SeedArticle("oscar", "Oscar Article One");

        var response = await _client.GetAsync("/api/articles?author=nora");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticlesResponse>();
        Assert.All(body!.Articles, a => Assert.Equal("nora", a.Author.Username));
        Assert.Equal(body.Articles.Count, body.ArticlesCount);
    }

    [Fact]
    public async Task ListArticles_WithLimit_RespectsPagination()
    {
        await RegisterAndGetToken("piper", "piper@example.com");
        SeedArticle("piper", "Piper Article One");
        SeedArticle("piper", "Piper Article Two");
        SeedArticle("piper", "Piper Article Three");

        var response = await _client.GetAsync("/api/articles?author=piper&limit=2");

        var body = await response.Content.ReadFromJsonAsync<ArticlesResponse>();
        Assert.Equal(2, body!.Articles.Count);
        Assert.Equal(3, body.ArticlesCount);
    }

    [Fact]
    public async Task Feed_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/articles/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Feed_ReturnsOnlyArticlesFromFollowedAuthors()
    {
        string quinnToken = await RegisterAndGetToken("quinn", "quinn@example.com");
        await RegisterAndGetToken("rex", "rex@example.com");
        SeedArticle("rex", "Rex Article One");

        var followResponse = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/profiles/rex/follow", quinnToken));
        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);

        var response = await _client.SendAsync(WithToken(HttpMethod.Get, "/api/articles/feed", quinnToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticlesResponse>();
        Assert.Single(body!.Articles);
        Assert.Equal("rex", body.Articles[0].Author.Username);
    }
}
