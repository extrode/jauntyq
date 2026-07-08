using JauntyQ.Conduit.Sqlite.Tests.Auth;
using JauntyQ.Conduit.Sqlite.Tests.Repositories;
using JauntyQ.Generated;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Overridable so Http/ConduitWebAppFixture.cs can point this at a per-test-run
// isolated in-memory database via ConfigureWebHost, without touching this file.
string connectionString = builder.Configuration["ConnectionString"] ?? "Data Source=conduit.db";

builder.Services.AddScoped<SqliteConnection>(_ =>
{
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

app.Run();

// Marker type WebApplicationFactory<Program> keys off; top-level-statement
// Program is otherwise an internal, unreferenceable type.
public partial class Program { }
