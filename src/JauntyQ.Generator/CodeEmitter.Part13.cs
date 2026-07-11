using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    // Provider-native bulk-insert fast paths. Each mirrors the portable
    // EmitBulkInsertBody (CodeEmitter.Part12.cs) method shape/signature exactly
    // — same name (BulkInsert / BulkInsertAsync), same instance/static overloads,
    // same weOpened connection lifecycle and _db?.CurrentTransaction reuse — but
    // swaps the per-row ExecuteNonQuery loop for the provider's set-based copy
    // API. The generator never references Npgsql / Microsoft.Data.SqlClient /
    // MySqlConnector itself (it is netstandard2.0); it emits global::-qualified
    // type names assuming the consumer's project references the provider package,
    // exactly as CodeEmitter.Part3.cs already does for NpgsqlParameter<T>.

    private static (string Modifier, string AsyncModifier, string Ret, string Name, string ParamList)
        BulkInsertSignature(string rowType, bool isStatic, bool isAsync)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? "System.Threading.Tasks.Task<int>" : "int";
        string name = isAsync ? "BulkInsertAsync" : "BulkInsert";
        string rowsParam = $"System.Collections.Generic.IEnumerable<{rowType}> rows";
        // Static variants take an optional trailing DbTransaction so the copy
        // can participate in a caller-managed unit of work (instance variants
        // flow the ambient _db.CurrentTransaction instead). On PostgreSQL the
        // binary COPY rides the connection's active transaction automatically,
        // so the parameter exists there for API uniformity.
        const string txParam = "System.Data.Common.DbTransaction? transaction = null";
        string paramList = isStatic
            ? (isAsync ? $"System.Data.Common.DbConnection conn, {rowsParam}, {txParam}, System.Threading.CancellationToken cancellationToken = default"
                       : $"System.Data.Common.DbConnection conn, {rowsParam}, {txParam}")
            : (isAsync ? $"{rowsParam}, System.Threading.CancellationToken cancellationToken = default" : rowsParam);
        return (modifier, asyncModifier, ret, name, paramList);
    }

    /// <summary>
    /// PostgreSQL fast path: Npgsql binary COPY. Writes each row's columns
    /// directly to an NpgsqlBinaryImporter in table-column order — no
    /// IDataReader adapter needed. Complete()/CompleteAsync() MUST be called or
    /// disposal rolls the whole batch back; the importer has no built-in row
    /// count so we track one ourselves.
    /// </summary>
    private static void EmitBulkInsertBodyPostgres(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync);
        string colList = string.Join(", ", cols.Select(c => c.Name));
        string copySql = $"COPY {tableName} ({colList}) FROM STDIN (FORMAT BINARY)";

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                var __npgsqlConn = (global::Npgsql.NpgsqlConnection){connVar};");
        sb.AppendLine("                int __count = 0;");
        if (isAsync)
            sb.AppendLine($"                await using (var __importer = await __npgsqlConn.BeginBinaryImportAsync(@\"{EscapeVerbatimString(copySql)}\", cancellationToken).ConfigureAwait(false))");
        else
            sb.AppendLine($"                using (var __importer = __npgsqlConn.BeginBinaryImport(@\"{EscapeVerbatimString(copySql)}\"))");
        sb.AppendLine("                {");
        sb.AppendLine("                    foreach (var row in rows)");
        sb.AppendLine("                    {");
        sb.AppendLine(isAsync
            ? "                        await __importer.StartRowAsync(cancellationToken).ConfigureAwait(false);"
            : "                        __importer.StartRow();");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            if (IsNonNullableValueType(ct))
            {
                sb.AppendLine(isAsync
                    ? $"                        await __importer.WriteAsync(row.{prop}, cancellationToken).ConfigureAwait(false);"
                    : $"                        __importer.Write(row.{prop});");
            }
            else
            {
                // Reference/nullable columns: WriteNull when null (the importer's
                // Write<T>(null) would otherwise not resolve a wire type). For a
                // nullable value type, unwrap to the underlying value so the
                // importer's generic Write<T> sees the concrete T (e.g. int),
                // not Nullable<int>.
                bool nullableValueType = ct.EndsWith("?") && IsNonNullableValueType(ct.Substring(0, ct.Length - 1));
                string writeExpr = nullableValueType ? $"row.{prop}.Value" : $"row.{prop}";
                sb.AppendLine($"                        if (row.{prop} is null)");
                sb.AppendLine(isAsync
                    ? "                            await __importer.WriteNullAsync(cancellationToken).ConfigureAwait(false);"
                    : "                            __importer.WriteNull();");
                sb.AppendLine("                        else");
                sb.AppendLine(isAsync
                    ? $"                            await __importer.WriteAsync({writeExpr}, cancellationToken).ConfigureAwait(false);"
                    : $"                            __importer.Write({writeExpr});");
            }
        }
        sb.AppendLine("                        __count++;");
        sb.AppendLine("                    }");
        sb.AppendLine(isAsync
            ? "                    await __importer.CompleteAsync(cancellationToken).ConfigureAwait(false);"
            : "                    __importer.Complete();");
        sb.AppendLine("                }");
        sb.AppendLine("                return __count;");
        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// SQL Server fast path: Microsoft.Data.SqlClient.SqlBulkCopy over the shared
    /// AOT-safe DbDataReader adapter. SqlBulkCopy returns no row count, so it is
    /// read back from the adapter's RowsRead counter after WriteToServer.
    /// </summary>
    private static void EmitBulkInsertBodySqlServer(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync);
        string readerType = $"__{rowType}BulkReader";

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        // Reuse an ambient JauntyDb transaction if one is active so the copy
        // participates in the caller's unit of work.
        if (!isStatic)
            sb.AppendLine("                var tx = (global::Microsoft.Data.SqlClient.SqlTransaction?)_db?.CurrentTransaction;");
        else
            sb.AppendLine("                var tx = (global::Microsoft.Data.SqlClient.SqlTransaction?)transaction;");
        sb.AppendLine($"                using var __reader = new {readerType}(rows);");
        sb.AppendLine($"                using (var __bulkCopy = new global::Microsoft.Data.SqlClient.SqlBulkCopy((global::Microsoft.Data.SqlClient.SqlConnection){connVar}, global::Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default, tx))");
        sb.AppendLine("                {");
        sb.AppendLine($"                    __bulkCopy.DestinationTableName = \"{IdentifierGuard.ToStringLiteral(tableName)}\";");
        foreach (var c in cols)
            sb.AppendLine($"                    __bulkCopy.ColumnMappings.Add(\"{IdentifierGuard.ToStringLiteral(c.Name)}\", \"{IdentifierGuard.ToStringLiteral(c.Name)}\");");
        sb.AppendLine(isAsync
            ? "                    await __bulkCopy.WriteToServerAsync(__reader, cancellationToken).ConfigureAwait(false);"
            : "                    __bulkCopy.WriteToServer(__reader);");
        sb.AppendLine("                }");
        sb.AppendLine("                return __reader.RowsRead;");
        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// MySQL fast path: MySqlConnector.MySqlBulkCopy over the shared AOT-safe
    /// DbDataReader adapter. We read the row count back from the adapter's
    /// RowsRead counter rather than MySqlBulkCopyResult, whose member name could
    /// not be verified from the local package source (see PR notes). Requires the
    /// connection string to include <c>AllowLoadLocalInfile=true</c> — see the
    /// method's XML doc; forgetting it fails at runtime, not build time.
    /// </summary>
    private static void EmitBulkInsertBodyMySql(
        System.Text.StringBuilder sb, string rowType, string tableName,
        System.Collections.Generic.List<ColumnSchema> cols,
        string connVar, bool isStatic, bool isAsync)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync);
        string readerType = $"__{rowType}BulkReader";

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Bulk-inserts <paramref name=\"rows\"/> via MySqlConnector.MySqlBulkCopy.");
        sb.AppendLine("        /// <para><b>Requires</b> the connection string to include");
        sb.AppendLine("        /// <c>AllowLoadLocalInfile=true</c> (JauntyQ cannot set this for you); without");
        sb.AppendLine("        /// it MySqlBulkCopy fails at runtime. MySqlConnector documents this API as");
        sb.AppendLine("        /// experimental and subject to change.</para>");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        if (!isStatic)
            sb.AppendLine("                var tx = (global::MySqlConnector.MySqlTransaction?)_db?.CurrentTransaction;");
        else
            sb.AppendLine("                var tx = (global::MySqlConnector.MySqlTransaction?)transaction;");
        sb.AppendLine($"                using var __reader = new {readerType}(rows);");
        sb.AppendLine($"                var __bulkCopy = new global::MySqlConnector.MySqlBulkCopy((global::MySqlConnector.MySqlConnection){connVar}, tx);");
        sb.AppendLine($"                __bulkCopy.DestinationTableName = \"{IdentifierGuard.ToStringLiteral(tableName)}\";");
        // Map each source ordinal to its destination column by name. Without
        // this MySqlBulkCopy maps positionally against the *full* table
        // (including the excluded identity column), which would misalign every
        // value by one column.
        for (int i = 0; i < cols.Count; i++)
            sb.AppendLine($"                __bulkCopy.ColumnMappings.Add(new global::MySqlConnector.MySqlBulkCopyColumnMapping({i}, \"{IdentifierGuard.ToStringLiteral(cols[i].Name)}\"));");
        sb.AppendLine(isAsync
            ? "                await __bulkCopy.WriteToServerAsync(__reader, cancellationToken).ConfigureAwait(false);"
            : "                __bulkCopy.WriteToServer(__reader);");
        sb.AppendLine("                return __reader.RowsRead;");
        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Emits a private nested sealed DbDataReader that streams an
    /// IEnumerable&lt;Row&gt; column-by-column with a switch on the int ordinal
    /// (and a reverse switch on name in GetOrdinal). Reflection-free and
    /// allocation-light so it is AOT/trimming safe. Shared by the SqlBulkCopy and
    /// MySqlBulkCopy fast paths; RowsRead is exposed so those paths can return a
    /// row count (SqlBulkCopy provides none).
    /// </summary>
    private static void EmitBulkReaderAdapter(
        System.Text.StringBuilder sb, string rowType,
        System.Collections.Generic.List<ColumnSchema> cols)
    {
        string readerType = $"__{rowType}BulkReader";

        sb.AppendLine($"        private sealed class {readerType} : System.Data.Common.DbDataReader");
        sb.AppendLine("        {");
        sb.AppendLine($"            private readonly System.Collections.Generic.IEnumerator<{rowType}> _enumerator;");
        sb.AppendLine($"            private {rowType} _current = default!;");
        sb.AppendLine("            public int RowsRead { get; private set; }");
        sb.AppendLine($"            public {readerType}(System.Collections.Generic.IEnumerable<{rowType}> rows)");
        sb.AppendLine("            {");
        sb.AppendLine("                _enumerator = rows.GetEnumerator();");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override bool Read()");
        sb.AppendLine("            {");
        sb.AppendLine("                if (!_enumerator.MoveNext()) return false;");
        sb.AppendLine("                _current = _enumerator.Current;");
        sb.AppendLine("                RowsRead++;");
        sb.AppendLine("                return true;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine($"            public override int FieldCount => {cols.Count};");
        sb.AppendLine();
        // GetValue: switch on ordinal; null values surface as DBNull.
        sb.AppendLine("            public override object GetValue(int ordinal)");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (ordinal)");
        sb.AppendLine("                {");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            if (IsNonNullableValueType(ct))
                sb.AppendLine($"                    case {i}: return _current.{prop};");
            else
                sb.AppendLine($"                    case {i}: return (object?)_current.{prop} ?? System.DBNull.Value;");
        }
        sb.AppendLine("                    default: throw new System.IndexOutOfRangeException(ordinal.ToString());");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        // GetName
        sb.AppendLine("            public override string GetName(int ordinal)");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (ordinal)");
        sb.AppendLine("                {");
        for (int i = 0; i < cols.Count; i++)
            sb.AppendLine($"                    case {i}: return \"{IdentifierGuard.ToStringLiteral(cols[i].Name)}\";");
        sb.AppendLine("                    default: throw new System.IndexOutOfRangeException(ordinal.ToString());");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        // GetOrdinal: reverse switch on name (ordinal-based comparison).
        sb.AppendLine("            public override int GetOrdinal(string name)");
        sb.AppendLine("            {");
        for (int i = 0; i < cols.Count; i++)
            sb.AppendLine($"                if (string.Equals(name, \"{IdentifierGuard.ToStringLiteral(cols[i].Name)}\", System.StringComparison.Ordinal)) return {i};");
        sb.AppendLine("                throw new System.IndexOutOfRangeException(name);");
        sb.AppendLine("            }");
        sb.AppendLine();
        // GetFieldType
        sb.AppendLine("            public override System.Type GetFieldType(int ordinal)");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (ordinal)");
        sb.AppendLine("                {");
        for (int i = 0; i < cols.Count; i++)
        {
            string ct = DialectMapper.MapColumnToCSharp(cols[i]);
            string baseType = ct.EndsWith("?") ? ct.Substring(0, ct.Length - 1) : ct;
            sb.AppendLine($"                    case {i}: return typeof({baseType});");
        }
        sb.AppendLine("                    default: throw new System.IndexOutOfRangeException(ordinal.ToString());");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override bool IsDBNull(int ordinal) => GetValue(ordinal) is System.DBNull;");
        sb.AppendLine();
        sb.AppendLine("            public override object this[int ordinal] => GetValue(ordinal);");
        sb.AppendLine("            public override object this[string name] => GetValue(GetOrdinal(name));");
        sb.AppendLine();
        // Typed getters: unbox from GetValue.
        sb.AppendLine("            public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);");
        sb.AppendLine("            public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);");
        sb.AppendLine("            public override char GetChar(int ordinal) => (char)GetValue(ordinal);");
        sb.AppendLine("            public override System.DateTime GetDateTime(int ordinal) => (System.DateTime)GetValue(ordinal);");
        sb.AppendLine("            public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);");
        sb.AppendLine("            public override double GetDouble(int ordinal) => (double)GetValue(ordinal);");
        sb.AppendLine("            public override float GetFloat(int ordinal) => (float)GetValue(ordinal);");
        sb.AppendLine("            public override System.Guid GetGuid(int ordinal) => (System.Guid)GetValue(ordinal);");
        sb.AppendLine("            public override short GetInt16(int ordinal) => (short)GetValue(ordinal);");
        sb.AppendLine("            public override int GetInt32(int ordinal) => (int)GetValue(ordinal);");
        sb.AppendLine("            public override long GetInt64(int ordinal) => (long)GetValue(ordinal);");
        sb.AppendLine("            public override string GetString(int ordinal) => (string)GetValue(ordinal);");
        sb.AppendLine("            public override System.Type GetProviderSpecificFieldType(int ordinal) => GetFieldType(ordinal);");
        sb.AppendLine("            public override object GetProviderSpecificValue(int ordinal) => GetValue(ordinal);");
        sb.AppendLine();
        sb.AppendLine("            public override int GetValues(object[] values)");
        sb.AppendLine("            {");
        sb.AppendLine("                int n = values.Length < FieldCount ? values.Length : FieldCount;");
        sb.AppendLine("                for (int i = 0; i < n; i++) values[i] = GetValue(i);");
        sb.AppendLine("                return n;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)");
        sb.AppendLine("            {");
        sb.AppendLine("                var data = (byte[])GetValue(ordinal);");
        sb.AppendLine("                if (buffer == null) return data.Length;");
        sb.AppendLine("                long available = data.Length - dataOffset;");
        sb.AppendLine("                if (available <= 0) return 0;");
        sb.AppendLine("                int toCopy = (int)System.Math.Min(length, available);");
        sb.AppendLine("                System.Array.Copy(data, (int)dataOffset, buffer, bufferOffset, toCopy);");
        sb.AppendLine("                return toCopy;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)");
        sb.AppendLine("            {");
        sb.AppendLine("                var data = GetString(ordinal);");
        sb.AppendLine("                if (buffer == null) return data.Length;");
        sb.AppendLine("                long available = data.Length - dataOffset;");
        sb.AppendLine("                if (available <= 0) return 0;");
        sb.AppendLine("                int toCopy = (int)System.Math.Min(length, available);");
        sb.AppendLine("                data.CopyTo((int)dataOffset, buffer, bufferOffset, toCopy);");
        sb.AppendLine("                return toCopy;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;");
        sb.AppendLine();
        // Advisory / no-op members.
        sb.AppendLine("            public override int Depth => 0;");
        sb.AppendLine("            public override bool HasRows => true;");
        sb.AppendLine("            public override bool IsClosed => false;");
        sb.AppendLine("            public override int RecordsAffected => -1;");
        sb.AppendLine("            public override bool NextResult() => false;");
        sb.AppendLine("            public override System.Collections.IEnumerator GetEnumerator() =>");
        sb.AppendLine("                new System.Data.Common.DbEnumerator(this, closeReader: false);");
        sb.AppendLine("            protected override void Dispose(bool disposing)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (disposing) _enumerator.Dispose();");
        sb.AppendLine("                base.Dispose(disposing);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }
}
