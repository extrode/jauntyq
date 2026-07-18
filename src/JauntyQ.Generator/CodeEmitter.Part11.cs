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
        string? dialect,
        DatabaseSchema? schema = null)
    {
        string listType = TypeRef(schema, "List", "System.Collections.Generic");
        string taskType = TypeRef(schema, "Task", "System.Threading.Tasks");
        // AUD-R50-03 (residual): same DbType-shadowing guard as
        // CodeEmitter.Part3.cs's EmitParameterBinding.
        string dbTypeEnum = TypeRef(schema, "DbType", "System.Data");
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        // Return: List<Result> for row-returning procs, else int (row count).
        string syncReturn = hasResults ? $"{listType}<{resultType}>" : "int";
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

        // AUD-R67-01: the tuple's own first element name ("results"/
        // "affected") is a JauntyQ-introduced bookkeeping name, not derived
        // from the schema -- but it was never checked against the OTHER
        // tuple elements, which ARE schema-derived (an OUT/INOUT param's own
        // camelCased name). A procedure with an OUT param literally named
        // "Affected" (non-row-returning) or "Results" (row-returning)
        // produces a tuple type with two identically-named elements --
        // confirmed via a real Roslyn compile: CS8127 "Tuple element names
        // must be unique." Only escalate to a guaranteed-unique fallback
        // name in the actual collision case, so the common (non-colliding)
        // case keeps its friendly, human-readable tuple element name.
        bool firstTupleElementCollides = outOrInOutParams.Exists(p =>
            IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)))
                == (hasResults ? "results" : "affected"));
        string firstTupleElementName = hasResults
            ? (firstTupleElementCollides ? "__results" : "results")
            : (firstTupleElementCollides ? "__affected" : "affected");

        string declaredReturn;
        if (asyncReturnsTuple)
        {
            var tupleParts = new System.Collections.Generic.List<string>
            {
                $"{syncReturn} {firstTupleElementName}"
            };
            foreach (var p in outOrInOutParams)
            {
                string outCt = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
                string outPname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
                tupleParts.Add($"{ShortenValueTypeName(schema, outCt)} {outPname}");
            }
            declaredReturn = $"{taskType}<({string.Join(", ", tupleParts)})>";
        }
        else
        {
            declaredReturn = isAsync ? $"{taskType}<{syncReturn}>" : syncReturn;
        }

        // AUD-R68-01: same defect family as AUD-R67-01 -- the static
        // overload's "conn" parameter and the async overload's
        // "cancellationToken" parameter are JauntyQ-introduced bookkeeping
        // names that were never checked against real schema-derived
        // parameter names either. Confirmed via a real Roslyn compile: a
        // schema parameter literally named "Conn" (static) or
        // "CancellationToken" (async) produces a duplicate-parameter
        // compile failure (CS0100/CS0229), since these two are hardcoded
        // formal parameters, not just internal locals. Unlike the pure-
        // internal locals below, these two DO appear in the method's
        // public signature, so -- exactly like AUD-R67-01's own tuple-
        // element-name fix -- only escalate to a guaranteed-unique
        // fallback name in the actual collision case, keeping the
        // conventional "conn"/"cancellationToken" name otherwise.
        bool connNameCollides = isStatic && AnyParamNameCollidesWith(procedure, "conn");
        if (connNameCollides)
            connVar = "__conn";
        bool cancellationTokenNameCollides = isAsync && AnyParamNameCollidesWith(procedure, "cancellationToken");
        string tokenParamName = cancellationTokenNameCollides ? "__cancellationToken" : "cancellationToken";

        // Parameter list: IN params by value; OUT/INOUT params as C# `out`/`ref`
        // on the sync overload. Async drops OUT entirely and keeps INOUT as a
        // plain input value (see above). "conn" is emitted via connVar (not a
        // hardcoded literal) so the collision fallback above actually takes
        // effect here too.
        var parts = new System.Collections.Generic.List<string>();
        if (isStatic)
            parts.Add($"DbConnection {connVar}");
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            string displayCt = ShortenValueTypeName(schema, ct);
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.Out)
            {
                if (!isAsync)
                    parts.Add($"out {displayCt} {pname}");
            }
            else if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.InOut)
                parts.Add(isAsync ? $"{displayCt} {pname}" : $"ref {displayCt} {pname}");
            else
                parts.Add($"{displayCt} {pname}");
        }
        // Static variants take an optional trailing DbTransaction (see
        // HasStaticTransactionParam); skipped if a proc parameter maps to the
        // same C# name.
        bool hasTransactionParam = isStatic
            && !parts.Exists(p => p.EndsWith(" transaction", StringComparison.Ordinal));
        if (hasTransactionParam)
            parts.Add("DbTransaction? transaction = null");
        if (isAsync)
            parts.Add($"CancellationToken {tokenParamName} = default");

        // AUD-R66-01: Postgres's CALL protocol reports only a bare "CALL"
        // completion tag for a procedure invocation, never an affected-row
        // count -- confirmed live: cmd.ExecuteNonQuery() (and
        // DbDataReader.RecordsAffected after an ExecuteReader-based CALL)
        // both unconditionally return -1 for a Postgres procedure, even one
        // that performs a genuine multi-row UPDATE. This is a structural
        // Postgres/Npgsql protocol limitation with no ADO.NET-level
        // workaround. MySQL confirmed live to always report a real
        // count for the identical call shape. SQL Server's own count is
        // NOT an unconditional per-dialect guarantee the way Postgres's -1
        // is -- confirmed live that a bound procedure using `SET NOCOUNT
        // ON` (a common, idiomatic T-SQL pattern -- JauntyQ's own --
        // @proc-emitted procedures use it) also reports -1 via
        // ExecuteNonQuery, same as Postgres. That gap is pre-existing,
        // general ADO.NET/T-SQL behavior unrelated to what changed here
        // (JauntyQ never authors the bound procedure's body for --
        // @call), so it isn't gated by this fix; only Postgres's
        // unconditional, authoring-independent -1 gets a caveat here.
        if (!hasResults && string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// PostgreSQL note: the returned row count is always <c>-1</c>,");
            sb.AppendLine("        /// unconditionally. Postgres's <c>CALL</c> protocol reports only a");
            sb.AppendLine("        /// bare completion tag for a procedure invocation, never an");
            sb.AppendLine("        /// affected-row count, regardless of how the procedure is authored.");
            sb.AppendLine("        /// This is a structural PostgreSQL/Npgsql limitation, not a JauntyQ");
            sb.AppendLine("        /// defect; do not rely on this value to detect whether the");
            sb.AppendLine("        /// procedure's side effects occurred.");
            sb.AppendLine("        /// </summary>");
        }
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

        // AUD-R68-01: "cmd"/"reader" (below) are unconditionally renamed
        // with a double-underscore prefix -- pure-internal locals, never
        // part of the public signature or return value, so (per the same
        // underscore-can-never-be-schema-derived reasoning AUD-R67-01
        // established for "__affected"/"__results") this is a zero-risk,
        // always-safe rename rather than a conditional one. Confirmed via a
        // real Roslyn compile that a schema parameter literally named
        // "Cmd"/"Reader" collided (CS0136) with the un-prefixed versions of
        // these locals. NOTE: "weOpened" (right below) has the identical
        // collision (a schema param named "WeOpened" also collides,
        // confirmed live) but is deliberately left un-renamed here --
        // EmitFinallyClose (CodeEmitter.Part4.cs), a HELPER SHARED BY 7
        // call sites across this file, hardcodes the literal "weOpened"
        // and is not itself parameterized to accept a different name;
        // fixing it correctly means auditing and updating all 7 call
        // sites, a larger cross-cutting change outside this finding's
        // scope -- recorded as a residual for a future round instead.
        sb.AppendLine($"            bool weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync({tokenParamName}).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine($"                using DbCommand __cmd = {connVar}.CreateCommand();");
        if (!isStatic)
            sb.AppendLine("                if (_db?.CurrentTransaction != null) __cmd.Transaction = _db.CurrentTransaction;");
        else if (hasTransactionParam)
            sb.AppendLine("                if (transaction != null) __cmd.Transaction = transaction;");
        sb.AppendLine($"                __cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procedure.Name)}\";");
        sb.AppendLine("                __cmd.CommandType = CommandType.StoredProcedure;");

        // Bind parameters. Track OUT/INOUT parameter variable names for readback.
        var outReadback = new System.Collections.Generic.List<(string ParamVar, string CSharpName, string CSharpType, bool Nullable)>();
        int idx = 0;
        foreach (var p in procedure.Params)
        {
            if (p.Direction == JauntyQ.Schema.ProcedureParamDirection.ReturnValue)
                continue;
            // AUD-R68-01: "__p0"/"__p1"/... not "p0"/"p1"/... -- confirmed
            // live that a schema parameter literally named "P0" collides
            // (CS0136) with the bare "p0" local for the first bound
            // parameter. Pure-internal, zero API impact.
            string varName = $"__p{idx++}";
            string ct = DialectMapper.MapDbTypeToCSharp(p.DbType, p.IsNullable, dialect: dialect);
            string pname = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            sb.AppendLine($"                DbParameter {varName} = __cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{IdentifierGuard.ToStringLiteral(p.Name)}\";");
            string? adoDbType = MapCSharpTypeToAdoDbType(ct);
            if (adoDbType != null)
                sb.AppendLine($"                {varName}.DbType = {dbTypeEnum}.{adoDbType};");
            switch (p.Direction)
            {
                case JauntyQ.Schema.ProcedureParamDirection.Out:
                    sb.AppendLine($"                {varName}.Direction = ParameterDirection.Output;");
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
                    sb.AppendLine($"                {varName}.Direction = ParameterDirection.InputOutput;");
                    if (p.MaxLength is int ml2 && ml2 != 0)
                        sb.AppendLine($"                {varName}.Size = {ml2};");
                    if (p.Precision is int prec2)
                        sb.AppendLine($"                {varName}.Precision = {prec2};");
                    if (p.Scale is int scl2)
                        sb.AppendLine($"                {varName}.Scale = {scl2};");
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? DBNull.Value;");
                    outReadback.Add((varName, pname, ct, p.IsNullable || !IsNonNullableValueType(ct)));
                    break;
                default:
                    sb.AppendLine(IsNonNullableValueType(ct)
                        ? $"                {varName}.Value = {pname};"
                        : $"                {varName}.Value = (object?){pname} ?? DBNull.Value;");
                    break;
            }
            sb.AppendLine($"                __cmd.Parameters.Add({varName});");
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
            string behavior = "CommandBehavior.SingleResult";
            // AUD-R67-01: named "__results", not "results" -- a schema
            // param/column can never camelCase to a name containing an
            // underscore (DialectMapper.ToPascalCase treats '_' as a pure
            // word separator, never emitted), so this local can never
            // collide with a real parameter's own name the way a bare
            // "results" local could (confirmed via a real Roslyn compile:
            // an IN, OUT, or INOUT param literally named "Results" produces
            // a formal parameter also named "results", and the two
            // definitions of the same name in the same scope are a
            // CS0136/CS0029 compile failure). Purely an internal rename --
            // the tuple TYPE's own declared element name (see
            // firstTupleElementName above) is unaffected by this and keeps
            // its friendly public-facing name.
            // AUD-R68-01: "__reader", not "reader" -- confirmed live that a
            // schema parameter literally named "Reader" (on a row-returning
            // proc) collides (CS0136) with the bare "reader" local.
            sb.AppendLine($"                var __results = new {listType}<{resultType}>();");
            sb.AppendLine(isAsync
                ? $"                using (DbDataReader __reader = await __cmd.ExecuteReaderAsync({behavior}, {tokenParamName}).ConfigureAwait(false))"
                : $"                using (DbDataReader __reader = __cmd.ExecuteReader({behavior}))");
            sb.AppendLine("                {");
            string readCall = isAsync ? $"await __reader.ReadAsync({tokenParamName}).ConfigureAwait(false)" : "__reader.Read()";
            sb.AppendLine($"                    while ({readCall})");
            sb.AppendLine($"                        __results.Add(__Map{methodName}(__reader));");
            sb.AppendLine("                }");
            if (asyncReturnsTuple)
            {
                var localNames = EmitProcOutReadback(sb, outReadback, "                ", declareLocals: true, schema);
                sb.AppendLine($"                return (__results, {string.Join(", ", localNames)});");
            }
            else
            {
                EmitProcOutReadback(sb, outReadback, "                ", declareLocals: false, schema);
                sb.AppendLine("                return __results;");
            }
        }
        else
        {
            sb.AppendLine(isAsync
                ? $"                int __affected = await __cmd.ExecuteNonQueryAsync({tokenParamName}).ConfigureAwait(false);"
                : "                int __affected = __cmd.ExecuteNonQuery();");
            if (asyncReturnsTuple)
            {
                var localNames = EmitProcOutReadback(sb, outReadback, "                ", declareLocals: true, schema);
                sb.AppendLine($"                return (__affected, {string.Join(", ", localNames)});");
            }
            else
            {
                EmitProcOutReadback(sb, outReadback, "                ", declareLocals: false, schema);
                sb.AppendLine("                return __affected;");
            }
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// AUD-R68-01: true if a real (non-return-value) schema parameter's own
    /// escaped camelCase name equals <paramref name="bookkeepingName"/> --
    /// used to gate the "conn"/"cancellationToken" formal-parameter fallback
    /// renames the same way AUD-R67-01 gates the tuple's first-element name,
    /// since these two names DO appear in the public method signature and
    /// should only escalate to the guaranteed-unique fallback in the actual
    /// collision case.
    /// </summary>
    private static bool AnyParamNameCollidesWith(JauntyQ.Schema.ProcedureSchema procedure, string bookkeepingName) =>
        procedure.Params.Exists(p =>
            p.Direction != JauntyQ.Schema.ProcedureParamDirection.ReturnValue
            && IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name))) == bookkeepingName);

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
        bool declareLocals,
        DatabaseSchema? schema = null)
    {
        var targetNames = new System.Collections.Generic.List<string>();
        foreach (var (paramVar, csName, rawCsType, nullable) in outParams)
        {
            string csType = ShortenValueTypeName(schema, rawCsType);
            string baseType = csType.EndsWith("?") ? csType.Substring(0, csType.Length - 1) : csType;
            // AUD-R68-01: "__{csName}Out", not "{csName}Out" -- this local is
            // only declared on the async overload (declareLocals: true),
            // purely to be folded into the tuple return below; it never
            // appears in the public API surface (the tuple TYPE's own
            // declared element name comes from a separate code path in
            // EmitProcCallBody, not from here). Confirmed live: an OUT
            // param named "Total" produces the readback local "totalOut" by
            // this suffix convention, which collides (CS0128) with a
            // SECOND, sibling OUT param literally named "TotalOut" (whose
            // own csName is also "totalOut") on the same procedure --
            // schema-vs-JauntyQ-bookkeeping, same defect shape as
            // "__results"/"__cmd"/"__reader" above, not the schema-vs-schema
            // case that's out of scope for this round.
            string target = declareLocals ? $"__{csName}Out" : csName;
            string declKeyword = declareLocals ? $"{csType} " : "";
            // DBNull -> default; otherwise unbox to the declared type.
            sb.AppendLine($"{indent}{declKeyword}{target} = {paramVar}.Value is null || {paramVar}.Value is DBNull ? default! : ({csType})({baseType}){paramVar}.Value;");
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
    public static string EmitBulkInsert(string entityName, string rowType, TableSchema tableSchema, string dialect, DatabaseSchema? schema = null)
    {
        var cols = CrudColumnRules.InsertableColumns(tableSchema.Columns.Values);

        bool isPostgres = string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase);
        bool isSqlServer = string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(dialect, "mysql", StringComparison.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Data.Common;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        if (isPostgres)
            sb.AppendLine("using Npgsql;");
        else if (isSqlServer)
            sb.AppendLine("using Microsoft.Data.SqlClient;");
        else if (isMySql)
            sb.AppendLine("using MySqlConnector;");
        sb.AppendLine();
        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");

        void EmitOne(string connVar, bool isStatic, bool isAsync)
        {
            if (isPostgres)
                EmitBulkInsertBodyPostgres(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, dialect, schema);
            else if (isSqlServer)
                EmitBulkInsertBodySqlServer(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, schema);
            else if (isMySql)
                EmitBulkInsertBodyMySql(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, schema);
            else
                EmitBulkInsertBody(sb, rowType, tableSchema.Name, cols, connVar, isStatic, isAsync, dialect, schema);
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
            EmitBulkReaderAdapter(sb, rowType, cols, dialect, schema);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

}
