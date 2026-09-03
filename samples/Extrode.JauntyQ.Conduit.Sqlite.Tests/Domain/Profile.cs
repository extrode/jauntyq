namespace Conduit.Sqlite.Tests.Domain;

public sealed record Profile(string Username, string Bio, string? Image, bool Following);
