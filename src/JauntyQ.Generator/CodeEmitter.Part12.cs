using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitBulkInsertBody(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync, string dialect)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? "System.Threading.Tasks.Task<int>" : "int";
        string name = isAsync ? "BulkInsertAsync" : "BulkInsert";
        string rowsParam = $"System.Collections.Generic.IEnumerable<{rowType}> rows";
        // Same signature shape as the provider fast paths (BulkInsertSignature
        // in CodeEmitter.Part13.cs): statics take an optional caller-managed
        // DbTransaction; instance variants flow _db.CurrentTransaction.
        const string txParam = "System.Data.Common.DbTransaction? transaction = null";
        string paramList = isStatic
            ? (isAsync ? $"System.Data.Common.DbConnection conn, {rowsParam}, {txParam}, System.Threading.CancellationToken cancellationToken = default"
                       : $"System.Data.Common.DbConnection conn, {rowsParam}, {txParam}")
            : (isAsync ? $"{rowsParam}, System.Threading.CancellationToken cancellationToken = default" : rowsParam);

        string colList = JoinColumns(cols, ", ", c => c.Name);
        string valueList = JoinColumns(cols, ", ", c => $"@{c.Name}");
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
            // A caller-supplied transaction is the caller's unit of work:
            // enlist in it and leave commit/rollback/dispose to the caller.
            sb.AppendLine("                System.Data.Common.DbTransaction? tx = transaction;");
            sb.AppendLine("                bool ownTx = tx == null;");
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
            string ct = DialectMapper.MapColumnToCSharp(c, dialect);
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
            string ct = DialectMapper.MapColumnToCSharp(c, dialect);
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
