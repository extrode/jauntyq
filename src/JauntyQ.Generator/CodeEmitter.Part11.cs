using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitProcCallBody(
        System.Text.StringBuilder sb,
        string methodName,
        JauntyQ.Schema.ProcedureSchema procedure,
        string resultType,
        bool hasResults,
        string connVar,
        bool isStatic,
        bool isAsync,
        string? dialect)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        // Return: List<Result> for row-returning procs, else int (row count).
        string syncReturn = hasResults ? $"System.Collections.Generic.List<{resultType}>" : "int";
        string name = isAsync ? $"{methodName}Async" : methodName;

        // Async methods cannot declare ref/out parameters (CS1988) -- confirmed
        // via a live Roslyn compilation (not just ParseText, which accepts
        // ref/out on async and stays silent: this is a binding-time error, so
        // the codegen's own syntax-only self-check never caught it). When the
        // proc has OUT/INOUT params, the async overload instead omits OUT
        // params from the parameter list entirely (output-only, nothing for
        // the caller to pass in) and keeps INOUT as a plain input value; every
        // out-flowing value is returned in a tuple alongside the normal return
        // value, so nothing the sync overload exposes is lost.
        var outOrInOutParams = new System.Collections.Generic.List<JauntyQ.Schema.ProcedureParam>();
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out
                || p.Direction == JauntyQ.Schema.ProcedureParamDirection.InOut)
                outOrInOutParams.Add(p);
        }
        bool asyncReturnsTuple = isAsync && outOrInOutParams.Count > 0;

        string declaredReturn;
        if (asyncReturnsTuple)
        {
            var tupleParts = new System.Collections.Generic.List<string>
            {
                $"{syncReturn} {(hasResults ? "results" : "affected")}"
            };
            foreach (var p in outOrInOutParams)
            {
                string outCt = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
                string outPname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
                tupleParts.Add($"{outCt} {outPname}");
            }
            declaredReturn = $"System.Threading.Tasks.Task<({string.Join(", ", tupleParts)})>";
        }
        else
        {
            declaredReturn = isAsync ? $"System.Threading.Tasks.Task<{syncReturn}>" : syncReturn;
        }

        // Parameter list: IN params by value; OUT/INOUT params as C# `out`/`ref`
        // on the sync overload. Async drops OUT entirely and keeps INOUT as a
        // plain input value (see above).
        var parts = new System.Collections.Generic.List<string>();
        if (isStatic)
            parts.Add("System.Data.Common.DbConnection conn");
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out)
            {
                if (!isAsync)
                    parts.Add($"out {ct} {pname}");
            }
            else if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.InOut)
                parts.Add(isAsync ? $"{ct} {pname}" : $"ref {ct} {pname}");
            else
                parts.Add($"{ct} {pname}");
        }
        // Static variants take an optional trailing DbTransaction (see
        // HasStaticTransactionParam); skipped if a proc parameter maps to the
        // same C# name.
        bool hasTransactionParam = isStatic
            && !parts.Exists(p => p.EndsWith(" transaction", StringComparison.Ordinal));
        if (hasTransactionParam)
            parts.Add("System.Data.Common.DbTransaction? transaction = null");
        if (isAsync)
            parts.Add("System.Threading.CancellationToken cancellationToken = default");

        sb.AppendLine($"        {modifier}{asyncModifier} {declaredReturn} {name}({string.Join(", ", parts)})");
        sb.AppendLine("        {");

        // Pre-assign out params so the method is definitely-assigned on all
        // paths. Only the sync overload has them as real `out` parameters;
        // async declares a local at readback time instead (see below).
        if (!isAsync)
        {
            foreach (var p in procedure.Params)
            {
                if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out)
                {
                    string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
                    sb.AppendLine($"            {pname} = default;");
                }
            }
        }

        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                using var cmd = {connVar}.CreateCommand();");
        if (!isStatic)
            sb.AppendLine("                if (_db?.CurrentTransaction != null) cmd.Transaction = _db.CurrentTransaction;");
        else if (hasTransactionParam)
            sb.AppendLine("                if (transaction != null) cmd.Transaction = transaction;");
        sb.AppendLine($"                cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procedure.Name)}\";");
        sb.AppendLine("                cmd.CommandType = System.Data.CommandType.StoredProcedure;");

        // Bind parameters. Track OUT/INOUT parameter variable names for readback.
        var outReadback = new System.Collections.Generic.List<(string ParamVar, string CSharpName, string CSharpType, bool Nullable)>();
        int idx = 0;
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            string varName = $"p{idx++}";
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            sb.AppendLine($"                var {varName} = cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{IdentifierGuard.ToStringLiteral(p.Name)}\";");
            string? adoDbType = MapCSharpTypeToAdoDbType(ct);
            if (adoDbType != null)
                sb.AppendLine($"                {varName}.DbType = System.Data.DbType.{adoDbType};");
            switch (p.Direction)
            {
                case JauntyQ.Schema.ProcedureParamDirection.Out:
                    sb.AppendLine($"                {varName}.Direction = System.Data.ParameterDirection.Output;");
                    if (p.MaxLength is int ml && ml != 0)
                        sb.AppendLine($"                {varName}.Size = {ml};");
                    // Decimal/numeric OUT parameters: several providers need
                    // Precision/Scale set explicitly to correctly size the
                    // return value -- left unset, the returned value can be
                    // silently truncated or rounded instead of matching what
                    // the procedure actually assigned.
                    if (p.Precision is int prec)
                        sb.AppendLine($"                {varName}.Precision = {prec};");
                    if (p.Scale is int scl)
                        sb.AppendLine($"                {varName}.Scale = {scl};");
                    outReadback.Add((varName, pname, ct, p.IsNullable || !IsNonNullableValueType(ct)));
                    break;
                case JauntyQ.Schema.ProcedureParamDirection.InOut:
                    sb.AppendLine($"                {varName}.Direction = System.Data.ParameterDirection.InputOutput;");
                    if (p.MaxLength is int ml2 && ml2 != 0)
                        sb.AppendLine($"                {varName}.Size = {ml2};");
                    if (p.Precision is int prec2)
                        sb.AppendLine($"                {varName}.Precision = {prec2};");
                    if (p.Scale is int scl2)
                        sb.AppendLine($"                {varName}.Scale = {scl2};");
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? System.DBNull.Value;");
                    outReadback.Add((varName, pname, ct, p.IsNullable || !IsNonNullableValueType(ct)));
                    break;
                default:
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? System.DBNull.Value;");
                    break;
            }
            sb.AppendLine($"                cmd.Parameters.Add({varName});");
        }
        sb.AppendLine();

        if (hasResults)
        {
            // OUT/INOUT parameter values are populated by the provider only
            // once the reader is fully closed -- confirmed live against SQL
            // Server (Microsoft.Data.SqlClient): reading cmd.Parameters[i].Value
            // while a DbDataReader from the same command is still open (even
            // after every row has been read) returns DBNull/unset, not the
            // procedure's actual OUT value. The read/loop stays inside the
            // using block; readback and the return happen after it closes.
            string behavior = "System.Data.CommandBehavior.SingleResult";
            sb.AppendLine($"                var results = new System.Collections.Generic.List<{resultType}>();");
            sb.AppendLine(isAsync
                ? $"                using (var reader = await cmd.ExecuteReaderAsync({behavior}, cancellationToken).ConfigureAwait(false))"
                : $"                using (var reader = cmd.ExecuteReader({behavior}))");
            sb.AppendLine("                {");
            string readCall = isAsync ? "await reader.ReadAsync(cancellationToken).ConfigureAwait(false)" : "reader.Read()";
            sb.AppendLine($"                    while ({readCall})");
            sb.AppendLine($"                        results.Add(__Map{methodName}(reader));");
            sb.AppendLine("                }");
            if (asyncReturnsTuple)
            {
                var localNames = EmitProcOutReadback(sb, outReadback, "                ", declareLocals: true);
                sb.AppendLine($"                return (results, {string.Join(", ", localNames)});");
            }
            else
            {
                EmitProcOutReadback(sb, outReadback, "                ", declareLocals: false);
                sb.AppendLine("                return results;");
            }
        }
        else
        {
            sb.AppendLine(isAsync
                ? "                int __affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);"
                : "                int __affected = cmd.ExecuteNonQuery();");
            if (asyncReturnsTuple)
            {
                var localNames = EmitProcOutReadback(sb, outReadback, "                ", declareLocals: true);
                sb.AppendLine($"                return (__affected, {string.Join(", ", localNames)});");
            }
            else
            {
                EmitProcOutReadback(sb, outReadback, "                ", declareLocals: false);
                sb.AppendLine("                return __affected;");
            }
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Reads OUT/INOUT parameter values back. The sync overload assigns
    /// directly into its out/ref parameter (<paramref name="declareLocals"/>
    /// false). The async overload cannot declare ref/out parameters (CS1988),
    /// so it declares a local instead; the caller then folds these into the
    /// tuple return value. Returns the target variable names, in the same
    /// order as <paramref name="outParams"/>.
    /// </summary>
    private static System.Collections.Generic.List<string> EmitProcOutReadback(
        System.Text.StringBuilder sb,
        System.Collections.Generic.List<(string ParamVar, string CSharpName, string CSharpType, bool Nullable)> outParams,
        string indent,
        bool declareLocals)
    {
        var targetNames = new System.Collections.Generic.List<string>();
        foreach (var (paramVar, csName, csType, nullable) in outParams)
        {
            string baseType = csType.EndsWith("?") ? csType.Substring(0, csType.Length - 1) : csType;
            string target = declareLocals ? $"{csName}Out" : csName;
            string declKeyword = declareLocals ? $"{csType} " : "";
            // DBNull -> default; otherwise unbox to the declared type.
            sb.AppendLine($"{indent}{declKeyword}{target} = {paramVar}.Value is null || {paramVar}.Value is System.DBNull ? default! : ({csType})({baseType}){paramVar}.Value;");
            targetNames.Add(target);
        }
        return targetNames;
    }

    /// <summary>
    /// Emits BulkInsert(IEnumerable&lt;Row&gt;): inserts many rows atomically.
    /// The <paramref name="dialect"/> selects the emission strategy:
    /// <list type="bullet">
    /// <item>postgres: Npgsql binary COPY (NpgsqlBinaryImporter).</item>
    /// <item>sqlserver: Microsoft.Data.SqlClient.SqlBulkCopy.</item>
    /// <item>mysql: MySqlConnector.MySqlBulkCopy.</item>
    /// <item>sqlite / anything else: the dialect-portable single-transaction
    ///   prepared-command loop — one ExecuteNonQuery per row reusing the same
    ///   DbCommand/parameters. Allocation-light and far faster than N
    ///   autocommitted round-trips.</item>
    /// </list>
    /// The sqlserver and mysql fast paths both feed
    /// <c>WriteToServer(IDataReader)</c>, so a single reflection-free (AOT-safe)
    /// <c>DbDataReader</c>-over-<c>IEnumerable&lt;Row&gt;</c> adapter is emitted
    /// once per table and shared. Identity, rowversion, and computed/generated
    /// columns are database-assigned and excluded. Returns the number of rows
    /// inserted.
    /// </summary>
    public static string EmitBulkInsert(string entityName, string rowType, TableSchema tableSchema, string dialect)
    {
        var cols = CrudColumnRules.InsertableColumns(tableSchema.Columns.Values);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");

        bool isPostgres = string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase);
        bool isSqlServer = string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase);

        void EmitOne(string connVar, bool isStatic, bool isAsync)
        {
            if (isPostgres)
                EmitBulkInsertBodyPostgres(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, dialect);
            else if (isSqlServer)
                EmitBulkInsertBodySqlServer(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync);
            else if (isMySql)
                EmitBulkInsertBodyMySql(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync);
            else
                EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, dialect);
        }

        EmitOne("_conn", isStatic: false, isAsync: false);
        sb.AppendLine();
        EmitOne("conn", isStatic: true, isAsync: false);
        sb.AppendLine();
        EmitOne("_conn", isStatic: false, isAsync: true);
        sb.AppendLine();
        EmitOne("conn", isStatic: true, isAsync: true);

        // The SqlBulkCopy / MySqlBulkCopy paths write columns through an
        // IDataReader; emit the shared AOT-safe adapter exactly once.
        if (isSqlServer || isMySql)
        {
            sb.AppendLine();
            EmitBulkReaderAdapter(sb, rowType, cols, dialect);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

}
