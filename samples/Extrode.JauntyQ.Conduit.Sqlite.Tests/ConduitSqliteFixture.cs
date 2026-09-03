using Microsoft.Data.Sqlite;
using Extrode.JauntyQ.Generated;

namespace Conduit.Sqlite.Tests;

/// <summary>
/// Spins up a fresh, isolated SQLite database (a uniquely-named shared
/// in-memory database kept alive for the fixture's lifetime), applies the
/// Conduit/RealWorld schema + seed data, and exposes a JauntyDb over it.
/// See the test log's "Part 3 kickoff scope decisions" for the
/// scope behind this schema. No Docker required, so <see cref="Available"/>
/// is always true -- kept only so test files written against the other
/// dialect fixtures' soft-skip pattern would port over unchanged.
/// SQLite disables foreign-key enforcement per connection by default, so
/// `PRAGMA foreign_keys = ON` is set explicitly -- required for the
/// ON DELETE CASCADE behavior article deletion relies on.
/// </summary>
public sealed class ConduitSqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public bool Available => true;
    public string? SkipReason => null;
    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public ConduitSqliteFixture()
    {
        string dbName = "jauntyq_conduit_" + Guid.NewGuid().ToString("N");
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
        EnableForeignKeys(_keepAlive);

        string ddlPath = Path.Combine(AppContext.BaseDirectory, "schema.sqlite.sql");
        string ddl = File.ReadAllText(ddlPath);
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        Connection = new SqliteConnection(ConnectionString);
        Connection.Open();
        EnableForeignKeys(Connection);
        Db = new JauntyDb(Connection);
    }

    private static void EnableForeignKeys(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Connection.Dispose();
        _keepAlive.Dispose();
    }
}
