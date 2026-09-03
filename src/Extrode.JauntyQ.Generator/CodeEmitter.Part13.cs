using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

public static partial class CodeEmitter
{
    // Provider-native bulk-insert fast paths. Each mirrors the portable
    // EmitBulkInsertBody (CodeEmitter.Part12.cs) method shape/signature exactly
    // — same name (BulkInsert / BulkInsertAsync), same instance/static overloads,
    // same __weOpened connection lifecycle and _db?.CurrentTransaction reuse — but
    // swaps the per-row ExecuteNonQuery loop for the provider's set-based copy
    // API. The generator never references Npgsql / Microsoft.Data.SqlClient /
    // MySqlConnector itself (it is netstandard2.0); it emits (collision-guarded,
    // see TypeRef) type names assuming the consumer's project references the
    // provider package, exactly as CodeEmitter.Part3.cs already does for
    // NpgsqlParameter<T>.

    private static (string Modifier, string AsyncModifier, string Ret, string Name, string ParamList)
        BulkInsertSignature(string rowType, bool isStatic, bool isAsync, DatabaseSchema? schema = null)
    {
        string taskType = TypeRef(schema, "Task", "System.Threading.Tasks");
        string enumerableType = TypeRef(schema, "IEnumerable", "System.Collections.Generic");
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? $"{taskType}<int>" : "int";
        string name = isAsync ? "BulkInsertAsync" : "BulkInsert";
        string rowsParam = $"{enumerableType}<{rowType}> rows";
        // Static variants take an optional trailing DbTransaction so the copy
        // can participate in a caller-managed unit of work (instance variants
        // flow the ambient _db.CurrentTransaction instead). On PostgreSQL the
        // binary COPY rides the connection's active transaction automatically,
        // so the parameter exists there for API uniformity.
        string txParam = "DbTransaction? transaction = null";
        string paramList = isStatic
            ? (isAsync ? $"DbConnection conn, {rowsParam}, {txParam}, CancellationToken cancellationToken = default"
                       : $"DbConnection conn, {rowsParam}, {txParam}")
            : (isAsync ? $"{rowsParam}, CancellationToken cancellationToken = default" : rowsParam);
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
        string connVar, bool isStatic, bool isAsync, string dialect, DatabaseSchema? schema = null)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync, schema);
        string npgsqlConnectionType = TypeRef(schema, "NpgsqlConnection", "Npgsql");
        string npgsqlBinaryImporterType = TypeRef(schema, "NpgsqlBinaryImporter", "Npgsql");
        string colList = JoinColumns(cols, ", ", c => c.Name);
        string copySql = $"COPY {tableName} ({colList}) FROM STDIN (FORMAT BINARY)";

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        // AUD-R69-01: "__weOpened", matching EmitFinallyClose's now-shared
        // literal across all 7 call sites (see CodeEmitter.Part4.cs). Same
        // structural-immunity note as CodeEmitter.Part12.cs's sibling: bulk
        // insert exposes no per-column formal parameters, so this rename is
        // purely for consistency with the shared helper.
        sb.AppendLine($"            bool __weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (__weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (__weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                var __npgsqlConn = ({npgsqlConnectionType}){connVar};");
        sb.AppendLine("                int __count = 0;");
        if (isAsync)
            sb.AppendLine($"                await using ({npgsqlBinaryImporterType} __importer = await __npgsqlConn.BeginBinaryImportAsync(@\"{EscapeVerbatimString(copySql)}\", cancellationToken).ConfigureAwait(false))");
        else
            sb.AppendLine($"                using ({npgsqlBinaryImporterType} __importer = __npgsqlConn.BeginBinaryImport(@\"{EscapeVerbatimString(copySql)}\"))");
        sb.AppendLine("                {");
        sb.AppendLine("                    foreach (var row in rows)");
        sb.AppendLine("                    {");
        sb.AppendLine(isAsync
            ? "                        await __importer.StartRowAsync(cancellationToken).ConfigureAwait(false);"
            : "                        __importer.StartRow();");
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            string ct = DialectMapper.MapColumnToCSharp(c, dialect, schema);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            // Spec 013 T12: binary COPY writes properties directly,
            // bypassing parameter binding entirely, so it needs its own
            // conversion. T0 probed live that the importer accepts an untyped
            // string for an enum column, so no exclusion is needed -- just the
            // wire value instead of the enum.
            string? copyEnumWire = EnumWireCall(ct, schema, $"row.{prop}");
            if (copyEnumWire != null)
            {
                if (ct.EndsWith("?"))
                {
                    string wireValue = EnumWireCall(ct, schema, $"row.{prop}.Value")!;
                    sb.AppendLine($"                        if (row.{prop} is null)");
                    sb.AppendLine(isAsync
                        ? "                            await __importer.WriteNullAsync(cancellationToken).ConfigureAwait(false);"
                        : "                            __importer.WriteNull();");
                    sb.AppendLine("                        else");
                    sb.AppendLine(isAsync
                        ? $"                            await __importer.WriteAsync({wireValue}, cancellationToken).ConfigureAwait(false);"
                        : $"                            __importer.Write({wireValue});");
                }
                else
                {
                    sb.AppendLine(isAsync
                        ? $"                        await __importer.WriteAsync({copyEnumWire}, cancellationToken).ConfigureAwait(false);"
                        : $"                        __importer.Write({copyEnumWire});");
                }
            }
            else if (IsNonNullableValueType(ct, schema))
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
                bool nullableValueType = ct.EndsWith("?") && IsNonNullableValueType(ct.Substring(0, ct.Length - 1), schema);
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
        string connVar, bool isStatic, bool isAsync, DatabaseSchema? schema = null)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync, schema);
        string readerType = $"__{rowType}BulkReader";
        string sqlTransactionType = TypeRef(schema, "SqlTransaction", "Microsoft.Data.SqlClient");
        string sqlBulkCopyType = TypeRef(schema, "SqlBulkCopy", "Microsoft.Data.SqlClient");
        string sqlConnectionType = TypeRef(schema, "SqlConnection", "Microsoft.Data.SqlClient");
        string sqlBulkCopyOptionsType = TypeRef(schema, "SqlBulkCopyOptions", "Microsoft.Data.SqlClient");

        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        // AUD-R69-01: "__weOpened", matching EmitFinallyClose's now-shared
        // literal across all 7 call sites (see CodeEmitter.Part4.cs). Same
        // structural-immunity note as CodeEmitter.Part12.cs's sibling: bulk
        // insert exposes no per-column formal parameters, so this rename is
        // purely for consistency with the shared helper.
        sb.AppendLine($"            bool __weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (__weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (__weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        // Reuse an ambient JauntyDb transaction if one is active so the copy
        // participates in the caller's unit of work.
        if (!isStatic)
            sb.AppendLine($"                var tx = ({sqlTransactionType}?)_db?.CurrentTransaction;");
        else
            sb.AppendLine($"                var tx = ({sqlTransactionType}?)transaction;");
        sb.AppendLine($"                using var __reader = new {readerType}(rows);");
        sb.AppendLine($"                using (var __bulkCopy = new {sqlBulkCopyType}(({sqlConnectionType}){connVar}, {sqlBulkCopyOptionsType}.Default, tx))");
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
        string connVar, bool isStatic, bool isAsync, DatabaseSchema? schema = null)
    {
        var (modifier, asyncModifier, ret, name, paramList) = BulkInsertSignature(rowType, isStatic, isAsync, schema);
        string readerType = $"__{rowType}BulkReader";
        string listType = TypeRef(schema, "List", "System.Collections.Generic");
        // AUD-R50-03 (residual): these BCL names appear as bare literals in the
        // SHOW WARNINGS check below; a colliding entity/row-POCO name (e.g.
        // "converts", "string_comparisons") would shadow them namespace-wide.
        string convertType = TypeRef(schema, "Convert", "System");
        string stringComparisonType = TypeRef(schema, "StringComparison", "System");
        string invalidOpEx = TypeRef(schema, "InvalidOperationException", "System");
        string mySqlTransactionType = TypeRef(schema, "MySqlTransaction", "MySqlConnector");
        string mySqlBulkCopyType = TypeRef(schema, "MySqlBulkCopy", "MySqlConnector");
        string mySqlConnectionType = TypeRef(schema, "MySqlConnection", "MySqlConnector");
        string mySqlBulkCopyColumnMappingType = TypeRef(schema, "MySqlBulkCopyColumnMapping", "MySqlConnector");

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Bulk-inserts <paramref name=\"rows\"/> via MySqlConnector.MySqlBulkCopy.");
        sb.AppendLine("        /// <para><b>Requires</b> the connection string to include");
        sb.AppendLine("        /// <c>AllowLoadLocalInfile=true</c> (JauntyQ cannot set this for you); without");
        sb.AppendLine("        /// it MySqlBulkCopy fails at runtime. MySqlConnector documents this API as");
        sb.AppendLine("        /// experimental and subject to change.</para>");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        {modifier}{asyncModifier} {ret} {name}({paramList})");
        sb.AppendLine("        {");
        // AUD-R69-01: "__weOpened", matching EmitFinallyClose's now-shared
        // literal across all 7 call sites (see CodeEmitter.Part4.cs). Same
        // structural-immunity note as CodeEmitter.Part12.cs's sibling: bulk
        // insert exposes no per-column formal parameters, so this rename is
        // purely for consistency with the shared helper.
        sb.AppendLine($"            bool __weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (__weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (__weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        if (!isStatic)
            sb.AppendLine($"                var tx = ({mySqlTransactionType}?)_db?.CurrentTransaction;");
        else
            sb.AppendLine($"                var tx = ({mySqlTransactionType}?)transaction;");
        sb.AppendLine($"                using var __reader = new {readerType}(rows);");
        sb.AppendLine($"                var __bulkCopy = new {mySqlBulkCopyType}(({mySqlConnectionType}){connVar}, tx);");
        sb.AppendLine($"                __bulkCopy.DestinationTableName = \"{IdentifierGuard.ToStringLiteral(tableName)}\";");
        // Map each source ordinal to its destination column by name. Without
        // this MySqlBulkCopy maps positionally against the *full* table
        // (including the excluded identity column), which would misalign every
        // value by one column.
        for (int i = 0; i < cols.Count; i++)
            sb.AppendLine($"                __bulkCopy.ColumnMappings.Add(new {mySqlBulkCopyColumnMappingType}({i}, \"{IdentifierGuard.ToStringLiteral(cols[i].Name)}\"));");
        sb.AppendLine(isAsync
            ? "                await __bulkCopy.WriteToServerAsync(__reader, cancellationToken).ConfigureAwait(false);"
            : "                __bulkCopy.WriteToServer(__reader);");
        // MySqlBulkCopy issues LOAD DATA ... IGNORE INTO TABLE ..., and MySQL's
        // IGNORE downgrades data-integrity violations (e.g. NULL supplied to a
        // NOT NULL column) from a hard error to a session warning, silently
        // substituting the column's implicit default instead of rejecting the
        // row. WriteToServer reports success regardless, so a batch can finish
        // with corrupted data and no signal that anything went wrong -- unlike
        // the SqlServer/Postgres fast paths (the server itself rejects such
        // rows) or the portable ExecuteNonQuery fallback (a real ADO.NET NULL
        // parameter trips the constraint as a genuine error). Check the
        // session's own warning list immediately after the copy and throw if
        // it holds anything at Warning level, so a silently-altered load can't
        // masquerade as a clean one.
        sb.AppendLine("                using (DbCommand __warnCmd = " + connVar + ".CreateCommand())");
        sb.AppendLine("                {");
        sb.AppendLine("                    if (tx != null) __warnCmd.Transaction = tx;");
        sb.AppendLine("                    __warnCmd.CommandText = \"SHOW WARNINGS\";");
        sb.AppendLine(isAsync
            ? "                    using DbDataReader __warnRdr = await __warnCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);"
            : "                    using DbDataReader __warnRdr = __warnCmd.ExecuteReader();");
        sb.AppendLine($"                    var __warnMsgs = new {listType}<string>();");
        sb.AppendLine(isAsync
            ? "                    while (await __warnRdr.ReadAsync(cancellationToken).ConfigureAwait(false))"
            : "                    while (__warnRdr.Read())");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        if (string.Equals({convertType}.ToString(__warnRdr.GetValue(0)), \"Warning\", {stringComparisonType}.OrdinalIgnoreCase))");
        sb.AppendLine($"                            __warnMsgs.Add({convertType}.ToString(__warnRdr.GetValue(2)) ?? string.Empty);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    if (__warnMsgs.Count > 0)");
        sb.AppendLine($"                        throw new {invalidOpEx}(\"MySqlBulkCopy completed but the server reported \" + __warnMsgs.Count + \" warning(s); LOAD DATA's IGNORE semantics silently coerce constraint violations (e.g. NULL into a NOT NULL column) to a default value instead of failing the row: \" + string.Join(\"; \", __warnMsgs));");
        sb.AppendLine("                }");
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
        System.Collections.Generic.List<ColumnSchema> cols, string dialect, DatabaseSchema? schema = null)
    {
        string readerType = $"__{rowType}BulkReader";
        string enumeratorType = TypeRef(schema, "IEnumerator", "System.Collections.Generic");
        string enumerableType = TypeRef(schema, "IEnumerable", "System.Collections.Generic");
        string typeType = TypeRef(schema, "Type", "System");
        // AUD-R50-03 (residual): further BCL/ADO.NET names emitted bare in the
        // adapter body below — each is shadowed namespace-wide by a colliding
        // entity/row-POCO name (e.g. "maths", "arrays", "db_enumerators").
        string indexOutOfRangeEx = TypeRef(schema, "IndexOutOfRangeException", "System");
        string mathType = TypeRef(schema, "Math", "System");
        string arrayType = TypeRef(schema, "Array", "System");
        string stringComparisonType = TypeRef(schema, "StringComparison", "System");
        string dbEnumeratorType = TypeRef(schema, "DbEnumerator", "System.Data.Common");

        sb.AppendLine($"        private sealed class {readerType} : DbDataReader");
        sb.AppendLine("        {");
        sb.AppendLine($"            private readonly {enumeratorType}<{rowType}> _enumerator;");
        sb.AppendLine($"            private {rowType} _current = default!;");
        sb.AppendLine("            public int RowsRead { get; private set; }");
        sb.AppendLine($"            public {readerType}({enumerableType}<{rowType}> rows)");
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
            string ct = DialectMapper.MapColumnToCSharp(c, dialect, schema);
            string prop = IdentifierGuard.Escape(DialectMapper.ToPascalCase(c.Name));
            // Spec 013 T12: GetValue, GetFieldType and GetString must agree.
            // GetString below is (string)GetValue, so handing back the wire
            // value here is what makes all three consistent -- an adapter that
            // reported typeof(OrderStatus) while returning a string would give
            // MySqlBulkCopy contradictory metadata.
            string? adapterEnumWire = EnumWireCall(ct, schema, $"_current.{prop}");
            if (adapterEnumWire != null)
                sb.AppendLine(ct.EndsWith("?")
                    ? $"                    case {i}: return _current.{prop} is null ? (object)DBNull.Value : {EnumWireCall(ct, schema, $"_current.{prop}.Value")!};"
                    : $"                    case {i}: return {adapterEnumWire};");
            else if (IsNonNullableValueType(ct, schema))
                sb.AppendLine($"                    case {i}: return _current.{prop};");
            else
                sb.AppendLine($"                    case {i}: return (object?)_current.{prop} ?? DBNull.Value;");
        }
        sb.AppendLine($"                    default: throw new {indexOutOfRangeEx}(ordinal.ToString());");
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
        sb.AppendLine($"                    default: throw new {indexOutOfRangeEx}(ordinal.ToString());");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        // GetOrdinal: reverse switch on name (ordinal-based comparison).
        sb.AppendLine("            public override int GetOrdinal(string name)");
        sb.AppendLine("            {");
        for (int i = 0; i < cols.Count; i++)
            sb.AppendLine($"                if (string.Equals(name, \"{IdentifierGuard.ToStringLiteral(cols[i].Name)}\", {stringComparisonType}.Ordinal)) return {i};");
        sb.AppendLine($"                throw new {indexOutOfRangeEx}(name);");
        sb.AppendLine("            }");
        sb.AppendLine();
        // GetFieldType
        sb.AppendLine($"            public override {typeType} GetFieldType(int ordinal)");
        sb.AppendLine("            {");
        sb.AppendLine("                switch (ordinal)");
        sb.AppendLine("                {");
        for (int i = 0; i < cols.Count; i++)
        {
            string ct = ShortenValueTypeName(schema, DialectMapper.MapColumnToCSharp(cols[i], dialect, schema));
            string baseType = ct.EndsWith("?") ? ct.Substring(0, ct.Length - 1) : ct;
            // Spec 013: an enum column is handed to the provider as its wire
            // string (GetValue above), so the declared field type must say so.
            if (IsEnumParameterType(baseType, schema))
                baseType = "string";
            sb.AppendLine($"                    case {i}: return typeof({baseType});");
        }
        sb.AppendLine($"                    default: throw new {indexOutOfRangeEx}(ordinal.ToString());");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;");
        sb.AppendLine();
        sb.AppendLine("            public override object this[int ordinal] => GetValue(ordinal);");
        sb.AppendLine("            public override object this[string name] => GetValue(GetOrdinal(name));");
        sb.AppendLine();
        // Typed getters: unbox from GetValue.
        sb.AppendLine("            public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);");
        sb.AppendLine("            public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);");
        sb.AppendLine("            public override char GetChar(int ordinal) => (char)GetValue(ordinal);");
        sb.AppendLine("            public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);");
        sb.AppendLine("            public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);");
        sb.AppendLine("            public override double GetDouble(int ordinal) => (double)GetValue(ordinal);");
        sb.AppendLine("            public override float GetFloat(int ordinal) => (float)GetValue(ordinal);");
        sb.AppendLine("            public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);");
        sb.AppendLine("            public override short GetInt16(int ordinal) => (short)GetValue(ordinal);");
        sb.AppendLine("            public override int GetInt32(int ordinal) => (int)GetValue(ordinal);");
        sb.AppendLine("            public override long GetInt64(int ordinal) => (long)GetValue(ordinal);");
        sb.AppendLine("            public override string GetString(int ordinal) => (string)GetValue(ordinal);");
        sb.AppendLine($"            public override {typeType} GetProviderSpecificFieldType(int ordinal) => GetFieldType(ordinal);");
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
        sb.AppendLine($"                int toCopy = (int){mathType}.Min(length, available);");
        sb.AppendLine($"                {arrayType}.Copy(data, (int)dataOffset, buffer, bufferOffset, toCopy);");
        sb.AppendLine("                return toCopy;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)");
        sb.AppendLine("            {");
        sb.AppendLine("                string data = GetString(ordinal);");
        sb.AppendLine("                if (buffer == null) return data.Length;");
        sb.AppendLine("                long available = data.Length - dataOffset;");
        sb.AppendLine("                if (available <= 0) return 0;");
        sb.AppendLine($"                int toCopy = (int){mathType}.Min(length, available);");
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
        sb.AppendLine($"                new {dbEnumeratorType}(this, closeReader: false);");
        sb.AppendLine("            protected override void Dispose(bool disposing)");
        sb.AppendLine("            {");
        sb.AppendLine("                if (disposing) _enumerator.Dispose();");
        sb.AppendLine("                base.Dispose(disposing);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }
}
