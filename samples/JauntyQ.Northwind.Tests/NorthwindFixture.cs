using Microsoft.Data.SqlClient;
using JauntyQ.Generated;

namespace JauntyQ.Northwind.Tests;

public class NorthwindFixture : IDisposable
{
    internal const string ConnectionString =
        "Server=localhost;Database=Northwind;Trusted_Connection=true;TrustServerCertificate=true";

    public System.Data.Common.DbConnection Connection { get; }
    public JauntyQDb Db { get; }

    public NorthwindFixture()
    {
        Connection = new SqlConnection(ConnectionString);
        Connection.Open();
        Db = new JauntyQDb(Connection);
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}
