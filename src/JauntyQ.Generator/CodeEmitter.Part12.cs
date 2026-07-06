using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitBulkInsertBody(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? "System.Threading.Tasks.Task<int>" : "int";
        string name = isAsync ? "BulkInsertAsync" : "BulkInsert";
        string rowsParam = $"System.Collections.Generic.IEnumerable<{rowType}> rows";
        string paramList = isStatic
            ? (isAsync ? $"System.Data.Common.DbConnection conn, {rowsParam}, System.Threading.CancellationToken cancellationToken = default"
                       : $"System.Data.Common.DbConnection conn, {rowsParam}")
            : (isAsync ? $"{rowsParam}, System.Threading.CancellationToken cancellationToken = default" : rowsParam);

        string colList = string.Join(", ", cols.Select(c => c.Name));
        string valueList = string.Join(", ", cols.Select(c => $"@{c.Name}"));
        string insertSql = $"insert into {tableName} ({colList}) values ({valueList})";

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        // Reuse an ambient JauntyDb transaction if one is active; else open our
        // own so the whole batch commits atomically.
        if (!isStatic)
        {
            sb.AppendLine("                System.Data.Common.DbTransaction? tx = _db?.CurrentTransaction;");
            sb.AppendLine("                bool ownTx = tx == null;");
        }
        else
        {
            sb.AppendLine("                System.Data.Common.DbTransaction? tx = null;");
            sb.AppendLine("                bool ownTx = true;");
        }
        sb.AppendLine(isAsync
            ? $"                if (ownTx) tx = await {connVar}.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);"
            : $"                if (ownTx) tx = {connVar}.BeginTransaction();");
        sb.AppendLine("                int __count = 0;");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine($"                    using var cmd = {connVar}.CreateCommand();");
        sb.AppendLine("                    cmd.Transaction = tx;");
        sb.AppendLine($"                    cmd.CommandText = @\"{EscapeVerbatimString(insertSql)}\";");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c);
            sb.AppendLine($"                    var p{i} = cmd.CreateParameter();");
            sb.AppendLine($"                    p{i}.ParameterName = \"@{c.Name}\";");
            string? ado = MapCSharpTypeToAdoDbType(ct);
            if (ado != null)
                sb.AppendLine($"                    p{i}.DbType = System.Data.DbType.{ado};");
            sb.AppendLine($"                    cmd.Parameters.Add(p{i});");
        }
        sb.AppendLine("                    foreach (var row in rows)");
        sb.AppendLine("                    {");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            sb.AppendLine(IsNonNullableValueType(ct)
                ? $"                        p{i}.Value = row.{prop};"
                : $"                        p{i}.Value = (object?)row.{prop} ?? System.DBNull.Value;");
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
}
