namespace Conduit.MariaDb.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
