using Microsoft.Data.Sqlite;

namespace JauntyQ.Benchmarks;

/// <summary>
/// Shared in-memory SQLite database seeded with a configurable number of
/// Widgets. A single keep-alive connection holds the shared in-memory database
/// open for the process lifetime. The connection string is built via
/// SqliteConnectionStringBuilder (no embedded credentials — it is an in-memory
/// database name).
/// </summary>
public static class BenchDb
{
    private static SqliteConnection? _keepAlive;
    public static string ConnectionString { get; private set; } = "";

    private static string BuildConnectionString() =>
        new SqliteConnectionStringBuilder
        {
            DataSource = "jauntyq_bench",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();

    public static void Init(int rowCount)
    {
        if (_keepAlive != null)
            return;

        ConnectionString = BuildConnectionString();
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE Widgets (
                    WidgetId INTEGER PRIMARY KEY,
                    Name     TEXT NOT NULL,
                    Price    NUMERIC NOT NULL,
                    Quantity INTEGER NOT NULL,
                    Active   INTEGER NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        using (var tx = _keepAlive.BeginTransaction())
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO Widgets (Name, Price, Quantity, Active) VALUES ($n, $p, $q, $a)";
            var pn = cmd.CreateParameter(); pn.ParameterName = "$n"; cmd.Parameters.Add(pn);
            var pp = cmd.CreateParameter(); pp.ParameterName = "$p"; cmd.Parameters.Add(pp);
            var pq = cmd.CreateParameter(); pq.ParameterName = "$q"; cmd.Parameters.Add(pq);
            var pa = cmd.CreateParameter(); pa.ParameterName = "$a"; cmd.Parameters.Add(pa);
            for (int i = 1; i <= rowCount; i++)
            {
                pn.Value = "Widget " + i;
                pp.Value = (decimal)(i % 100) + 0.99m;
                pq.Value = i % 50;
                pa.Value = (i % 2 == 0) ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }
}
