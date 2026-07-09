namespace JauntyQ.Conduit.MySql.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
