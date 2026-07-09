namespace JauntyQ.Conduit.Postgres.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
