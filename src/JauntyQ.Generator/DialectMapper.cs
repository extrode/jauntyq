namespace JauntyQ.Generator;

public static class DialectMapper
{
    public static string ToPascalCase(string snakeCaseName)
    {
        if (string.IsNullOrEmpty(snakeCaseName))
            return snakeCaseName;

        var parts = snakeCaseName.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
        }
        return string.Join("", parts);
    }

    public static string MapDbTypeToCSharp(string dbType, bool isNullable)
    {
        string csharpType = NormalizeDbType(dbType.ToLowerInvariant()) switch
        {
            "int" or "int4" or "integer" or "serial" => isNullable ? "int?" : "int",
            "bigint" or "int8" or "bigserial" => isNullable ? "long?" : "long",
            "smallint" or "int2" or "tinyint" => isNullable ? "short?" : "short",
            "varchar" or "text" or "nvarchar" or "ntext" or "character varying"
                or "char" or "nchar" or "character" => "string",
            "bool" or "boolean" or "bit" => isNullable ? "bool?" : "bool",
            "decimal" or "numeric" or "money" or "smallmoney" => isNullable ? "decimal?" : "decimal",
            "float" or "double precision" or "float8" => isNullable ? "double?" : "double",
            "real" or "float4" => isNullable ? "float?" : "float",
            "datetime" or "timestamp" or "datetime2" or "date"
                or "timestamp without time zone" or "timestamp with time zone"
                or "smalldatetime" => isNullable ? "System.DateTime?" : "System.DateTime",
            "time" or "time without time zone" => isNullable ? "System.TimeSpan?" : "System.TimeSpan",
            "uniqueidentifier" or "uuid" => isNullable ? "System.Guid?" : "System.Guid",
            "bytea" or "varbinary" or "binary" or "image" => "byte[]",
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
