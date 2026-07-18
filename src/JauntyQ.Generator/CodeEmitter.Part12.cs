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
        sb.AppendLine($"                    cmd.CommandText = @\"{EscapeVerbatimString(IndentSqlContinuationLines(insertSql, 40))}\";");
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
    /// Re-indents every line after the first in a SQL string so continuation
    /// lines align under the opening quote instead of sitting at column 0 --
    /// e.g. "delete from address\nwhere ..." becomes readable once the
    /// "where" clause lines up under "delete". <paramref name="column"/> is
    /// the number of characters preceding the SQL's first character on the
    /// CommandText assignment line (indent plus <c>cmd.CommandText = @"</c>).
    /// Call BEFORE <see cref="EscapeVerbatimString"/> (the two commute --
    /// escaping only doubles quotes, indenting only inserts spaces -- but
    /// this helper's quote tracking assumes raw, un-doubled text).
    /// The pad is inserted only after a newline that sits OUTSIDE any
    /// single-quoted string literal or quoted identifier ("..." / [...] /
    /// `...`): the emitted verbatim string's whitespace IS the runtime
    /// CommandText, so padding a newline inside a multi-line literal would
    /// silently change the value the database receives (AUD-R50-01).
    /// Newlines inside comments are padded as usual -- a line comment is
    /// terminated by the newline itself, and extra interior whitespace in a
    /// block comment is semantically inert.
    /// </summary>
    private static string IndentSqlContinuationLines(string sql, int column)
    {
        if (sql.IndexOf('\n') < 0)
            return sql;

        string pad = new string(' ', column);
        var sb = new System.Text.StringBuilder(sql.Length + 64);
        // Mirror of SqlTokenizer's quoting rules, reduced to the one question
        // this formatter needs: is this newline inside a literal/identifier?
        char delimiter = '\0'; // active '\'' / '"' / '`' / '[' opener, or \0
        bool inLineComment = false;
        bool inBlockComment = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            sb.Append(c);

            if (inLineComment)
            {
                if (c == '\n') { inLineComment = false; sb.Append(pad); }
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { sb.Append('/'); i++; inBlockComment = false; }
                else if (c == '\n') sb.Append(pad);
                continue;
            }
            if (delimiter != '\0')
            {
                char close = delimiter == '[' ? ']' : delimiter;
                if (c == close)
                {
                    // A doubled close char ('' / "" / `` / ]]) is an escaped
                    // occurrence inside the run, not a terminator.
                    if (i + 1 < sql.Length && sql[i + 1] == close) { sb.Append(close); i++; }
                    else delimiter = '\0';
                }
                continue;
            }

            switch (c)
            {
                case '\'':
                case '"':
                case '`':
                case '[':
                    delimiter = c;
                    break;
                case '-' when i + 1 < sql.Length && sql[i + 1] == '-':
                    sb.Append('-');
                    i++;
                    inLineComment = true;
                    break;
                case '/' when i + 1 < sql.Length && sql[i + 1] == '*':
                    sb.Append('*');
                    i++;
                    inBlockComment = true;
                    break;
                case '\n':
                    sb.Append(pad);
                    break;
            }
        }
        return sb.ToString();
    }
}
