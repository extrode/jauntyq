using System.Data.Common;

namespace JauntyQ.Runtime;

public static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }

    public static DbDataReader ExecuteReaderSafe(this DbCommand cmd)
    {
        if (cmd.Connection != null && cmd.Connection.State != System.Data.ConnectionState.Open)
        {
            cmd.Connection.Open();
        }
        return cmd.ExecuteReader();
    }
}
