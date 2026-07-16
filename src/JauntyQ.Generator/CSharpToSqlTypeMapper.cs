namespace JauntyQ.Generator;

/// <summary>
/// Maps C# type names to T-SQL type names for CREATE PROCEDURE parameter lists.
/// Inverse of <see cref="DialectMapper.MapDbTypeToCSharp"/>.
/// </summary>
public static class CSharpToSqlTypeMapper
{
    public static string Map(string csharpType)
    {
        // Strip nullable suffix — SQL NULLability is separate
        var baseType = csharpType.TrimEnd('?');

        return baseType switch
        {
            "int"             => "int",
            "long"            => "bigint",
            "short"           => "smallint",
            "byte"            => "tinyint",
            "bool"            => "bit",
            "decimal"         => "decimal(18,2)",
            "double"          => "float",
            "float"           => "real",
            "string"          => "nvarchar(MAX)",
            "System.DateTime" => "datetime2",
            "System.DateTimeOffset" => "datetimeoffset",
            "System.TimeSpan" => "time",
            "System.Guid"     => "uniqueidentifier",
            "byte[]"          => "varbinary(MAX)",
            _                 => "sql_variant"
        };
    }
}
