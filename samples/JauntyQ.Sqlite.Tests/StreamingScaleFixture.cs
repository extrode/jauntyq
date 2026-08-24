using Microsoft.Data.Sqlite;
using JauntyQ.Generated;

namespace JauntyQ.Sqlite.Tests;

public sealed class StreamingScaleFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public string ConnectionString { get; }
    public JauntyDb Db { get; }
    public SqliteConnection Connection { get; }

    public const int SeededProductCount = 20_000;

    public StreamingScaleFixture()
    {
        string dbName = "jauntyq_sqlite_scale_" + Guid.NewGuid().ToString("N");
        ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        string ddlPath = Path.Combine(AppContext.BaseDirectory, "schema.sqlite.sql");
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = File.ReadAllText(ddlPath);
            cmd.ExecuteNonQuery();
        }

        using (var tx = _keepAlive.BeginTransaction())
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "insert into Products (ProductName, SupplierId, CategoryId, UnitPrice, Discontinued) " +
                "values (@n, @s, @c, @p, 0)";
            var n = cmd.CreateParameter(); n.ParameterName = "@n";
            var s = cmd.CreateParameter(); s.ParameterName = "@s";
            var c = cmd.CreateParameter(); c.ParameterName = "@c";
            var p = cmd.CreateParameter(); p.ParameterName = "@p";
            cmd.Parameters.Add(n); cmd.Parameters.Add(s); cmd.Parameters.Add(c); cmd.Parameters.Add(p);
            for (int i = 0; i < SeededProductCount; i++)
            {
                n.Value = "Scale product " + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                s.Value = 1 + (i % 4);
                c.Value = 1 + (i % 4);
                p.Value = (i % 1000) * 0.01m;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
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
