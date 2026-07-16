namespace JauntyQ.Analysis;

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
    public static string MapColumnToCSharp(JauntyQ.Schema.ColumnSchema column, string? dialect = null) =>
        column.IsRowVersion ? "byte[]?" : MapDbTypeToCSharp(column.DbType, column.IsNullable, column.Precision ?? column.MaxLength, dialect);

    /// <summary>
    /// <paramref name="length"/> is the declared bit/char length for types
    /// where it changes the mapping — currently just <c>bit(n)</c>: MySQL and
    /// PostgreSQL both allow a multi-bit <c>BIT(n &gt; 1)</c> column (unlike SQL
    /// Server, where BIT is always a single bit), and neither MySqlConnector
    /// nor Npgsql returns that as a bool (ulong and BitArray respectively) — so
    /// mapping it to "bool" would silently corrupt/throw. Null means "unknown
    /// or not applicable", which preserves the old single-bit behavior for
    /// callers (identity/param types) that don't have column metadata handy.
    ///
    /// <paramref name="dialect"/> disambiguates the one dbType string that
    /// means genuinely different things on different engines: "tinyint" is
    /// SQL Server's unsigned single-byte type (0-255, provider storage type
    /// byte — Microsoft.Data.SqlClient's GetInt16 throws InvalidCastException
    /// on it) but MySQL's signed-by-default tinyint (-128..127, provider
    /// storage type sbyte/byte depending on UNSIGNED) round-trips fine through
    /// GetInt16 today. Only "sqlserver" gets the byte mapping; every other
    /// dialect (and null, for callers without schema/dialect in scope) keeps
    /// the historical short mapping.
    /// </summary>
    public static string MapDbTypeToCSharp(string dbType, bool isNullable, int? length = null, string? dialect = null)
    {
        bool isMySql = string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase);

        // MySQL's UNSIGNED modifier widens int/bigint/smallint's positive
        // range beyond what the equivalent signed CLR type holds (e.g. INT
        // UNSIGNED's max, 4294967295, overflows System.Int32) --
        // MySqlConnector reports these as System.UInt32/UInt64/UInt16 on the
        // wire, so the mapping must follow or any value above the signed max
        // fails at read time with zero build-time signal. Gated on the mysql
        // dialect specifically: "unsigned" is a MySQL-only wire concept, and
        // a SQLite/Postgres/SQL Server DbType string that happens to contain
        // the word (e.g. DDL ported verbatim from MySQL into SQLite, which
        // has no real UNSIGNED semantics) must keep degrading to "object" +
        // JNT2007 exactly as before — those providers never box a
        // uint/ulong/ushort for such a column, so routing them there the
        // same way MySQL is routed throws InvalidCastException at read time
        // instead of surfacing the (correct, diagnosed) unmapped-type gap.
        bool unsigned = isMySql && dbType.Contains("unsigned", StringComparison.OrdinalIgnoreCase);
        string normalized = unsigned
            ? NormalizeDbType(StripMySqlUnsignedModifier(dbType.ToLowerInvariant()))
            : NormalizeDbType(dbType.ToLowerInvariant());

        // Postgres array types (e.g. "text[]", "integer[]"): recurse on the
        // element type (as non-nullable — nullability describes the array
        // reference itself, not each element) and wrap in "[]".
        if (normalized.EndsWith("[]"))
        {
            string elementType = MapDbTypeToCSharp(normalized.Substring(0, normalized.Length - 2), isNullable: false, length, dialect);
            return isNullable ? $"{elementType}[]?" : $"{elementType}[]";
        }

        if (unsigned)
        {
            switch (normalized)
            {
                case "int": case "int4": case "integer": case "serial":
                    return isNullable ? "uint?" : "uint";
                case "bigint": case "int8": case "bigserial":
                    return isNullable ? "ulong?" : "ulong";
                case "smallint": case "int2":
                    return isNullable ? "ushort?" : "ushort";
                    // "tinyint unsigned" (0..255) and "mediumint unsigned"
                    // (0..16777215) both already fit their signed mapping's
                    // range (short, int respectively) — falls through to the
                    // main switch below using the already-unsigned-stripped
                    // `normalized`, so it still matches "tinyint"/"mediumint"
                    // there instead of missing and degrading to "object".
            }
        }

        bool isSqlServer = string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase);

        string csharpType = normalized switch
        {
            // "mediumint" (MySQL 24-bit int, -8388608..8388607) and "year"
            // (MySQL 1-4 digit year) both round-trip through MySqlConnector
            // as System.Int32 (confirmed live) -- int is a safe superset for
            // both.
            "int" or "int4" or "integer" or "serial" or "mediumint" or "year" => isNullable ? "int?" : "int",
            "bigint" or "int8" or "bigserial" => isNullable ? "long?" : "long",
            "tinyint" when isSqlServer => isNullable ? "byte?" : "byte",
            "smallint" or "int2" or "tinyint" => isNullable ? "short?" : "short",
            // "enum"/"set" (MySQL) materialize as System.String through
            // MySqlConnector (confirmed live) -- the member/value list itself
            // isn't captured by the schema extractor, so a C# enum can't be
            // synthesized; string is the safe, lossless representation.
            "varchar" or "text" or "nvarchar" or "ntext" or "character varying"
                or "char" or "nchar" or "character" or "citext"
                or "tinytext" or "mediumtext" or "longtext"
                or "enum" or "set" => isNullable ? "string?" : "string",
            "bool" or "boolean" => isNullable ? "bool?" : "bool",
            "bit" when length is null or <= 1 => isNullable ? "bool?" : "bool",
            "decimal" or "numeric" or "money" or "smallmoney" => isNullable ? "decimal?" : "decimal",
            "float" or "double precision" or "float8" or "double" => isNullable ? "double?" : "double",
            "real" or "float4" => isNullable ? "float?" : "float",
            "datetime" or "timestamp" or "datetime2" or "date"
                or "timestamp without time zone" or "timestamp with time zone"
                or "smalldatetime" => isNullable ? "System.DateTime?" : "System.DateTime",
            "time" or "time without time zone" => isNullable ? "System.TimeSpan?" : "System.TimeSpan",
            // SQL Server "datetimeoffset" and Postgres "time with time zone"
            // both carry a UTC offset the provider materializes as
            // System.DateTimeOffset (confirmed live: Microsoft.Data.SqlClient
            // and Npgsql respectively) -- mapping either to plain DateTime/
            // TimeSpan would silently drop the offset.
            "datetimeoffset" or "time with time zone" => isNullable ? "System.DateTimeOffset?" : "System.DateTimeOffset",
            "uniqueidentifier" or "uuid" => isNullable ? "System.Guid?" : "System.Guid",
            // "blob" (MySQL's default-size BLOB) plus the tiny/medium/long
            // variants all materialize as System.Byte[] (confirmed live).
            "bytea" or "varbinary" or "binary" or "image"
                or "blob" or "tinyblob" or "mediumblob" or "longblob" => isNullable ? "byte[]?" : "byte[]",
            "json" or "jsonb" => isNullable ? "string?" : "string",
            "inet" or "cidr" => isNullable ? "System.Net.IPAddress?" : "System.Net.IPAddress",
            // Every other branch above appends "?" for a nullable column; the
            // fallback must too. Without it, a nullable column of an
            // unrecognized db type (e.g. SQL Server "hierarchyid", reachable
            // live via AdventureWorksLite's HumanResources.Employee
            // .OrganizationNode) gets a non-annotated `object` property AND
            // CodeEmitter.Part9.GetReaderCall's own `isNullable` check (which
            // keys off the type string ending in "?") sees no "?" and skips
            // the `reader.IsDBNull(...)` guard entirely -- so a genuine SQL
            // NULL comes through as the raw `System.DBNull.Value` sentinel
            // instead of C# `null`, silently breaking the same
            // null-means-no-value contract every other nullable column
            // honors. JNT2007 only warns (it doesn't block), so nothing else
            // catches this.
            _ => isNullable ? "object?" : "object"
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
    public static bool IsUnmappedDbType(string dbType, bool isNullable, int? length = null, string? dialect = null)
    {
        string normalized = NormalizeDbType(dbType.ToLowerInvariant());
        if (normalized.EndsWith("[]"))
            return IsUnmappedDbType(normalized.Substring(0, normalized.Length - 2), isNullable: false, length, dialect);

        // The fallback arm of MapDbTypeToCSharp returns "object?" (not
        // "object") when isNullable is true, so both must be checked here --
        // matching only "object" would silently stop reporting JNT2007 for
        // every nullable unmapped-type column.
        string mapped = MapDbTypeToCSharp(dbType, isNullable, length, dialect);
        return mapped == "object" || mapped == "object?";
    }

    /// <summary>
    /// True for the SQL Server CLR user-defined types (hierarchyid,
    /// geography, geometry) that fall through to the "object"/"object?"
    /// mapping like any other unmapped db type, but — unlike every other
    /// JNT2007 case — crash at RUNTIME rather than merely losing type
    /// fidelity: reading such a column through the generated fallback's
    /// <c>reader.GetValue(i)</c> call requires ADO.NET to materialize the
    /// CLR UDT via the <c>Microsoft.SqlServer.Types</c> assembly, which
    /// JauntyQ's zero-dependency/NativeAOT-compatible core never
    /// references. Confirmed live (round 14 audit) via AdventureWorksLite's
    /// HumanResources.Employee.OrganizationNode column:
    /// <c>System.IO.FileNotFoundException</c> on
    /// "Microsoft.SqlServer.Types, Version=10.0.0.0, ..." thrown from
    /// <c>SqlDataReader.GetValue</c>'s internal
    /// <c>CheckGetExtendedUDTInfo</c>. Used only to sharpen JNT2007's
    /// message text for sqlserver-dialect columns of these types — the
    /// underlying "object"/"object?" codegen is unchanged, since actually
    /// fixing the crash would mean either referencing
    /// <c>Microsoft.SqlServer.Types</c> (violates the zero-dependency/
    /// NativeAOT mandate) or teaching the reader-call codegen a
    /// column-type-specific safe read path (a larger redesign than a single
    /// diagnostic-message fix).
    /// </summary>
    public static bool IsKnownSqlServerClrUdtType(string dbType)
    {
        string normalized = NormalizeDbType(dbType.ToLowerInvariant());
        return normalized is "hierarchyid" or "geography" or "geometry";
    }

    private static string NormalizeDbType(string dbType)
    {
        // Strip length/precision specifiers: varchar(255) -> varchar, decimal(10,2) -> decimal
        int parenIndex = dbType.IndexOf('(');
        return parenIndex >= 0 ? dbType.Substring(0, parenIndex).Trim() : dbType.Trim();
    }

    /// <summary>
    /// Strips MySQL's "unsigned" (and any trailing "zerofill" that rides
    /// along with it, e.g. "int(10) unsigned zerofill") before the paren
    /// strip in <see cref="NormalizeDbType"/> — it's a modifier word, not
    /// part of the base type name, and (unlike the "(n)" facet) can appear
    /// with no parens at all ("int unsigned"). Must not use a blind
    /// first-space cut here: several base type names are themselves
    /// multi-word ("double precision", "character varying"). Deliberately
    /// separate from <see cref="NormalizeDbType"/> (used by every dialect)
    /// so it is only ever applied on the MySQL-only path in
    /// <see cref="MapDbTypeToCSharp"/> — a SQLite/Postgres/SQL Server column
    /// whose declared type happens to contain the word "unsigned" (e.g. DDL
    /// ported verbatim from MySQL into SQLite, which has no real UNSIGNED
    /// semantics) must keep falling through to the same "object" + JNT2007
    /// degradation it always has, not silently match a signed/unsigned CLR
    /// type meant for a wire format that engine doesn't use.
    /// </summary>
    private static string StripMySqlUnsignedModifier(string dbType)
    {
        int unsignedIndex = dbType.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase);
        return unsignedIndex >= 0 ? dbType.Substring(0, unsignedIndex).Trim() : dbType;
    }
}
