namespace Conduit.SqlServer.Tests.Auth;

/// <summary>
/// Sample-only JWT settings. The signing key is a hardcoded constant, not a
/// secret pulled from configuration/a vault -- appropriate for a self-contained
/// sample/test host, never for a deployed service (same caveat as
/// UserRepository.HashPassword's SHA-256 stand-in).
/// </summary>
public static class JwtOptions
{
    public const string SigningKey = "conduit-sqlite-sample-signing-key-please-do-not-reuse!!";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
}
