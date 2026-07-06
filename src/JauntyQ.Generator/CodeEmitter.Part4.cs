using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    /// <summary>
    /// Resolves the single identity key column for an -- @identity INSERT.
    /// Returns null when preconditions are not met (the generator reports
    /// JNT7001 for user files before emission; synthetics only carry the
    /// directive when resolvable).
    /// </summary>
    internal static IdentityInfo? ResolveIdentityInfo(QueryModel query, DatabaseSchema? schema, Directives.DirectiveModel? directives)
    {
        if (directives?.ReturnsIdentity != true || schema == null || string.IsNullOrEmpty(schema.Dialect))
            return null;
        if (query.StatementType != StatementType.Insert || query.TargetTable == null)
            return null;
        if (!schema.Tables.TryGetValue(query.TargetTable, out var tableSchema))
            return null;

        ColumnSchema? identityCol = null;
        foreach (var col in tableSchema.Columns.Values)
        {
            if (!col.IsIdentity)
                continue;
            if (identityCol != null)
                return null; // multiple identity columns: unsupported
            identityCol = col;
        }
        if (identityCol == null)
            return null;

        string csharpType = DialectMapper.MapDbTypeToCSharp(identityCol.DbType, isNullable: false);
        return new IdentityInfo(identityCol.Name, csharpType, schema.Dialect);
    }

    /// <summary>
    /// Rewrites a plain INSERT so it returns the database-assigned identity:
    /// OUTPUT INSERTED.x (SQL Server, before VALUES), RETURNING x
    /// (PostgreSQL/SQLite), or a trailing SELECT LAST_INSERT_ID() (MySQL).
    /// </summary>
    internal static string BuildIdentityInsertSql(string sql, string dialect, string identityColumn)
    {
        string trimmed = sql.TrimEnd();
        while (trimmed.EndsWith(";"))
            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

        switch (dialect)
        {
            case "sqlserver":
            {
                int idx = FindValuesKeyword(trimmed);
                if (idx < 0)
                    return trimmed; // generator validated the shape; defensive only
                return trimmed.Substring(0, idx) + $"output inserted.{identityColumn}\n" + trimmed.Substring(idx);
            }
            case "postgres":
            case "sqlite":
                return trimmed + $"\nreturning {identityColumn}";
            case "mysql":
                return trimmed + ";\nselect last_insert_id()";
            default:
                return trimmed;
        }
    }

    private static int FindValuesKeyword(string sql)
    {
        for (int i = 0; i + 6 <= sql.Length; i++)
        {
            if (string.Compare(sql, i, "values", 0, 6, StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            bool startOk = i == 0 || !char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_' && sql[i - 1] != '@';
            bool endOk = i + 6 == sql.Length || !char.IsLetterOrDigit(sql[i + 6]) && sql[i + 6] != '_';
            if (startOk && endOk)
                return i;
        }
        return -1;
    }

    private static string GetIdentityReaderCall(IdentityInfo identity)
    {
        // MySQL's LAST_INSERT_ID() is BIGINT UNSIGNED regardless of the key
        // type; read as long and narrow explicitly.
        if (identity.Dialect == "mysql" && identity.CSharpType != "long")
            return $"checked(({identity.CSharpType})reader.GetInt64(0))";
        return GetReaderCall(identity.CSharpType, 0);
    }

    private static void EmitFinallyClose(System.Text.StringBuilder sb, string connVar, bool isAsync)
    {
        sb.AppendLine("            }");
        sb.AppendLine("            finally");
        sb.AppendLine("            {");
        sb.AppendLine(isAsync
            ? $"                if (weOpened) await {connVar}.CloseAsync().ConfigureAwait(false);"
            : $"                if (weOpened) {connVar}.Close();");
        sb.AppendLine("            }");
    }

    private static bool IsNonNullableValueType(string csharpType) => csharpType switch
    {
        "int" or "long" or "short" or "bool" or "decimal" or "double" or "float"
            or "System.DateTime" or "System.TimeSpan" or "System.Guid" => true,
        _ => false
    };

    /// <summary>
    /// Maps an emitted C# parameter type to System.Data.DbType so providers skip
    /// runtime type inference. Returns null when no static mapping exists.
    /// </summary>
    private static string? MapCSharpTypeToAdoDbType(string csharpType)
    {
        string baseType = csharpType.EndsWith("?") ? csharpType.Substring(0, csharpType.Length - 1) : csharpType;
        return baseType switch
        {
            "int" => "Int32",
            "long" => "Int64",
            "short" => "Int16",
            "bool" => "Boolean",
            "decimal" => "Decimal",
            "double" => "Double",
            "float" => "Single",
            "string" => "String",
            "System.DateTime" => "DateTime",
            "System.TimeSpan" => "Time",
            "System.Guid" => "Guid",
            "byte[]" => "Binary",
            _ => null
        };
    }

    private static bool IsBoundColumnNullable(ParameterRef param, QueryModel query, DatabaseSchema? schema)
    {
        if (schema == null || string.IsNullOrEmpty(param.BoundColumnName) || query.TargetTable == null)
            return false;
        return schema.Tables.TryGetValue(query.TargetTable, out var tableSchema)
            && tableSchema.Columns.TryGetValue(param.BoundColumnName, out var col)
            && col.IsNullable;
    }

    internal static string InferCrudParameterType(ParameterRef param, QueryModel query, DatabaseSchema? schema, Directives.DirectiveModel? directives = null)
    {
        // Check @params directive first
        if (directives?.ExplicitParams != null)
        {
            foreach (var ep in directives.ExplicitParams)
            {
                if (string.Equals(ep.Name, param.Name, StringComparison.OrdinalIgnoreCase))
                    return ep.CSharpType;
            }
        }

        if (!string.IsNullOrEmpty(param.BoundColumnName) && schema != null)
        {
            // For CRUD, the target table is the primary table
            string? targetTable = query.TargetTable;
            if (targetTable != null && schema.Tables.TryGetValue(targetTable, out var tableSchema))
            {
                if (tableSchema.Columns.TryGetValue(param.BoundColumnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema);
                }
            }

            // Fallback: try alias-based resolution
            var resolved = ResolveColumnType(param.BoundTableAlias, param.BoundColumnName, query, schema);
            if (resolved != null)
                return resolved;
        }

        return "object";
    }

}
