using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
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
        string syncReturn = identity != null ? identity.Value.CSharpType : "int";
        string returnType = isAsync ? $"{TypeRef(schema, "Task", "System.Threading.Tasks")}<{syncReturn}>" : syncReturn;
        string methodName = isAsync ? $"{query.Name}Async" : query.Name;
        string paramList = BuildParamList(paramInfos, isStatic, isAsync, trailingNullableDefaults: true);

        sb.AppendLine($"        {modifier}{asyncModifier} {returnType} {methodName}({paramList})");
        sb.AppendLine("        {");

        EmitValueGuards(sb, paramInfos);

        // Connection lifecycle
        sb.AppendLine($"            bool weOpened = {connVar}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"            if (weOpened) await {connVar}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"            if (weOpened) {connVar}.Open();");
        sb.AppendLine("            try");
        sb.AppendLine("            {");

        // Create command
        sb.AppendLine($"                using var cmd = {connVar}.CreateCommand();");
        if (!isStatic)
        {
            sb.AppendLine("                if (_db?.CurrentTransaction != null) cmd.Transaction = _db.CurrentTransaction;");
        }
        else if (HasStaticTransactionParam(paramInfos, isStatic))
        {
            sb.AppendLine("                if (transaction != null) cmd.Transaction = transaction;");
        }
        if (procName != null)
        {
            sb.AppendLine($"                cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procName)}\";");
            sb.AppendLine("                cmd.CommandType = CommandType.StoredProcedure;");
        }
        else if (identity != null)
        {
            string identitySql = BuildIdentityInsertSql(
                StripLeadingSqlComments(originalSql), identity.Value.Dialect, identity.Value.ColumnName);
            sb.AppendLine($"                cmd.CommandText = @\"{IndentSqlContinuationLines(EscapeVerbatimString(identitySql), 36)}\";");
        }
        else
        {
            sb.AppendLine($"                cmd.CommandText = @\"{IndentSqlContinuationLines(EscapeVerbatimString(StripLeadingSqlComments(originalSql)), 36)}\";");
        }

        EmitParameterBinding(sb, paramInfos, schema);

        // Execute
        sb.AppendLine();
        if (identity != null)
        {
            string behavior = "CommandBehavior.SingleRow | CommandBehavior.SingleResult";
            sb.AppendLine(isAsync
                ? $"                using var reader = await cmd.ExecuteReaderAsync({behavior}, cancellationToken).ConfigureAwait(false);"
                : $"                using var reader = cmd.ExecuteReader({behavior});");
            string readCall = isAsync
                ? "await reader.ReadAsync(cancellationToken).ConfigureAwait(false)"
                : "reader.Read()";
            sb.AppendLine($"                if (!({readCall}))");
            sb.AppendLine("                    throw new InvalidOperationException(\"INSERT did not return an identity value.\");");
            sb.AppendLine($"                return {GetIdentityReaderCall(identity.Value)};");
        }
        else
        {
            sb.AppendLine(isAsync
                ? "                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);"
                : "                return cmd.ExecuteNonQuery();");
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

    private static string BuildParamList(System.Collections.Generic.List<EmittedParam> paramInfos, bool isStatic, bool isAsync, bool trailingNullableDefaults = false, bool enumeratorCancellation = false)
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
            sb.Append("DbConnection conn");

        for (int i = 0; i < paramInfos.Count; i++)
        {
            var p = paramInfos[i];
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{p.CSharpType} {p.CSharpName}");
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
            sb.Append("CancellationToken cancellationToken = default");
        }

        return sb.ToString();
    }

    private static void EmitParameterBinding(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos, DatabaseSchema? schema = null)
    {
        string? dialect = schema?.Dialect;
        for (int i = 0; i < paramInfos.Count; i++)
        {
            var param = paramInfos[i];
            string varName = $"p{i}";
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
                if (IsNonNullableValueType(param.CSharpType))
                {
                    sb.AppendLine($"                var {varName} = new {npgsqlParameterType}<{param.CSharpType}> {{ ParameterName = \"@{param.Name}\", TypedValue = {param.CSharpName} }};");
                }
                else
                {
                    sb.AppendLine($"                var {varName} = new {npgsqlParameterType} {{ ParameterName = \"@{param.Name}\" }};");
                    string? pgAdoDbType = MapCSharpTypeToAdoDbType(param.CSharpType);
                    if (pgAdoDbType != null)
                        sb.AppendLine($"                {varName}.DbType = DbType.{pgAdoDbType};");
                    sb.AppendLine($"                {varName}.Value = (object?){param.CSharpName} ?? DBNull.Value;");
                }
                sb.AppendLine($"                cmd.Parameters.Add({varName});");
                continue;
            }

            sb.AppendLine($"                var {varName} = cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{param.Name}\";");
            string? adoDbType = MapCSharpTypeToAdoDbType(param.CSharpType);
            if (adoDbType != null)
            {
                sb.AppendLine($"                {varName}.DbType = DbType.{adoDbType};");
            }
            EmitParameterSizing(sb, param, varName);
            // DbParameter.Value is object, so value types box exactly once per
            // call here - an ADO.NET boundary cost every library pays. For
            // non-nullable value types the DBNull coalesce is dead code, so
            // emit a plain assignment; the (object?) dance is only needed
            // where null is actually possible.
            if (IsNonNullableValueType(param.CSharpType))
            {
                sb.AppendLine($"                {varName}.Value = {param.CSharpName};");
            }
            else
            {
                sb.AppendLine($"                {varName}.Value = (object?){param.CSharpName} ?? DBNull.Value;");
            }
            sb.AppendLine($"                cmd.Parameters.Add({varName});");
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

        if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            string npgsqlParameterType = TypeRef(schema, "NpgsqlParameter", "Npgsql");
            sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {param.CSharpName}.Count; {loopVar}++)");
            sb.AppendLine("                {");
            if (IsNonNullableValueType(elementType))
            {
                sb.AppendLine($"                    cmd.Parameters.Add(new {npgsqlParameterType}<{elementType}> {{ ParameterName = \"@{param.Name}\" + {loopVar}, TypedValue = {param.CSharpName}[{loopVar}] }});");
            }
            else
            {
                sb.AppendLine($"                    var {varName} = new {npgsqlParameterType} {{ ParameterName = \"@{param.Name}\" + {loopVar} }};");
                if (adoDbType != null)
                    sb.AppendLine($"                    {varName}.DbType = DbType.{adoDbType};");
                sb.AppendLine($"                    {varName}.Value = (object?){param.CSharpName}[{loopVar}] ?? DBNull.Value;");
                sb.AppendLine($"                    cmd.Parameters.Add({varName});");
            }
            sb.AppendLine("                }");
            return;
        }

        sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {param.CSharpName}.Count; {loopVar}++)");
        sb.AppendLine("                {");
        sb.AppendLine($"                    var {varName} = cmd.CreateParameter();");
        sb.AppendLine($"                    {varName}.ParameterName = \"@{param.Name}\" + {loopVar};");
        if (adoDbType != null)
            sb.AppendLine($"                    {varName}.DbType = DbType.{adoDbType};");
        // -- @each params are always comparison (IN-list) params, never write
        // targets: same "size to fit the actual value" defense as a plain
        // comparison parameter (CodeEmitter.Part2.cs's EmitParameterSizing),
        // so an oversize element can never get clipped down to a shorter
        // stored value and falsely match it.
        EmitParameterSizing(sb, elementType, param.MaxLength, param.Precision, param.Scale,
            isWriteTarget: false, varName, $"{param.CSharpName}[{loopVar}]", indent: "                    ");
        sb.AppendLine(IsNonNullableValueType(elementType)
            ? $"                    {varName}.Value = {param.CSharpName}[{loopVar}];"
            : $"                    {varName}.Value = (object?){param.CSharpName}[{loopVar}] ?? DBNull.Value;");
        sb.AppendLine($"                    cmd.Parameters.Add({varName});");
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
