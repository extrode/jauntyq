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
        string normalized = NormalizeDbType(dbType.ToLowerInvariant());

        // Postgres array types (e.g. "text[]", "integer[]"): recurse on the
        // element type (as non-nullable — nullability describes the array
        // reference itself, not each element) and wrap in "[]".
        if (normalized.EndsWith("[]"))
        {
            string elementType = MapDbTypeToCSharp(normalized.Substring(0, normalized.Length - 2), isNullable: false);
            return isNullable ? $"{elementType}[]?" : $"{elementType}[]";
        }

        string csharpType = normalized switch
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
            "json" or "jsonb" => isNullable ? "string?" : "string",
            "inet" or "cidr" => isNullable ? "System.Net.IPAddress?" : "System.Net.IPAddress",
            _ => "object"
        };

        return csharpType;
    }

    /// <summary>
    /// The dialect strings every dialect-specific switch in the generator
    /// (identity-insert SQL, upsert synthesis, etc.) actually has a case
    /// for. MariaDB is intentionally absent: it is wire/SQL-compatible with
    /// MySQL for everything the generator emits, so a MariaDB schema
    /// snapshot should declare <c>"dialect": "mysql"</c> rather than
    /// getting its own case.
    /// </summary>
    private static readonly HashSet<string> KnownDialects = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlserver", "postgres", "sqlite", "mysql"
    };

    /// <summary>
    /// True when <paramref name="dialect"/> is one of the strings the
    /// generator's dialect-specific switches actually recognize — used to
    /// surface JNT7003 instead of silently no-oping (or emitting SQL for
    /// the wrong dialect) on a snapshot with a typo'd or unsupported
    /// dialect string.
    /// </summary>
    public static bool IsKnownDialect(string dialect) => KnownDialects.Contains(dialect);

    /// <summary>
    /// True when <see cref="MapDbTypeToCSharp"/> would fall through to the
    /// degraded "object" mapping for this db type — used to surface JNT2007
    /// instead of leaving the type-loss silent.
    /// </summary>
    public static bool IsUnmappedDbType(string dbType, bool isNullable)
    {
        string normalized = NormalizeDbType(dbType.ToLowerInvariant());
        if (normalized.EndsWith("[]"))
            return IsUnmappedDbType(normalized.Substring(0, normalized.Length - 2), isNullable: false);

        return MapDbTypeToCSharp(dbType, isNullable) == "object";
    }

    private static string NormalizeDbType(string dbType)
    {
        // Strip length/precision specifiers: varchar(255) -> varchar, decimal(10,2) -> decimal
        int parenIndex = dbType.IndexOf('(');
        return parenIndex >= 0 ? dbType.Substring(0, parenIndex).Trim() : dbType.Trim();
    }
}
