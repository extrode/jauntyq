using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

public static partial class CodeEmitter
{
    private static void EmitCrudMethodBody(
        System.Text.StringBuilder sb,
        QueryModel query,
        string originalSql,
        System.Collections.Generic.List<EmittedParam> paramInfos,
        string connVar,
        bool isStatic,
        bool isAsync,
        string? procName = null,
        IdentityInfo? identity = null,
        DatabaseSchema? schema = null)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string syncReturn = identity != null ? ShortenValueTypeName(schema, identity.Value.CSharpType) : "int";
        string returnType = isAsync ? $"{TypeRef(schema, "Task", "System.Threading.Tasks")}<{syncReturn}>" : syncReturn;
        string methodName = isAsync ? $"{query.Name}Async" : query.Name;

        // AUD-R69-01: same defect family as AUD-R67-01/AUD-R68-01 (which only
        // covered -- @call's EmitProcCallBody), applied here to the ordinary
        // hand-written .sql query path. Unlike the proc-call case, no schema
        // cooperation is needed to trigger this: EmittedParam.CSharpName is
        // the literal SQL parameter name (only keyword-escaped, no
        // PascalCase/camelCase folding), so any .sql file binding a parameter
        // literally named e.g. "@conn"/"@cancellationToken" reaches these
        // formal parameters directly. "conn"/"cancellationToken" DO appear in
        // the public signature, so -- exactly like the proc-call fix --
        // only escalate to a guaranteed-unique fallback in the actual
        // collision case.
        bool connNameCollides = isStatic && AnyParamNameCollidesWith(paramInfos, "conn");
        if (connNameCollides)
            connVar = "__conn";
        bool cancellationTokenNameCollides = isAsync && AnyParamNameCollidesWith(paramInfos, "cancellationToken");
        string tokenParamName = cancellationTokenNameCollides ? "__cancellationToken" : "cancellationToken";

        string paramList = BuildParamList(paramInfos, isStatic, isAsync, connVarName: connVar, tokenParamName: tokenParamName, trailingNullableDefaults: true, schema: schema);

        sb.AppendLine($"        {modifier}{asyncModifier} {returnType} {methodName}({paramList})");
        sb.AppendLine("        {");

        EmitValueGuards(sb, paramInfos, schema);

        // Connection lifecycle. AUD-R69-01: "__weOpened", "__cmd" -- pure
        // internal locals, never part of the public signature or return
        // value, unconditionally renamed. Unlike the -- @call path (where
        // ToPascalCase/ToCamelCase never emit an underscore, making a
        // double-underscore prefix structurally collision-proof),
        // EmittedParam.CSharpName on THIS path is the literal, unfolded SQL
        // parameter name -- so a query parameter literally named
        // "@__weOpened"/"@__cmd" still collides (confirmed live). This
        // rename closes the plausible, schema/query-derived-name collisions
        // this round targets (e.g. "@weOpened"/"@cmd"), not every
        // conceivable dunder-prefixed literal; see AUD-R69-01's registry
        // entry for the residual.
        sb.AppendLine($"            bool __weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (__weOpened) await {connVar}.OpenAsync({tokenParamName}).ConfigureAwait(false);"
            : $"            if (__weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");

        // Create command
        sb.AppendLine($"                using DbCommand __cmd = {connVar}.CreateCommand();");
        if (!isStatic)
        {
            // AUD-R70-01: "this._db", not "_db" -- confirmed live that a
            // query parameter literally named "@_db" collides with the
            // bare field reference, since EmittedParam.CSharpName performs
            // no casing fold (unlike -- @call's schema-parameter naming,
            // structurally immune per DialectMapper.ToPascalCase never
            // re-emitting an underscore).
            sb.AppendLine("                if (this._db?.CurrentTransaction != null) __cmd.Transaction = this._db.CurrentTransaction;");
        }
        else if (HasStaticTransactionParam(paramInfos, isStatic))
        {
            sb.AppendLine("                if (transaction != null) __cmd.Transaction = transaction;");
        }
        if (procName != null)
        {
            sb.AppendLine($"                __cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procName)}\";");
            sb.AppendLine("                __cmd.CommandType = CommandType.StoredProcedure;");
        }
        else if (identity != null)
        {
            string identitySql = BuildIdentityInsertSql(
                StripLeadingSqlComments(originalSql), identity.Value.Dialect, identity.Value.ColumnName);
            sb.AppendLine($"                __cmd.CommandText = @\"{EscapeVerbatimString(IndentSqlContinuationLines(identitySql, 36))}\";");
        }
        else
        {
            sb.AppendLine($"                __cmd.CommandText = @\"{EscapeVerbatimString(IndentSqlContinuationLines(StripLeadingSqlComments(originalSql), 36))}\";");
        }

