using Microsoft.Data.Sqlite;
using JauntyQ.Generated;

namespace JauntyQ.Chinook.Sqlite.Tests;

/// <summary>
/// Spins up a fresh, isolated SQLite database (a uniquely-named shared
/// in-memory database kept alive for the fixture's lifetime), applies the
/// canonical Chinook 1.4.5 schema + full seed data (vendored verbatim from
/// lerocha/chinook-database, MIT), and exposes a JauntyDb over it. No Docker
/// required.
/// </summary>
public sealed class ChinookSqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public ChinookSqliteFixture()
    {
        string dbName = "jauntyq_chinook_" + Guid.NewGuid().ToString("N");
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
