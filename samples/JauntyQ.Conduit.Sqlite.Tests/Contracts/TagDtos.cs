namespace JauntyQ.Conduit.Sqlite.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
