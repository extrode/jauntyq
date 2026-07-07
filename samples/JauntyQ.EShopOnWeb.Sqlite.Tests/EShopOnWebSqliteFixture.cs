using Microsoft.Data.Sqlite;
using JauntyQ.Generated;

namespace JauntyQ.EShopOnWeb.Sqlite.Tests;

/// <summary>
/// Spins up a fresh, isolated SQLite database (a uniquely-named shared
/// in-memory database kept alive for the fixture's lifetime), applies the
/// eShopOnWeb schema + seed data, and exposes a JauntyDb over it. See
/// the test log at the repo root for the flattening/scope
/// decisions behind this schema. No Docker required, so <see cref="Available"/>
/// is always true -- kept only so test files written against the
/// Postgres fixture's soft-skip pattern port over unchanged.
/// </summary>
public sealed class EShopOnWebSqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public bool Available => true;
    public string? SkipReason => null;
    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public EShopOnWebSqliteFixture()
    {
        string dbName = "jauntyq_eshoponweb_" + Guid.NewGuid().ToString("N");
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        string ddlPath = Path.Combine(AppContext.BaseDirectory, "schema.sqlite.sql");
        string ddl = File.ReadAllText(ddlPath);
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        Connection = new SqliteConnection(ConnectionString);
        Connection.Open();
        Db = new JauntyDb(Connection);
    }

    public void Dispose()
    {
        Connection.Dispose();
        _keepAlive.Dispose();
    }
}
