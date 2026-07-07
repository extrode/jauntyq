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
            "bool" => $"reader.GetBoolean({ordinal})",
            "decimal" => $"reader.GetDecimal({ordinal})",
            "double" => $"reader.GetDouble({ordinal})",
            "float" => $"reader.GetFloat({ordinal})",
            "string" => $"reader.GetString({ordinal})",
            "System.DateTime" => $"reader.GetDateTime({ordinal})",
            "System.Guid" => $"reader.GetGuid({ordinal})",
            "byte[]" => $"(byte[])reader.GetValue({ordinal})",
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
        var paramRef = query.Parameters.FirstOrDefault(p => p.Name == paramName);
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
            if (tableName != null &&
                schema.Tables.TryGetValue(tableName, out var tableSchema) &&
                tableSchema.Columns.TryGetValue(columnName, out var colSchema))
            {
                return DialectMapper.MapColumnToCSharp(colSchema);
            }
        }
        else
        {
            // Unqualified — search all referenced tables
            foreach (var table in query.Tables)
            {
                if (schema.Tables.TryGetValue(table.TableName, out var tableSchema) &&
                    tableSchema.Columns.TryGetValue(columnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema);
                }
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
