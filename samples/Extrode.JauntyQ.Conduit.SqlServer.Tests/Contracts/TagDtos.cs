namespace Conduit.SqlServer.Tests.Contracts;

public sealed record TagsResponse(IReadOnlyList<string> Tags);
