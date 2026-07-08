using System.Security.Claims;
using JauntyQ.Conduit.Sqlite.Tests.Auth;
using JauntyQ.Conduit.Sqlite.Tests.Contracts;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using JauntyQ.Generated;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Connection string is read lazily from IConfiguration inside the factory, not hoisted
// into a local variable here -- WebApplicationFactory-based tests (Http/ConduitWebAppFixture.cs)
// inject their ConfigureAppConfiguration override at builder.Build() time, which is after
// this top-level code runs, so an eagerly-captured local would always see the default.
builder.Services.AddScoped<SqliteConnection>(sp =>
{
    string connectionString = sp.GetRequiredService<IConfiguration>()["ConnectionString"] ?? "Data Source=conduit.db";
    var conn = new SqliteConnection(connectionString);
    conn.Open();
    using var pragma = conn.CreateCommand();
    pragma.CommandText = "PRAGMA foreign_keys = ON";
    pragma.ExecuteNonQuery();
    return conn;
});
builder.Services.AddScoped(sp => new JauntyDb(sp.GetRequiredService<SqliteConnection>()));

builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<ProfileRepository>();
builder.Services.AddScoped<ArticleRepository>();
builder.Services.AddScoped<CommentRepository>();
builder.Services.AddScoped<FavoriteRepository>();

builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<TokenAuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapPost("/users", (RegisterRequestEnvelope body, UserRepository users, TokenService tokens) =>
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(body.User.Username)) errors["username"] = new[] { "can't be blank" };
    else if (users.GetByUsername(body.User.Username) is not null) errors["username"] = new[] { "has already been taken" };

    if (string.IsNullOrWhiteSpace(body.User.Email)) errors["email"] = new[] { "can't be blank" };
    else if (users.GetByEmail(body.User.Email) is not null) errors["email"] = new[] { "has already been taken" };

    if (string.IsNullOrWhiteSpace(body.User.Password)) errors["password"] = new[] { "can't be blank" };

    if (errors.Count > 0) return Results422.ValidationError(errors);

    int id = users.Register(body.User.Username, body.User.Email, body.User.Password);
    var user = users.GetById(id)!;
    string token = tokens.CreateToken(user);
    return Results.Json(new UserResponseEnvelope(UserResponse.From(user, token)), statusCode: StatusCodes.Status201Created);
});

api.MapPost("/users/login", (LoginRequestEnvelope body, UserRepository users, TokenService tokens) =>
{
    var user = users.Login(body.User.Email, body.User.Password);
    if (user is null) return Results.Unauthorized();

    string token = tokens.CreateToken(user);
    return Results.Json(new UserResponseEnvelope(UserResponse.From(user, token)));
});

api.MapGet("/user", (ClaimsPrincipal principal, UserRepository users, TokenService tokens) =>
{
    var user = users.GetById(principal.GetUserId()!.Value)!;
    string token = tokens.CreateToken(user);
    return Results.Json(new UserResponseEnvelope(UserResponse.From(user, token)));
}).RequireAuthorization();

api.MapPut("/user", (UpdateUserRequestEnvelope body, ClaimsPrincipal principal, UserRepository users, TokenService tokens) =>
{
    int id = principal.GetUserId()!.Value;
    var current = users.GetById(id)!;

    string? passwordHash = body.User.Password is null ? null : UserRepository.HashPassword(body.User.Password);
    users.UpdateProfile(
        id,
        username: body.User.Username ?? current.Username,
        email: body.User.Email ?? current.Email,
        bio: body.User.Bio ?? current.Bio,
        image: body.User.Image ?? current.Image,
        passwordHash: passwordHash);

    var updated = users.GetById(id)!;
    string token = tokens.CreateToken(updated);
    return Results.Json(new UserResponseEnvelope(UserResponse.From(updated, token)));
}).RequireAuthorization();

api.MapGet("/tags", (JauntyDb db) =>
{
    var names = db.Tags.GetAll().Select(t => t.Name).ToList();
    return Results.Json(new TagsResponse(names));
});

api.MapGet("/profiles/{username}", (string username, ClaimsPrincipal principal, ProfileRepository profiles) =>
{
    var profile = profiles.GetProfile(username, principal.GetUserId());
    return profile is null ? Results.NotFound() : Results.Json(new ProfileResponseEnvelope(profile));
});

api.MapPost("/profiles/{username}/follow", (string username, ClaimsPrincipal principal, UserRepository users, ProfileRepository profiles) =>
{
    var target = users.GetByUsername(username);
    if (target is null) return Results.NotFound();

    int followerId = principal.GetUserId()!.Value;
    profiles.Follow(followerId, target.Id);
    var profile = profiles.GetProfile(username, followerId);
    return Results.Json(new ProfileResponseEnvelope(profile!));
}).RequireAuthorization();

api.MapDelete("/profiles/{username}/follow", (string username, ClaimsPrincipal principal, UserRepository users, ProfileRepository profiles) =>
{
    var target = users.GetByUsername(username);
    if (target is null) return Results.NotFound();

    int followerId = principal.GetUserId()!.Value;
    profiles.Unfollow(followerId, target.Id);
    var profile = profiles.GetProfile(username, followerId);
    return Results.Json(new ProfileResponseEnvelope(profile!));
}).RequireAuthorization();

app.Run();

// Marker type WebApplicationFactory<Program> keys off; top-level-statement
// Program is otherwise an internal, unreferenceable type.
public partial class Program { }
