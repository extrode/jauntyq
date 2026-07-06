namespace JauntyQ.Generator;

public static class DialectMapper
{
    /// <summary>
    /// Maps a SQL identifier to a PascalCase C# identifier. Splits on any run of
    /// non-alphanumeric characters (underscore, space, and anything else), so a
    /// real table name like "Order Details" becomes "OrderDetails". Because the
    /// output keeps only letters and digits, this is also the trust boundary for
    /// schema/alias names: a hostile value such as "X { get; } static ... //"
    /// collapses to a harmless identifier ("XGetStatic") instead of injecting
    /// C#. A leading digit (illegal to start an identifier) is prefixed with '_'.
    /// Returns "_" when the input has no usable characters.
    /// </summary>
    public static string ToPascalCase(string snakeCaseName)
    {
        if (string.IsNullOrEmpty(snakeCaseName))
            return snakeCaseName;

        var sb = new System.Text.StringBuilder(snakeCaseName.Length);
        bool startOfWord = true;
        foreach (char c in snakeCaseName)
        {
            bool isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool isDigit = c >= '0' && c <= '9';
            if (isLetter)
            {
                sb.Append(startOfWord ? char.ToUpperInvariant(c) : c);
                startOfWord = false;
            }
            else if (isDigit)
            {
                sb.Append(c);
                startOfWord = false;
            }
            else
            {
                // separator (underscore, space, punctuation): next letter starts a word
                startOfWord = true;
            }
        }

        if (sb.Length == 0)
            return "_";
        if (sb[0] >= '0' && sb[0] <= '9')
            sb.Insert(0, '_');
        return sb.ToString();
    }

    /// <summary>
    /// Column-aware mapping: rowversion concurrency tokens are byte[]? (the
    /// value is database-assigned and unknown until first read) regardless
    /// of the reported db type. SQL Server reports rowversion as data type
    /// 'timestamp', which would otherwise map to System.DateTime.
    /// </summary>
    public static string MapColumnToCSharp(JauntyQ.Schema.ColumnSchema column) =>
        column.IsRowVersion ? "byte[]?" : MapDbTypeToCSharp(column.DbType, column.IsNullable);

    public static string MapDbTypeToCSharp(string dbType, bool isNullable)
    {
        string csharpType = NormalizeDbType(dbType.ToLowerInvariant()) switch
        {
            "int" or "int4" or "integer" or "serial" => isNullable ? "int?" : "int",
            "bigint" or "int8" or "bigserial" => isNullable ? "long?" : "long",
            "smallint" or "int2" or "tinyint" => isNullable ? "short?" : "short",
            "varchar" or "text" or "nvarchar" or "ntext" or "character varying"
                or "char" or "nchar" or "character" or "citext" => isNullable ? "string?" : "string",
            "bool" or "boolean" or "bit" => isNullable ? "bool?" : "bool",
            "decimal" or "numeric" or "money" or "smallmoney" => isNullable ? "decimal?" : "decimal",
            "float" or "double precision" or "float8" => isNullable ? "double?" : "double",
            "real" or "float4" => isNullable ? "float?" : "float",
            "datetime" or "timestamp" or "datetime2" or "date"
                or "timestamp without time zone" or "timestamp with time zone"
                or "smalldatetime" => isNullable ? "System.DateTime?" : "System.DateTime",
            "time" or "time without time zone" => isNullable ? "System.TimeSpan?" : "System.TimeSpan",
            "uniqueidentifier" or "uuid" => isNullable ? "System.Guid?" : "System.Guid",
            "bytea" or "varbinary" or "binary" or "image" => isNullable ? "byte[]?" : "byte[]",
            _ => "object"
        };

        return csharpType;
    }

    private static string NormalizeDbType(string dbType)
    {
        // Strip length/precision specifiers: varchar(255) -> varchar, decimal(10,2) -> decimal
        int parenIndex = dbType.IndexOf('(');
        return parenIndex >= 0 ? dbType.Substring(0, parenIndex).Trim() : dbType.Trim();
    }
}
