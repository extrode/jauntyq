using Microsoft.Data.Sqlite;
using JauntyQ.Generated;

namespace JauntyQ.Sqlite.Tests;

/// <summary>
/// Spins up a fresh, isolated SQLite database (a uniquely-named shared
/// in-memory database kept alive for the fixture's lifetime), applies the
/// Northwind-subset DDL + seed, and exposes a JauntyDb over it. Proves the
/// generated code — auto-CRUD, ON CONFLICT upsert, RETURNING identity, typed
/// ordinal reads — actually runs against a real SQLite engine, not just SQL
/// Server. No Docker or external service required.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public SqliteFixture()
    {
        // Unique name so parallel test classes never share state; Cache=Shared
        // keeps the in-memory DB alive as long as one connection is open.
        string dbName = "jauntyq_sqlite_" + Guid.NewGuid().ToString("N");
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Holds the in-memory database open for the fixture's lifetime.
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

    public SqliteConnection NewConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        Connection.Dispose();
        _keepAlive.Dispose();
    }
}