        EmitParameterBinding(sb, paramInfos, schema);

        // Execute
        sb.AppendLine();
        if (identity != null)
        {
            string behavior = "CommandBehavior.SingleRow | CommandBehavior.SingleResult";
            // AUD-R69-01: "__reader", not "reader" -- confirmed live that a
            // query parameter literally named "@reader" collides (CS0136)
            // with the bare "reader" local. GetIdentityReaderCall's readback
            // expression is threaded the same renamed variable via its own
            // readerVar parameter (CodeEmitter.Part4.cs) -- every OTHER
            // caller of GetReaderCall/GetIdentityReaderCall is a standalone
            // `__Map...(DbDataReader reader)`-style mapper method with its
            // own immune "reader" parameter and keeps the default.
            sb.AppendLine(isAsync
                ? $"                using DbDataReader __reader = await __cmd.ExecuteReaderAsync({behavior}, {tokenParamName}).ConfigureAwait(false);"
                : $"                using DbDataReader __reader = __cmd.ExecuteReader({behavior});");
            string readCall = isAsync
                ? $"await __reader.ReadAsync({tokenParamName}).ConfigureAwait(false)"
                : "__reader.Read()";
            sb.AppendLine($"                if (!({readCall}))");
            sb.AppendLine($"                    throw new {TypeRef(schema, "InvalidOperationException", "System")}(\"INSERT did not return an identity value.\");");
            sb.AppendLine($"                return {GetIdentityReaderCall(identity.Value, schema, readerVar: "__reader")};");
        }
        else
        {
            sb.AppendLine(isAsync
                ? $"                return await __cmd.ExecuteNonQueryAsync({tokenParamName}).ConfigureAwait(false);"
                : "                return __cmd.ExecuteNonQuery();");
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Static variants take an optional trailing DbTransaction: a connection
    /// with an active transaction requires every command to carry it (SqlClient
    /// throws otherwise), and static methods have no JauntyDb to flow an
    /// ambient one from. Skipped in the (pathological) case of a SQL parameter
    /// literally named @transaction, which would collide.
    /// </summary>
    private static bool HasStaticTransactionParam(System.Collections.Generic.List<EmittedParam> paramInfos, bool isStatic) =>
        isStatic && !paramInfos.Exists(p => string.Equals(p.Name, "transaction", StringComparison.OrdinalIgnoreCase));

    // AUD-R69-01: true if a real query/CRUD parameter's own escaped C# name
    // equals bookkeepingName -- used to gate the "conn"/"cancellationToken"
    // formal-parameter fallback renames, mirroring CodeEmitter.Part11.cs's
    // identically-named helper for the -- @call path (a distinct overload,
    // since this path's parameters are List&lt;EmittedParam&gt;, not
    // ProcedureSchema.Params).
    private static bool AnyParamNameCollidesWith(System.Collections.Generic.List<EmittedParam> paramInfos, string bookkeepingName) =>
        paramInfos.Exists(p => p.CSharpName == bookkeepingName);

    private static string BuildParamList(System.Collections.Generic.List<EmittedParam> paramInfos, bool isStatic, bool isAsync, string connVarName = "conn", string tokenParamName = "cancellationToken", bool trailingNullableDefaults = false, bool enumeratorCancellation = false, DatabaseSchema? schema = null)
    {
        // C# optional parameters must be trailing: give `= default` to the
        // longest suffix of nullable-column parameters so callers pass only
        // what the schema actually requires.
        int firstDefault = paramInfos.Count;
        if (trailingNullableDefaults)
        {
            while (firstDefault > 0 && paramInfos[firstDefault - 1].IsNullable)
                firstDefault--;
        }

        var sb = new System.Text.StringBuilder();
        if (isStatic)
            sb.Append($"DbConnection {connVarName}");

        for (int i = 0; i < paramInfos.Count; i++)
        {
            var p = paramInfos[i];
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{ShortenValueTypeName(schema, p.CSharpType)} {p.CSharpName}");
            if (i >= firstDefault) sb.Append(" = default");
        }

        if (HasStaticTransactionParam(paramInfos, isStatic))
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append("DbTransaction? transaction = null");
        }

        if (isAsync)
        {
            if (sb.Length > 0) sb.Append(", ");
            // Async streaming iterators annotate the token so the framework can
            // flow a token supplied via await foreach (...).WithCancellation(ct).
            if (enumeratorCancellation)
                sb.Append("[EnumeratorCancellation] ");
            sb.Append($"CancellationToken {tokenParamName} = default");
        }

        return sb.ToString();
    }

