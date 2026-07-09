using System.Net;
using System.Net.Http.Json;
using JauntyQ.Conduit.SqlServer.Tests.Contracts;
using Xunit;

namespace JauntyQ.Conduit.SqlServer.Tests.Http;

public sealed class TagHttpTests : IClassFixture<ConduitWebAppFixture>
{
    private readonly ConduitWebAppFixture _fixture;
    private readonly HttpClient _client;

    public TagHttpTests(ConduitWebAppFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task GetTags_ReturnsBareArray_NoAuthRequired()
    {
        if (!_fixture.Available) return;

        var response = await _client.GetAsync("/api/tags");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagsResponse>();
        Assert.NotNull(body);
        Assert.NotNull(body!.Tags);
    }
}
