using System.Net;
using System.Net.Http.Json;
using Conduit.Sqlite.Tests.Contracts;
using Xunit;

namespace Conduit.Sqlite.Tests.Http;

public sealed class TagHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly HttpClient _client;

    public TagHttpTests(ConduitWebAppFixture fixture) => _client = fixture.CreateClient();

    [Fact]
    public async Task GetTags_ReturnsBareArray_NoAuthRequired()
    {
        var response = await _client.GetAsync("/api/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagsResponse>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Tags);
    }
}
