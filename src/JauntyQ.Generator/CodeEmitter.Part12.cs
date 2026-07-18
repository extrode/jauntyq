using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitBulkInsertBody(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync, string dialect, DatabaseSchema? schema = null)
    {
        string enumerableType = TypeRef(schema, "IEnumerable", "System.Collections.Generic");
        string taskType = TypeRef(schema, "Task", "System.Threading.Tasks");
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? $"{taskType}<int>" : "int";
        string name = isAsync ? "BulkInsertAsync" : "BulkInsert";
        string rowsParam = $"{enumerableType}<{rowType}> rows";
        // Same signature shape as the provider fast paths (BulkInsertSignature
        // in CodeEmitter.Part13.cs): statics take an optional caller-managed
        // DbTransaction; instance variants flow _db.CurrentTransaction.
        const string txParam = "DbTransaction? transaction = null";
        string paramList = isStatic
            ? (isAsync ? $"DbConnection conn, {rowsParam}, {txParam}, CancellationToken cancellationToken = default"
                       : $"DbConnection conn, {rowsParam}, {txParam}")
            : (isAsync ? $"{rowsParam}, CancellationToken cancellationToken = default" : rowsParam);

        string colList = JoinColumns(cols, ", ", c => c.Name);
        string valueList = JoinColumns(cols, ", ", c => $"@{c.Name}");
        string insertSql = $"INSERT INTO {tableName} ({colList}) VALUES ({valueList})";

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        sb.AppendLine($"            bool weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        // Reuse an ambient JauntyDb transaction if one is active; else open our
        // own so the whole batch commits atomically.
        if (!isStatic)
        {
            sb.AppendLine("                DbTransaction? tx = _db?.CurrentTransaction;");
            sb.AppendLine("                bool ownTx = tx == null;");
        }
        else
        {
            // A caller-supplied transaction is the caller's unit of work:
            // enlist in it and leave commit/rollback/dispose to the caller.
            sb.AppendLine("                DbTransaction? tx = transaction;");
            sb.AppendLine("                bool ownTx = tx == null;");
        }
        sb.AppendLine(isAsync
            ? $"                if (ownTx) tx = await {connVar}.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);"
            : $"                if (ownTx) tx = {connVar}.BeginTransaction();");
        sb.AppendLine("                int __count = 0;");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine($"                    using DbCommand cmd = {connVar}.CreateCommand();");
        sb.AppendLine("                    cmd.Transaction = tx;");
        sb.AppendLine($"                    cmd.CommandText = @\"{IndentSqlContinuationLines(EscapeVerbatimString(insertSql), 40)}\";");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c, dialect);
            sb.AppendLine($"                    DbParameter p{i} = cmd.CreateParameter();");
            sb.AppendLine($"                    p{i}.ParameterName = \"@{c.Name}\";");
            string? ado = MapCSharpTypeToAdoDbType(ct);
            if (ado != null)
                sb.AppendLine($"                    p{i}.DbType = DbType.{ado};");
            sb.AppendLine($"                    cmd.Parameters.Add(p{i});");
        }
        sb.AppendLine("                    foreach (var row in rows)");
        sb.AppendLine("                    {");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c, dialect);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            sb.AppendLine(IsNonNullableValueType(ct)
                ? $"                        p{i}.Value = row.{prop};"
                : $"                        p{i}.Value = (object?)row.{prop} ?? DBNull.Value;");
        }
        sb.AppendLine(isAsync
            ? "                        __count += await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);"
            : "                        __count += cmd.ExecuteNonQuery();");
        sb.AppendLine("                    }");
        sb.AppendLine(isAsync
            ? "                    if (ownTx && tx != null) await tx.CommitAsync(cancellationToken).ConfigureAwait(false);"
            : "                    if (ownTx && tx != null) tx.Commit();");
        sb.AppendLine("                    return __count;");
        sb.AppendLine("                }");
        sb.AppendLine("                finally");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (ownTx && tx != null) tx.Dispose();");
        sb.AppendLine("                }");
        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    private static string EscapeVerbatimString(string s)
    {
        return s.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Re-indents every line after the first in a verbatim-string SQL literal
    /// so continuation lines align under the opening quote instead of sitting
    /// at column 0 -- e.g. "delete from address\nwhere ..." becomes readable
    /// once the "where" clause lines up under "delete". Call after
    /// <see cref="EscapeVerbatimString"/>; <paramref name="column"/> is the
    /// number of characters preceding the SQL's first character on the
    /// CommandText assignment line (indent plus <c>cmd.CommandText = @"</c>).
    /// </summary>
    private static string IndentSqlContinuationLines(string escapedSql, int column)
    {
        return escapedSql.IndexOf('\n') < 0
            ? escapedSql
            : escapedSql.Replace("\n", "\n" + new string(' ', column));
    }
}
