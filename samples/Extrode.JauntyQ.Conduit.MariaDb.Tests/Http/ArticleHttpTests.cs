using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Conduit.MariaDb.Tests.Contracts;
using Conduit.MariaDb.Tests.Repositories;
using Extrode.JauntyQ.Generated;
using MySqlConnector;
using Xunit;

namespace Conduit.MariaDb.Tests.Http;

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
    // connection sharing the fixture's database.
    private string SeedArticle(string authorUsername, string title, params string[] tags)
    {
        using var connection = new MySqlConnection(_fixture.ConnectionString);
        connection.Open();
        var db = new JauntyDb(connection);
        var users = new UserRepository(db);
        var articles = new ArticleRepository(db);

        var author = users.GetByUsername(authorUsername)!;
        return articles.Create(author.Id, title, "desc", "body", tags, DateTime.UtcNow.ToString("O"));
    }

    [SkippableFact]
    public async Task GetArticle_UnknownSlug_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        var response = await _client.GetAsync("/api/articles/no-such-slug");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task GetArticle_KnownSlug_ReturnsArticle()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await RegisterAndGetToken("mona", "mona@example.com");
        string slug = SeedArticle("mona", "Mona's First Article", "dragons");

        var response = await _client.GetAsync($"/api/articles/{slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.Equal(slug, body!.Article.Slug);
        Assert.Equal("mona", body.Article.Author.Username);
        Assert.Contains("dragons", body.Article.TagList);
    }

    [SkippableFact]
    public async Task ListArticles_FilterByAuthor_ReturnsOnlyThatAuthorsArticles()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

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

    [SkippableFact]
    public async Task ListArticles_WithLimit_RespectsPagination()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await RegisterAndGetToken("piper", "piper@example.com");
        SeedArticle("piper", "Piper Article One");
        SeedArticle("piper", "Piper Article Two");
        SeedArticle("piper", "Piper Article Three");

        var response = await _client.GetAsync("/api/articles?author=piper&limit=2");

        var body = await response.Content.ReadFromJsonAsync<ArticlesResponse>();
        Assert.Equal(2, body!.Articles.Count);
        Assert.Equal(3, body.ArticlesCount);
    }

    [SkippableFact]
    public async Task Feed_WithoutToken_Returns401()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        var response = await _client.GetAsync("/api/articles/feed");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Feed_ReturnsOnlyArticlesFromFollowedAuthors()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

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

    private static UpsertArticleRequestEnvelope UpsertBody(string title, string description = "desc", string body = "body", params string[] tags) =>
        new(new UpsertArticleRequest(title, description, body, tags));

    [SkippableFact]
    public async Task CreateArticle_Valid_Returns201WithArticle()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("sam", "sam@example.com");

        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/articles")
        {
            Content = JsonContent.Create(UpsertBody("Sam Article", tags: "reactjs"))
        }.Also(m => m.Headers.Authorization = new AuthenticationHeaderValue("Token", token)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.Equal("sam-article", created!.Article.Slug);
        Assert.Equal("sam", created.Article.Author.Username);
        Assert.Contains("reactjs", created.Article.TagList);
    }

    [SkippableFact]
    public async Task CreateArticle_MissingTitle_Returns422()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("tara", "tara@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles", token).Also(m =>
            m.Content = JsonContent.Create(UpsertBody(""))));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [SkippableFact]
    public async Task CreateArticle_DuplicateSlug_Returns422()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("uma", "uma@example.com");
        await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles", token).Also(m =>
            m.Content = JsonContent.Create(UpsertBody("Uma Article"))));

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles", token).Also(m =>
            m.Content = JsonContent.Create(UpsertBody("Uma Article"))));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
    }

    [SkippableFact]
    public async Task CreateArticle_WithoutToken_Returns401()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        var response = await _client.PostAsJsonAsync("/api/articles", UpsertBody("No Token Article"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task UpdateArticle_ByAuthor_UpdatesFields()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("vince", "vince@example.com");
        string slug = SeedArticle("vince", "Vince Original Title");

        var response = await _client.SendAsync(WithToken(HttpMethod.Put, $"/api/articles/{slug}", token).Also(m =>
            m.Content = JsonContent.Create(UpsertBody("Vince Updated Title", "new desc", "new body"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        Assert.Equal("Vince Updated Title", updated!.Article.Title);
        Assert.Equal("new desc", updated.Article.Description);
        Assert.Equal(slug, updated.Article.Slug);
    }

    [SkippableFact]
    public async Task UpdateArticle_ByNonAuthor_Returns403()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await RegisterAndGetToken("walt", "walt@example.com");
        string slug = SeedArticle("walt", "Walt Article");
        string otherToken = await RegisterAndGetToken("xena", "xena@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Put, $"/api/articles/{slug}", otherToken).Also(m =>
            m.Content = JsonContent.Create(UpsertBody("Hijacked Title"))));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task UpdateArticle_UnknownSlug_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("yara", "yara@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Put, "/api/articles/no-such-slug", token).Also(m =>
            m.Content = JsonContent.Create(UpsertBody("Doesn't Matter"))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task DeleteArticle_ByAuthor_RemovesArticle()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("zane", "zane@example.com");
        string slug = SeedArticle("zane", "Zane Article");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}", token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await _client.GetAsync($"/api/articles/{slug}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [SkippableFact]
    public async Task DeleteArticle_ByNonAuthor_Returns403()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        await RegisterAndGetToken("aaron", "aaron@example.com");
        string slug = SeedArticle("aaron", "Aaron Article");
        string otherToken = await RegisterAndGetToken("beth", "beth@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

internal static class HttpRequestMessageExtensions
{
    public static HttpRequestMessage Also(this HttpRequestMessage message, Action<HttpRequestMessage> configure)
    {
        configure(message);
        return message;
    }
}
