namespace JauntyQ.Conduit.Postgres.Tests.Domain;

public sealed record Profile(string Username, string Bio, string? Image, bool Following);
