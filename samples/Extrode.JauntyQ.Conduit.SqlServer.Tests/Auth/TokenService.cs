using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Extrode.JauntyQ.Generated;
using Microsoft.IdentityModel.Tokens;

namespace Conduit.SqlServer.Tests.Auth;

/// <summary>
/// Mints the JWT returned in the "user.token" field on register/login.
/// Deliberately not a UserRepository method: token issuance is an HTTP/auth
/// concern, not a data-layer concern.
/// </summary>
public sealed class TokenService
{
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes(JwtOptions.SigningKey));

    public string CreateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
        };

        var credentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(JwtOptions.TokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
