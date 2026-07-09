namespace JauntyQ.Conduit.MariaDb.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
