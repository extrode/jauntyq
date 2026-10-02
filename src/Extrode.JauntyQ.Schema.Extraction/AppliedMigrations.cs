using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Extrode.JauntyQ.Schema.Extraction;

/// <summary>
/// Reads the <c>schema_migrations</c> table a consumer's migration runner keeps,
/// in the shape <c>docs/06-reference/migration-tracking-contract.md</c> defines.
/// Read-only: it never creates the table or writes a row.
/// </summary>
public static class AppliedMigrations
{
    public const string TableName = "schema_migrations";

    /// <summary>
    /// The <c>version</c> of every row, in no particular order, or null when the
    /// connected database has no <c>schema_migrations</c> table. A missing table
    /// is reported as null rather than thrown because a database no runner has
    /// touched yet is an ordinary state, not an error.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ReadAsync(string dialect, string connectionString)
    {
        var (connection, existsQuery) = For(dialect, connectionString);
        await using DbConnection conn = connection;
        await conn.OpenAsync();

        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText = existsQuery;
            if (!Convert.ToBoolean(await exists.ExecuteScalarAsync()))
                return null;
        }

        await using var select = conn.CreateCommand();
        select.CommandText = $"SELECT version FROM {TableName}";
        var versions = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            versions.Add(reader.GetString(0));
        return versions;
    }

    // Each existence check resolves the name the way the unqualified SELECT on
    // the same connection will: Postgres through search_path, SQL Server through
    // the user's default schema, MySQL in the connected database.
    private static (DbConnection Connection, string ExistsQuery) For(string dialect, string connectionString) => dialect switch
    {
        "postgres" => (new NpgsqlConnection(connectionString),
            $"SELECT to_regclass('{TableName}') IS NOT NULL"),
        "sqlserver" => (new SqlConnection(connectionString),
            $"SELECT CASE WHEN OBJECT_ID(N'{TableName}', N'U') IS NULL THEN 0 ELSE 1 END"),
        "mysql" => (new MySqlConnection(connectionString),
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{TableName}'"),
        "sqlite" => (new SqliteConnection(connectionString),
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{TableName}'"),
        _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "No connection is wired for this dialect."),
    };
}
