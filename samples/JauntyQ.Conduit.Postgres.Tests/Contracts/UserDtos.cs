using JauntyQ.Generated;

namespace JauntyQ.Conduit.Postgres.Tests.Contracts;

/// <summary>
/// Wire shape for {"user": {email, token, username, bio, image}} -- not a
/// direct wrap of the generated User POCO, which must not leak PasswordHash
/// or the internal Id, and needs the minted token added.
/// </summary>
public sealed record UserResponse(string Email, string Token, string Username, string Bio, string? Image)
{
    public static UserResponse From(User user, string token) =>
        new(user.Email, token, user.Username, user.Bio, user.Image);
}

public sealed record UserResponseEnvelope(UserResponse User);

public sealed record RegisterRequest(string Username, string Email, string Password);
public sealed record RegisterRequestEnvelope(RegisterRequest User);

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginRequestEnvelope(LoginRequest User);

public sealed record UpdateUserRequest(string? Email, string? Username, string? Password, string? Bio, string? Image);
public sealed record UpdateUserRequestEnvelope(UpdateUserRequest User);
