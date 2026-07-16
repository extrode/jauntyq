using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitColumnAssignments(System.Text.StringBuilder sb, ProjectionModel projection, string indent)
    {
        for (int i = 0; i < projection.Columns.Count; i++)
        {
            var col = projection.Columns[i];
            string readerCall = GetReaderCall(col.Type, col.Ordinal);
            string comma = i < projection.Columns.Count - 1 ? "," : "";
            sb.AppendLine($"{indent}{IdentifierGuard.Escape(col.Name)} = {readerCall}{comma}");
        }
    }

    private static string GetReaderCall(string csharpType, int ordinal)
    {
        // Handle nullable types
        bool isNullable = csharpType.EndsWith("?");
        string baseType = isNullable ? csharpType.TrimEnd('?') : csharpType;

        string getMethod = baseType switch
        {
            "int" => $"reader.GetInt32({ordinal})",
            "long" => $"reader.GetInt64({ordinal})",
            "short" => $"reader.GetInt16({ordinal})",
            "byte" => $"reader.GetByte({ordinal})",
            "bool" => $"reader.GetBoolean({ordinal})",
            "decimal" => $"reader.GetDecimal({ordinal})",
            "double" => $"reader.GetDouble({ordinal})",
            "float" => $"reader.GetFloat({ordinal})",
            "string" => $"reader.GetString({ordinal})",
            "System.DateTime" => $"reader.GetDateTime({ordinal})",
            "System.Guid" => $"reader.GetGuid({ordinal})",
            // TimeSpan (Postgres "time"), IPAddress ("inet"/"cidr"), and every
            // array type (Postgres "int[]", "text[]", "uuid[]", ...) fell
            // through to the bare reader.GetValue(ordinal) below, which is
            // typed `object` -- an implicit object-to-T assignment at the
            // property initializer is CS0266. Confirmed live against Npgsql
            // that GetValue already returns the exact target runtime type in
            // every case (System.Net.IPAddress, System.TimeSpan, int[],
            // string[], Guid[]), so an explicit cast (matching the existing
            // "byte[]" case just below) is all that's missing.
            "System.TimeSpan" => $"(System.TimeSpan)reader.GetValue({ordinal})",
            "System.Net.IPAddress" => $"(System.Net.IPAddress)reader.GetValue({ordinal})",
            // DialectMapper maps SQL Server "datetimeoffset" and Postgres "time
            // with time zone" to System.DateTimeOffset (task #26), which has no
            // dedicated IDataReader.Get* method either -- same CS0266 hazard as
            // TimeSpan/IPAddress above if left as bare GetValue.
            "System.DateTimeOffset" => $"(System.DateTimeOffset)reader.GetValue({ordinal})",
            "byte[]" => $"(byte[])reader.GetValue({ordinal})",
            _ when baseType.EndsWith("[]") => $"({baseType})reader.GetValue({ordinal})",
            _ => $"reader.GetValue({ordinal})"
        };

        if (isNullable)
        {
            // Bare `default` here infers the conditional's natural type from the getMethod arm
            // (a non-nullable value type), yielding e.g. DateTime.MinValue instead of null for
            // NULL columns. Carry the full nullable type explicitly so the null arm is truly null.
            return $"reader.IsDBNull({ordinal}) ? default({csharpType}) : {getMethod}";
        }

        // String is a reference type — always needs null check
        if (csharpType == "string")
        {
            return $"reader.IsDBNull({ordinal}) ? null! : {getMethod}";
        }

        return getMethod;
    }

    public static string InferParameterType(string paramName, QueryModel query, ProjectionModel projection, DatabaseSchema? schema = null, Directives.DirectiveModel? directives = null)
    {
        // 0. Check @params directive first (a full, unwrapped override)
        if (directives?.ExplicitParams != null)
        {
            foreach (var ep in directives.ExplicitParams)
            {
                if (string.Equals(ep.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return ep.CSharpType;
            }
        }

        string elementType = InferScalarParameterType(paramName, query, projection, schema);

        bool isEach = directives?.EachParams != null &&
            directives.EachParams.Exists(n => string.Equals(n, paramName, StringComparison.OrdinalIgnoreCase));

        return isEach ? $"System.Collections.Generic.IReadOnlyList<{elementType}>" : elementType;
    }

    private static string InferScalarParameterType(string paramName, QueryModel query, ProjectionModel projection, DatabaseSchema? schema)
    {
        // 1. Try binding-based inference (col = @param parsed by ExtractParameterBindings)
        var paramRef = query.Parameters.Find(p => p.Name == paramName);
        if (paramRef != null && !string.IsNullOrEmpty(paramRef.BoundColumnName) && schema != null)
        {
            var resolved = ResolveColumnType(paramRef.BoundTableAlias, paramRef.BoundColumnName, query, schema);
            if (resolved != null)
                return resolved;
        }

        // 2. Fallback: match parameter name to a projection column name
        foreach (var col in projection.Columns)
        {
            if (col.Name.Equals(DialectMapper.ToPascalCase(paramName), StringComparison.OrdinalIgnoreCase))
            {
                return col.Type;
            }
        }

        return "object";
    }

    /// <summary>
    /// Extracts T back out of the "System.Collections.Generic.IReadOnlyList&lt;T&gt;"
    /// shape InferParameterType wraps -- @each params in. Safe only because that
    /// wrapping is the sole producer of this exact string shape.
    /// </summary>
    private static string GetEachElementType(string csharpType)
    {
        const string prefix = "System.Collections.Generic.IReadOnlyList<";
        return csharpType.Substring(prefix.Length, csharpType.Length - prefix.Length - 1);
    }

    private static string? ResolveColumnType(string tableAlias, string columnName, QueryModel query, DatabaseSchema schema)
    {
        if (!string.IsNullOrEmpty(tableAlias))
        {
            // Qualified — resolve alias to table name
            string? tableName = ResolveAlias(tableAlias, query);
            if (tableName != null)
            {
                if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema) &&
                    SchemaLookup.TryGetColumn(tableSchema!, columnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema!, schema.Dialect);
                }
                // Not a schema table: the qualifier may name a CTE whose
                // virtual column traces back to a real one.
                var qualifiedViaCte = ProjectionBuilder.ResolveThroughCtes(tableName, columnName, query.Ctes, schema, depth: 0);
                if (qualifiedViaCte != null)
                    return DialectMapper.MapColumnToCSharp(qualifiedViaCte, schema.Dialect);
            }
        }
        else
        {
            // Unqualified — search all referenced tables
            foreach (var table in query.Tables)
            {
                if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema) &&
                    SchemaLookup.TryGetColumn(tableSchema!, columnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema!, schema.Dialect);
                }
            }

            // Not in any schema table: it may be a CTE's virtual column
            // (declared column list or aliased output).
            foreach (var table in query.Tables)
            {
                var viaCte = ProjectionBuilder.ResolveThroughCtes(table.TableName, columnName, query.Ctes, schema, depth: 0);
                if (viaCte != null)
                    return DialectMapper.MapColumnToCSharp(viaCte, schema.Dialect);
            }
        }

        return null;
    }

    private static string? ResolveAlias(string alias, QueryModel query)
    {
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            if (string.Equals(key, alias, StringComparison.OrdinalIgnoreCase))
                return table.TableName;
        }
        return null;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string? ResolveProcName(Directives.DirectiveModel? directives, string entityName, string methodName)
    {
        if (directives == null || !directives.IsProc)
            return null;
        return directives.ProcName ?? $"{entityName}_{methodName}";
    }

    /// <summary>
    /// Strips leading blank lines and leading whole-line `--` comments so
    /// commented-out old queries at the top of a .sql file are not embedded
    /// into every generated CommandText. Mid-query comments are preserved.
    /// </summary>
    internal static string StripLeadingSqlComments(string sql)
    {
        var lines = sql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int start = 0;
        while (start < lines.Length)
        {
            var trimmed = lines[start].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("--"))
            {
                start++;
                continue;
            }
            break;
        }
        return string.Join("\n", lines, start, lines.Length - start).Trim();
    }

}
