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
    /// Client-side value guards, emitted before the connection opens.
    /// Null guards first: a null passed for a non-nullable reference parameter
    /// (possible from non-NRT callers) would otherwise surface as an NRE deep
    /// in the length guard / Size expression, or reach the provider as an
    /// unset parameter value — fail fast with the parameter's name instead.
    /// Then length guards for write parameters: fails fast with the exact
    /// column and limit instead of a server round-trip ending in a truncation
    /// error; also what makes the fixed DbParameter.Size safe (ADO.NET
    /// providers silently truncate oversize values to Size).
    /// </summary>
    private static void EmitValueGuards(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos)
    {
        bool any = false;
        foreach (var param in paramInfos)
        {
            // -- @each lists (IReadOnlyList<T>) and non-nullable string/byte[]
            // params dereference below (.Count/.Length) — null-guard them.
            if (!param.IsEach && param.CSharpType is not ("string" or "byte[]"))
                continue;
            sb.AppendLine($"            if ({param.Name} is null)");
            sb.AppendLine($"                throw new System.ArgumentNullException(nameof({param.Name}));");
            any = true;
        }
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
    private static void EmitParameterSizing(System.Text.StringBuilder sb, EmittedParam param, string varName) =>
        EmitParameterSizing(sb, param.CSharpType, param.MaxLength, param.Precision, param.Scale, param.IsWriteTarget, varName, param.Name);

    /// <summary>
    /// Same DbParameter.Size / Precision / Scale sizing, but for a value
    /// expression that is not simply "the parameter itself" — namely one
    /// element of a -- @each list (<see cref="EmitEachParameterBinding"/>),
    /// whose value expression is "list[i]", not the list parameter's own
    /// name, and whose emitted lines sit one nesting level deeper (inside
    /// the each-loop's own for-block). -- @each params are always comparison
    /// (IN-list) params, never write targets, so the dynamic-sizing branch
    /// is the only one reachable from that caller; the write-target branch
    /// exists for the plain single-value overload above.
    /// </summary>
    private static void EmitParameterSizing(
        System.Text.StringBuilder sb, string csharpType, int? maxLength, int? precision, int? scale,
        bool isWriteTarget, string varName, string valueExpr, string indent = "                ")
    {
        bool isText = csharpType is "string" or "string?";
        bool isBinary = csharpType is "byte[]" or "byte[]?";
        if ((isText || isBinary) && maxLength is int max)
        {
            if (max < 0)
            {
                sb.AppendLine($"{indent}{varName}.Size = -1;");
            }
            else if (isWriteTarget)
            {
                sb.AppendLine($"{indent}{varName}.Size = {max};");
            }
            else
            {
                string sizeExpr = csharpType is "string" or "byte[]"
                    ? $"{valueExpr}.Length > {max} ? {valueExpr}.Length : {max}"
                    : $"{valueExpr} == null ? {max} : ({valueExpr}.Length > {max} ? {valueExpr}.Length : {max})";
                sb.AppendLine($"{indent}{varName}.Size = {sizeExpr};");
            }
        }

        if (csharpType is "decimal" or "decimal?" && precision is int prec && prec > 0)
        {
            sb.AppendLine($"{indent}{varName}.Precision = {prec};");
            sb.AppendLine($"{indent}{varName}.Scale = {scale ?? 0};");
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
