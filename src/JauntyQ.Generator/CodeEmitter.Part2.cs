using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    /// <summary>
    /// Resolves the schema column a parameter is bound to (explicit
    /// alias-qualification first, then the CRUD target table, then a unique
    /// unqualified match). An explicit qualifier (e.g. <c>e.outcome</c> in an
    /// INSERT...SELECT's source WHERE clause) must win over the CRUD target
    /// table even when the target table happens to have a same-named column —
    /// otherwise a write-target column silently shadows the column the SQL
    /// actually references.
    /// </summary>
    private static ColumnSchema? ResolveBoundColumn(ParameterRef param, QueryModel query, DatabaseSchema? schema, out string? tableName)
    {
        tableName = null;
        if (schema == null || string.IsNullOrEmpty(param.BoundColumnName))
            return null;

        if (!string.IsNullOrEmpty(param.BoundTableAlias))
        {
            string? resolved = ResolveAlias(param.BoundTableAlias, query);
            if (resolved != null &&
                schema.Tables.TryGetValue(resolved, out var aliased) &&
                aliased.Columns.TryGetValue(param.BoundColumnName, out var aliasedCol))
            {
                tableName = resolved;
                return aliasedCol;
            }
            return null;
        }

        if (query.TargetTable != null &&
            schema.Tables.TryGetValue(query.TargetTable, out var target) &&
            target.Columns.TryGetValue(param.BoundColumnName, out var targetCol))
        {
            tableName = query.TargetTable;
            return targetCol;
        }

        foreach (var table in query.Tables)
        {
            if (schema.Tables.TryGetValue(table.TableName, out var tableSchema) &&
                tableSchema.Columns.TryGetValue(param.BoundColumnName, out var col))
            {
                tableName = table.TableName;
                return col;
            }
        }
        return null;
    }

    private static EmittedParam CreateEmittedParam(string name, string csharpType, bool isNullable,
        ColumnSchema? column, string? tableName, bool isWriteTarget, bool isEach = false)
    {
        if (column == null)
            return new EmittedParam(name, csharpType, isNullable, isEach: isEach);
        return new EmittedParam(name, csharpType, isNullable,
            column.MaxLength, column.Precision, column.Scale, isWriteTarget,
            tableName != null ? $"{tableName}.{column.Name}" : column.Name, isEach);
    }

    /// <summary>
    /// Client-side length guards for write parameters. Fails fast with the
    /// exact column and limit instead of a server round-trip ending in a
    /// truncation error; also what makes the fixed DbParameter.Size safe
    /// (ADO.NET providers silently truncate oversize values to Size).
    /// </summary>
    private static void EmitValueGuards(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos)
    {
        bool any = false;
        foreach (var param in paramInfos)
        {
            if (!param.IsWriteTarget || param.MaxLength is not int max || max <= 0)
                continue;
            bool isText = param.CSharpType is "string" or "string?";
            bool isBinary = param.CSharpType is "byte[]" or "byte[]?";
            if (!isText && !isBinary)
                continue;
            string unit = isText ? "characters" : "bytes";
            string condition = param.CSharpType is "string" or "byte[]"
                ? $"{param.Name}.Length > {max}"
                : $"{param.Name} != null && {param.Name}.Length > {max}";
            sb.AppendLine($"            if ({condition})");
            sb.AppendLine($"                throw new System.ArgumentException($\"Value ({{{param.Name}.Length}} {unit}) exceeds {param.ColumnDisplay} max length ({max}).\", nameof({param.Name}));");
            any = true;
        }
        if (any)
            sb.AppendLine();
    }

    /// <summary>
    /// DbParameter.Size / Precision / Scale from the schema snapshot.
    /// Fixed Size keeps the server plan cache to one plan per query instead
    /// of one per distinct value length. Write parameters are guarded above,
    /// so the fixed Size can never truncate; comparison parameters size
    /// dynamically because a truncated key could match the wrong row.
    /// </summary>
    private static void EmitParameterSizing(System.Text.StringBuilder sb, EmittedParam param, string varName)
    {
        bool isText = param.CSharpType is "string" or "string?";
        bool isBinary = param.CSharpType is "byte[]" or "byte[]?";
        if ((isText || isBinary) && param.MaxLength is int max)
        {
            if (max < 0)
            {
                sb.AppendLine($"                {varName}.Size = -1;");
            }
            else if (param.IsWriteTarget)
            {
                sb.AppendLine($"                {varName}.Size = {max};");
            }
            else
            {
                string sizeExpr = param.CSharpType is "string" or "byte[]"
                    ? $"{param.Name}.Length > {max} ? {param.Name}.Length : {max}"
                    : $"{param.Name} == null ? {max} : ({param.Name}.Length > {max} ? {param.Name}.Length : {max})";
                sb.AppendLine($"                {varName}.Size = {sizeExpr};");
            }
        }

        if (param.CSharpType is "decimal" or "decimal?" && param.Precision is int precision && precision > 0)
        {
            sb.AppendLine($"                {varName}.Precision = {precision};");
            sb.AppendLine($"                {varName}.Scale = {param.Scale ?? 0};");
        }
    }

    private static void EmitColumnNames(System.Text.StringBuilder sb, string methodName, ProjectionModel projection)
    {
        sb.Append($"        private static readonly string[] __{methodName}Columns = {{ ");
        for (int i = 0; i < projection.Columns.Count; i++)
        {
            var col = projection.Columns[i];
            string sourceName = string.IsNullOrEmpty(col.SourceName) ? col.Name : col.SourceName;
            if (i > 0) sb.Append(", ");
            // JNT2004 (C3): sourceName comes from a SQL alias/column and is
            // embedded in a regular string literal; encode it so a quote or
            // backslash cannot break out of the literal.
            sb.Append($"\"{IdentifierGuard.ToStringLiteral(sourceName)}\"");
        }
        sb.AppendLine(" };");
        // One-time shape-guard latch (0 = not yet validated). The guard runs on
        // the first execution of this query per process and never again, so the
        // per-row-free drift check is also per-query-once, as documented.
        sb.AppendLine($"        private static int __{methodName}Validated;");
    }

}