    private static void EmitParameterBinding(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos, DatabaseSchema? schema = null)
    {
        string? dialect = schema?.Dialect;
        // AUD-R50-03 (residual): System.Data.DbType was emitted bare — a table
        // named "db_types" (row POCO "DbType") shadowed the enum namespace-wide
        // and broke every `p.DbType = DbType.X` assignment with CS0117.
        string dbTypeEnum = TypeRef(schema, "DbType", "System.Data");
        for (int i = 0; i < paramInfos.Count; i++)
        {
            var param = paramInfos[i];
            // AUD-R69-01: "__p{i}", not "p{i}" -- confirmed live that a query
            // parameter literally named "@p0" collides (CS0136) with the
            // bare "p0" local for the first bound parameter. Pure-internal,
            // zero API impact, same fix as the -- @call path's identical
            // convention (CodeEmitter.Part11.cs).
            string varName = $"__p{i}";
            sb.AppendLine();

            if (param.IsEach)
            {
                EmitEachParameterBinding(sb, param, varName, schema);
                continue;
            }

            // PostgreSQL parameter typing is OID-based, so the per-length
            // plan-cache concern does not apply and Size is not emitted; the
            // client-side write guards still run.
            if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                // NpgsqlParameter<T>.TypedValue keeps non-null values strongly
                // typed end to end with no boxing. A null TypedValue, however,
                // leaves the parameter with no resolved wire type and Npgsql
                // throws "must have either its DbType ... or its Value set".
                // For nullable params emit a plain NpgsqlParameter that coalesces
                // null to DBNull and pins a DbType so the null case still has a
                // resolved type (provider-neutral System.Data.DbType, no mapping
                // layer). Non-nullable value types keep the fast generic path.
                string npgsqlParameterType = TypeRef(schema, "NpgsqlParameter", "Npgsql");

                // Spec 013 T12/T12a. A Postgres enum column takes text on the
                // wire, but an ordinary parameter carrying a plain string is
                // rejected with 42804 ("column is of type order_status but
                // expression is of type text") -- and setting DbType.String,
                // which MapCSharpTypeToAdoDbType would do, is rejected the
                // same way. Probed live against PostgreSQL 16 / Npgsql 8.0.6:
                // NpgsqlDbType.Unknown with NO DbType is the one combination
                // the server accepts, because it defers the type decision to
                // the server's own text-to-enum coercion.
                if (IsEnumParameterType(param.CSharpType, schema))
                {
                    string npgsqlDbTypeEnum = TypeRef(schema, "NpgsqlDbType", "NpgsqlTypes");
                    sb.AppendLine($"                var {varName} = new {npgsqlParameterType} {{ ParameterName = \"@{param.Name}\" }};");
                    sb.AppendLine($"                {varName}.NpgsqlDbType = {npgsqlDbTypeEnum}.Unknown;");
                    string wire = EnumWireCall(param.CSharpType, schema, param.CSharpName)!;
                    sb.AppendLine(param.CSharpType.EndsWith("?")
                        ? $"                {varName}.Value = {param.CSharpName} is null ? (object)DBNull.Value : {EnumWireCall(param.CSharpType, schema, param.CSharpName + ".Value")!};"
                        : $"                {varName}.Value = {wire};");
                    sb.AppendLine($"                __cmd.Parameters.Add({varName});");
                    continue;
                }

                if (IsNonNullableValueType(param.CSharpType, schema))
                {
                    sb.AppendLine($"                var {varName} = new {npgsqlParameterType}<{ShortenValueTypeName(schema, param.CSharpType)}> {{ ParameterName = \"@{param.Name}\", TypedValue = {param.CSharpName} }};");
                }
                else
                {
                    sb.AppendLine($"                var {varName} = new {npgsqlParameterType} {{ ParameterName = \"@{param.Name}\" }};");
                    string? pgAdoDbType = MapCSharpTypeToAdoDbType(param.CSharpType);
                    if (pgAdoDbType != null)
                        sb.AppendLine($"                {varName}.DbType = {dbTypeEnum}.{pgAdoDbType};");
                    sb.AppendLine($"                {varName}.Value = (object?){param.CSharpName} ?? DBNull.Value;");
                }
                sb.AppendLine($"                __cmd.Parameters.Add({varName});");
                continue;
            }

            sb.AppendLine($"                DbParameter {varName} = __cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{param.Name}\";");

            // Spec 013 T12: the non-Postgres scalar path, which every MySQL
            // query parameter and every CRUD write parameter takes. Bound as
            // the wire string; MapCSharpTypeToAdoDbType has no arm for a
            // snapshot-derived enum name and would otherwise leave the
            // parameter with no DbType at all.
            string? enumWire = EnumWireCall(param.CSharpType, schema, param.CSharpName);
            if (enumWire != null)
            {
                sb.AppendLine($"                {varName}.DbType = {dbTypeEnum}.String;");
                sb.AppendLine(param.CSharpType.EndsWith("?")
                    ? $"                {varName}.Value = {param.CSharpName} is null ? (object)DBNull.Value : {EnumWireCall(param.CSharpType, schema, param.CSharpName + ".Value")!};"
                    : $"                {varName}.Value = {enumWire};");
                sb.AppendLine($"                __cmd.Parameters.Add({varName});");
                continue;
            }

            string? adoDbType = MapCSharpTypeToAdoDbType(param.CSharpType);
            if (adoDbType != null)
            {
                sb.AppendLine($"                {varName}.DbType = {dbTypeEnum}.{adoDbType};");
            }
            EmitParameterSizing(sb, param, varName);
            // DbParameter.Value is object, so value types box exactly once per
            // call here - an ADO.NET boundary cost every library pays. For
            // non-nullable value types the DBNull coalesce is dead code, so
            // emit a plain assignment; the (object?) dance is only needed
            // where null is actually possible.
            if (IsNonNullableValueType(param.CSharpType, schema))
            {
                sb.AppendLine($"                {varName}.Value = {param.CSharpName};");
            }
            else
            {
                sb.AppendLine($"                {varName}.Value = (object?){param.CSharpName} ?? DBNull.Value;");
            }
            sb.AppendLine($"                __cmd.Parameters.Add({varName});");
        }
    }

    /// <summary>
    /// Binds one -- @each param: loops the caller's list and adds one
    /// DbParameter per element, named "@ParamName0", "@ParamName1", ... to
    /// match the CommandText expansion built alongside it in
    /// CodeEmitter.Part8.cs's EmitCommandText.
    /// </summary>
    private static void EmitEachParameterBinding(System.Text.StringBuilder sb, EmittedParam param, string varName, DatabaseSchema? schema)
    {
        string? dialect = schema?.Dialect;
        string elementType = GetEachElementType(param.CSharpType);
        string loopVar = $"__ib_{param.Name}";
        string? adoDbType = MapCSharpTypeToAdoDbType(elementType);
        // AUD-R50-03 (residual): same DbType-shadowing guard as EmitParameterBinding.
        string dbTypeEnum = TypeRef(schema, "DbType", "System.Data");

        // Spec 013 T12: WHERE status IN (-- @each) over an enum column is
        // reachable on both dialects. Handled ahead of the dialect split
        // because the conversion is the same either way; only the parameter
        // type differs, and Postgres needs the same NpgsqlDbType.Unknown the
        // scalar path does.
        string? eachEnumWire = EnumWireCall(elementType, schema, $"{param.CSharpName}[{loopVar}]");
        if (eachEnumWire != null)
        {
            sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {param.CSharpName}.Count; {loopVar}++)");
            sb.AppendLine("                {");
            if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                string npgsqlParameterTypeForEnum = TypeRef(schema, "NpgsqlParameter", "Npgsql");
                string npgsqlDbTypeEnum = TypeRef(schema, "NpgsqlDbType", "NpgsqlTypes");
                sb.AppendLine($"                    var {varName} = new {npgsqlParameterTypeForEnum} {{ ParameterName = \"@{param.Name}\" + {loopVar} }};");
                sb.AppendLine($"                    {varName}.NpgsqlDbType = {npgsqlDbTypeEnum}.Unknown;");
            }
            else
            {
                sb.AppendLine($"                    DbParameter {varName} = __cmd.CreateParameter();");
                sb.AppendLine($"                    {varName}.ParameterName = \"@{param.Name}\" + {loopVar};");
                sb.AppendLine($"                    {varName}.DbType = {dbTypeEnum}.String;");
            }
            // A nullable enum element is OrderStatus?, which ToWire cannot
            // take: unwrap inside the null branch, as the scalar and COPY
            // paths do. IN (NULL) never matches in SQL, but binding DBNull is
            // still the only shape that compiles and round-trips.
            sb.AppendLine(elementType.EndsWith("?")
                ? $"                    {varName}.Value = {param.CSharpName}[{loopVar}] is null ? (object)DBNull.Value : {EnumWireCall(elementType, schema, $"{param.CSharpName}[{loopVar}].Value")!};"
                : $"                    {varName}.Value = {eachEnumWire};");
            sb.AppendLine($"                    __cmd.Parameters.Add({varName});");
            sb.AppendLine("                }");
            return;
        }

        if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            string npgsqlParameterType = TypeRef(schema, "NpgsqlParameter", "Npgsql");
            sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {param.CSharpName}.Count; {loopVar}++)");
            sb.AppendLine("                {");
            if (IsNonNullableValueType(elementType, schema))
            {
                sb.AppendLine($"                    __cmd.Parameters.Add(new {npgsqlParameterType}<{ShortenValueTypeName(schema, elementType)}> {{ ParameterName = \"@{param.Name}\" + {loopVar}, TypedValue = {param.CSharpName}[{loopVar}] }});");
            }
            else
            {
                sb.AppendLine($"                    var {varName} = new {npgsqlParameterType} {{ ParameterName = \"@{param.Name}\" + {loopVar} }};");
                if (adoDbType != null)
                    sb.AppendLine($"                    {varName}.DbType = {dbTypeEnum}.{adoDbType};");
                sb.AppendLine($"                    {varName}.Value = (object?){param.CSharpName}[{loopVar}] ?? DBNull.Value;");
                sb.AppendLine($"                    __cmd.Parameters.Add({varName});");
            }
            sb.AppendLine("                }");
            return;
        }

        string elementVar = $"{varName}_element";
        sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {param.CSharpName}.Count; {loopVar}++)");
        sb.AppendLine("                {");
        // Bind the indexed element to a local once: the guarded length
        // expression EmitParameterSizing emits below re-checks the value for
        // null, and re-evaluating a list indexer (rather than a plain local)
        // at each of those checks defeats the compiler's null-state tracking
        // and emits a spurious CS8602 on the second dereference.
        sb.AppendLine($"                    var {elementVar} = {param.CSharpName}[{loopVar}];");
        sb.AppendLine($"                    DbParameter {varName} = __cmd.CreateParameter();");
        sb.AppendLine($"                    {varName}.ParameterName = \"@{param.Name}\" + {loopVar};");
        if (adoDbType != null)
            sb.AppendLine($"                    {varName}.DbType = {dbTypeEnum}.{adoDbType};");
        // -- @each params are always comparison (IN-list) params, never write
        // targets: same "size to fit the actual value" defense as a plain
        // comparison parameter (CodeEmitter.Part2.cs's EmitParameterSizing),
        // so an oversize element can never get clipped down to a shorter
        // stored value and falsely match it.
        EmitParameterSizing(sb, elementType, param.MaxLength, param.Precision, param.Scale,
            isWriteTarget: false, varName, elementVar, indent: "                    ");
        sb.AppendLine(IsNonNullableValueType(elementType, schema)
            ? $"                    {varName}.Value = {elementVar};"
            : $"                    {varName}.Value = (object?){elementVar} ?? DBNull.Value;");
        sb.AppendLine($"                    __cmd.Parameters.Add({varName});");
        sb.AppendLine("                }");
    }

    internal readonly struct IdentityInfo
    {
        public readonly string ColumnName;
        public readonly string CSharpType;
        public readonly string Dialect;

        public IdentityInfo(string columnName, string csharpType, string dialect)
        {
            ColumnName = columnName;
            CSharpType = csharpType;
            Dialect = dialect;
        }
    }

}
