using JauntyQ.Conduit.MySql.Tests.Domain;

namespace JauntyQ.Conduit.MySql.Tests.Contracts;

/// <summary>
/// Domain.Profile's field names already match the wire shape 1:1
/// ({"profile": {username, bio, image, following}}) -- just needs wrapping.
/// </summary>
public sealed record ProfileResponseEnvelope(Profile Profile);
