using System.Text.Json;
using Extrode.JauntyQ.Schema;
using Xunit;

namespace Extrode.JauntyQ.Schema.Extraction.Tests;

public class ScopeLoaderTests
{
    [Fact]
    public void ALiteralNull_IsRefused()
    {
        var ex = Assert.Throws<JsonException>(() => ScopeLoader.Load("null"));

        Assert.Equal("scope file JSON was the literal 'null', not a scope object", ex.Message);
    }

    [Fact]
    public void EachEntry_LoadsItsTableAndColumn()
    {
        ScopeFile file = ScopeLoader.Load(
            @"{ ""scopes"": [ { ""table"": ""orders"", ""column"": ""tenant_id"" }, { ""table"": ""documents"" } ] }");

        Assert.Equal(2, file.Scopes.Count);
        Assert.Equal("orders", file.Scopes[0].Table);
        Assert.Equal("tenant_id", file.Scopes[0].Column);
        Assert.Null(file.Scopes[1].Column);
    }

    [Fact]
    public void AnEmptyObject_ScopesNothing()
    {
        Assert.Empty(ScopeLoader.Load("{}").Scopes);
    }
}
