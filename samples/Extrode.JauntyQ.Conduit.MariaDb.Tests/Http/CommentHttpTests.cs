using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Conduit.MariaDb.Tests.Contracts;
using Xunit;

namespace Conduit.MariaDb.Tests.Http;

public sealed class CommentHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly ConduitWebAppFixture _fixture;
    private readonly HttpClient _client;

    public CommentHttpTests(ConduitWebAppFixture fixture)
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

    private async Task<string> CreateArticle(string token, string title)
    {
        var body = new UpsertArticleRequestEnvelope(new UpsertArticleRequest(title, "desc", "body", Array.Empty<string>()));
        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles", token).Also(m =>
            m.Content = JsonContent.Create(body)));
        var created = await response.Content.ReadFromJsonAsync<ArticleResponseEnvelope>();
        return created!.Article.Slug;
    }

    [SkippableFact]
    public async Task ListComments_UnknownSlug_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        var response = await _client.GetAsync("/api/articles/no-such-slug/comments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task ListComments_NoComments_ReturnsEmptyBareArray()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("carl", "carl@example.com");
        string slug = await CreateArticle(token, "Carl Article");

        var response = await _client.GetAsync($"/api/articles/{slug}/comments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CommentsResponse>();
        Assert.Empty(body!.Comments);
    }

    [SkippableFact]
    public async Task AddComment_Valid_Returns201WithComment()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("dana", "dana@example.com");
        string slug = await CreateArticle(token, "Dana Article");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/comments", token).Also(m =>
            m.Content = JsonContent.Create(new AddCommentRequestEnvelope(new AddCommentRequest("Great read!")))));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CommentResponseEnvelope>();
        Assert.Equal("Great read!", created!.Comment.Body);
        Assert.Equal("dana", created.Comment.Author.Username);
    }

    [SkippableFact]
    public async Task AddComment_WithoutToken_Returns401()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("earl", "earl@example.com");
        string slug = await CreateArticle(token, "Earl Article");

        var response = await _client.PostAsJsonAsync($"/api/articles/{slug}/comments",
            new AddCommentRequestEnvelope(new AddCommentRequest("No token")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task AddComment_UnknownSlug_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("fran", "fran@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Post, "/api/articles/no-such-slug/comments", token).Also(m =>
            m.Content = JsonContent.Create(new AddCommentRequestEnvelope(new AddCommentRequest("Body")))));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task ListComments_AfterAdd_ReturnsBareArrayWithComment()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("greg", "greg@example.com");
        string slug = await CreateArticle(token, "Greg Article");
        await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/comments", token).Also(m =>
            m.Content = JsonContent.Create(new AddCommentRequestEnvelope(new AddCommentRequest("First!")))));

        var response = await _client.GetAsync($"/api/articles/{slug}/comments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CommentsResponse>();
        Assert.Single(body!.Comments);
        Assert.Equal("First!", body.Comments[0].Body);
    }

    [SkippableFact]
    public async Task DeleteComment_ByAuthor_RemovesComment()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("hana", "hana@example.com");
        string slug = await CreateArticle(token, "Hana Article");
        var addResponse = await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/comments", token).Also(m =>
            m.Content = JsonContent.Create(new AddCommentRequestEnvelope(new AddCommentRequest("To delete")))));
        var added = await addResponse.Content.ReadFromJsonAsync<CommentResponseEnvelope>();

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}/comments/{added!.Comment.Id}", token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listResponse = await _client.GetAsync($"/api/articles/{slug}/comments");
        var list = await listResponse.Content.ReadFromJsonAsync<CommentsResponse>();
        Assert.Empty(list!.Comments);
    }

    [SkippableFact]
    public async Task DeleteComment_ByNonAuthor_Returns403()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("ian", "ian@example.com");
        string slug = await CreateArticle(token, "Ian Article");
        var addResponse = await _client.SendAsync(WithToken(HttpMethod.Post, $"/api/articles/{slug}/comments", token).Also(m =>
            m.Content = JsonContent.Create(new AddCommentRequestEnvelope(new AddCommentRequest("Mine")))));
        var added = await addResponse.Content.ReadFromJsonAsync<CommentResponseEnvelope>();
        string otherToken = await RegisterAndGetToken("jade", "jade@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}/comments/{added!.Comment.Id}", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [SkippableFact]
    public async Task DeleteComment_UnknownCommentId_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("kyle", "kyle@example.com");
        string slug = await CreateArticle(token, "Kyle Article");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, $"/api/articles/{slug}/comments/999999", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task DeleteComment_UnknownArticleSlug_Returns404()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);

        string token = await RegisterAndGetToken("lena", "lena@example.com");

        var response = await _client.SendAsync(WithToken(HttpMethod.Delete, "/api/articles/no-such-slug/comments/1", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
