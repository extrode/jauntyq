using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JauntyQ.Conduit.Postgres.Tests.Auth;

public sealed class TokenAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// RealWorld's spec uses `Authorization: Token &lt;jwt&gt;`, not the standard
/// `Bearer` scheme -- the built-in JwtBearerHandler hardcodes the `Bearer`
/// prefix and has no supported hook to swap it, so this is a small custom
/// handler instead. Returns NoResult() (never Fail()) on a missing/malformed/
/// invalid header so auth-optional endpoints proceed unauthenticated rather
/// than short-circuiting the pipeline with a 401 -- RequireAuthorization() on
/// specific endpoints is what turns "unauthenticated" into an actual 401.
/// </summary>
public sealed class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationSchemeOptions>
{
    public const string SchemeName = "Token";
    private const string Prefix = "Token ";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes(JwtOptions.SigningKey));

    public TokenAuthenticationHandler(
        IOptionsMonitor<TokenAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        string? header = headerValues.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        string jwt = header[Prefix.Length..].Trim();
        if (jwt.Length == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey,
        };

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(jwt, validationParameters, out _);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (SecurityTokenException)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}

/// <summary>
/// Small helper for endpoint handlers to read the optional viewer id straight
/// into the existing repository `int? viewerId` parameters -- no repository
/// changes needed for auth-optional endpoints.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        string? id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : int.Parse(id);
    }
}
