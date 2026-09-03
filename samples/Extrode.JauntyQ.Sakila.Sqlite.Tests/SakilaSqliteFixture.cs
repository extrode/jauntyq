using Microsoft.Data.Sqlite;
using Extrode.JauntyQ.Generated;

namespace Extrode.JauntyQ.Sakila.Sqlite.Tests;

/// <summary>
/// Spins up a fresh, isolated SQLite database (a uniquely-named shared
/// in-memory database kept alive for the fixture's lifetime), applies the
/// trimmed Sakila/Pagila schema + curated seed data, and exposes a JauntyDb
/// over it. See the test log at the repo root for what
/// "trimmed" means and why. No Docker required.
/// </summary>
public sealed class SakilaSqliteFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public SakilaSqliteFixture()
    {
        string dbName = "jauntyq_sakila_" + Guid.NewGuid().ToString("N");
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
