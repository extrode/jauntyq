using System.Security.Claims;
using Conduit.Postgres.Tests.Auth;
using Conduit.Postgres.Tests.Contracts;
using Conduit.Postgres.Tests.Repositories;
using Extrode.JauntyQ.Generated;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Connection string is read lazily from IConfiguration inside the factory, not hoisted
// into a local variable here -- WebApplicationFactory-based tests (Http/ConduitWebAppFixture.cs)
// inject their ConfigureAppConfiguration override at builder.Build() time, which is after
// this top-level code runs, so an eagerly-captured local would always see the default.
builder.Services.AddScoped<NpgsqlConnection>(sp =>
{
    string connectionString = sp.GetRequiredService<IConfiguration>()["ConnectionString"]
        ?? "Host=localhost;Database=conduit;Username=postgres";
    var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    return conn;
});
builder.Services.AddScoped(sp => new JauntyDb(sp.GetRequiredService<NpgsqlConnection>()));

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

api.MapGet("/articles", (string? tag, string? author, string? favorited, int? limit, int? offset,
    ClaimsPrincipal principal, ArticleRepository articles) =>
{
    var (views, total) = articles.List(tag, author, favorited, offset ?? 0, limit ?? 20, principal.GetUserId());
    return Results.Json(new ArticlesResponse(views, total));
});

api.MapGet("/articles/feed", (int? limit, int? offset, ClaimsPrincipal principal, ArticleRepository articles) =>
{
    var (views, total) = articles.Feed(principal.GetUserId()!.Value, offset ?? 0, limit ?? 20);
    return Results.Json(new ArticlesResponse(views, total));
}).RequireAuthorization();

api.MapGet("/articles/{slug}", (string slug, ClaimsPrincipal principal, ArticleRepository articles) =>
{
    var article = articles.GetBySlug(slug, principal.GetUserId());
    return article is null ? Results.NotFound() : Results.Json(new ArticleResponseEnvelope(article));
});

api.MapPost("/articles", (UpsertArticleRequestEnvelope body, ClaimsPrincipal principal, ArticleRepository articles) =>
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(body.Article.Title)) errors["title"] = new[] { "can't be blank" };
    if (string.IsNullOrWhiteSpace(body.Article.Description)) errors["description"] = new[] { "can't be blank" };
    if (string.IsNullOrWhiteSpace(body.Article.Body)) errors["body"] = new[] { "can't be blank" };
    if (errors.Count > 0) return Results422.ValidationError(errors);

    int authorId = principal.GetUserId()!.Value;
    string nowIso = DateTime.UtcNow.ToString("O");
    string slug;
    try
    {
        slug = articles.Create(authorId, body.Article.Title, body.Article.Description, body.Article.Body,
            body.Article.TagList ?? Array.Empty<string>(), nowIso);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results422.ValidationError("title", "must be unique");
    }

    var created = articles.GetBySlug(slug, authorId)!;
    return Results.Json(new ArticleResponseEnvelope(created), statusCode: StatusCodes.Status201Created);
}).RequireAuthorization();

api.MapPut("/articles/{slug}", (string slug, UpsertArticleRequestEnvelope body, ClaimsPrincipal principal,
    ArticleRepository articles, UserRepository users) =>
{
    int userId = principal.GetUserId()!.Value;
    var existing = articles.GetBySlug(slug, userId);
    if (existing is null) return Results.NotFound();

    var caller = users.GetById(userId)!;
    if (existing.Author.Username != caller.Username) return Results.Forbid();

    articles.Update(slug, body.Article.Title, body.Article.Description, body.Article.Body, DateTime.UtcNow.ToString("O"));
    var updated = articles.GetBySlug(slug, userId)!;
    return Results.Json(new ArticleResponseEnvelope(updated));
}).RequireAuthorization();

api.MapDelete("/articles/{slug}", (string slug, ClaimsPrincipal principal, ArticleRepository articles, UserRepository users) =>
{
    int userId = principal.GetUserId()!.Value;
    var existing = articles.GetBySlug(slug, userId);
    if (existing is null) return Results.NotFound();

    var caller = users.GetById(userId)!;
    if (existing.Author.Username != caller.Username) return Results.Forbid();

    articles.Delete(slug);
    return Results.NoContent();
}).RequireAuthorization();

api.MapPost("/articles/{slug}/favorite", (string slug, ClaimsPrincipal principal, ArticleRepository articles, FavoriteRepository favorites) =>
{
    int? articleId = articles.GetIdBySlug(slug);
    if (articleId is null) return Results.NotFound();

    int userId = principal.GetUserId()!.Value;
    favorites.Favorite(userId, articleId.Value);
    var updated = articles.GetBySlug(slug, userId)!;
    return Results.Json(new ArticleResponseEnvelope(updated));
}).RequireAuthorization();

api.MapDelete("/articles/{slug}/favorite", (string slug, ClaimsPrincipal principal, ArticleRepository articles, FavoriteRepository favorites) =>
{
    int? articleId = articles.GetIdBySlug(slug);
    if (articleId is null) return Results.NotFound();

    int userId = principal.GetUserId()!.Value;
    favorites.Unfavorite(userId, articleId.Value);
    var updated = articles.GetBySlug(slug, userId)!;
    return Results.Json(new ArticleResponseEnvelope(updated));
}).RequireAuthorization();

api.MapGet("/tags", (JauntyDb db) =>
{
    var names = db.Tags.GetAll().Select(t => t.Name).ToList();
    return Results.Json(new TagsResponse(names));
});

api.MapGet("/articles/{slug}/comments", (string slug, ClaimsPrincipal principal, ArticleRepository articles, CommentRepository comments) =>
{
    int? articleId = articles.GetIdBySlug(slug);
    if (articleId is null) return Results.NotFound();

    var views = comments.GetByArticleId(articleId.Value, principal.GetUserId());
    return Results.Json(new CommentsResponse(views));
});

api.MapPost("/articles/{slug}/comments", (string slug, AddCommentRequestEnvelope body, ClaimsPrincipal principal,
    ArticleRepository articles, CommentRepository comments) =>
{
    int? articleId = articles.GetIdBySlug(slug);
    if (articleId is null) return Results.NotFound();

    int authorId = principal.GetUserId()!.Value;
    int commentId = comments.Add(articleId.Value, authorId, body.Comment.Body, DateTime.UtcNow.ToString("O"));
    var created = comments.GetByArticleId(articleId.Value, authorId).First(c => c.Id == commentId);
    return Results.Json(new CommentResponseEnvelope(created), statusCode: StatusCodes.Status201Created);
}).RequireAuthorization();

api.MapDelete("/articles/{slug}/comments/{commentId:int}", (string slug, int commentId, ClaimsPrincipal principal,
    ArticleRepository articles, CommentRepository comments, UserRepository users) =>
{
    int? articleId = articles.GetIdBySlug(slug);
    if (articleId is null) return Results.NotFound();

    int userId = principal.GetUserId()!.Value;
    var existing = comments.GetByArticleId(articleId.Value, userId).FirstOrDefault(c => c.Id == commentId);
    if (existing is null) return Results.NotFound();

    var caller = users.GetById(userId)!;
    if (existing.Author.Username != caller.Username) return Results.Forbid();

    comments.Delete(commentId);
    return Results.NoContent();
}).RequireAuthorization();

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
