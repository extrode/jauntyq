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
        string? dialect = null)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string syncReturn = identity != null ? identity.Value.CSharpType : "int";
        string returnType = isAsync ? $"System.Threading.Tasks.Task<{syncReturn}>" : syncReturn;
        string methodName = isAsync ? $"{query.Name}Async" : query.Name;
        string paramList = BuildParamList(paramInfos, isStatic, isAsync, trailingNullableDefaults: true);

        sb.AppendLine($"        {modifier}{asyncModifier} {returnType} {methodName}({paramList})");
        sb.AppendLine("        {");

        EmitValueGuards(sb, paramInfos);

        // Connection lifecycle
        sb.AppendLine($"            bool weOpened = {connVar}.State != System.Data.ConnectionState.Open;");
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
        if (procName != null)
        {
            sb.AppendLine($"                cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(procName)}\";");
            sb.AppendLine("                cmd.CommandType = System.Data.CommandType.StoredProcedure;");
        }
        else if (identity != null)
        {
            string identitySql = BuildIdentityInsertSql(
                StripLeadingSqlComments(originalSql), identity.Value.Dialect, identity.Value.ColumnName);
            sb.AppendLine($"                cmd.CommandText = @\"{EscapeVerbatimString(identitySql)}\";");
        }
        else
        {
            sb.AppendLine($"                cmd.CommandText = @\"{EscapeVerbatimString(StripLeadingSqlComments(originalSql))}\";");
        }

        EmitParameterBinding(sb, paramInfos, dialect);

        // Execute
        sb.AppendLine();
        if (identity != null)
        {
            string behavior = "System.Data.CommandBehavior.SingleRow | System.Data.CommandBehavior.SingleResult";
            sb.AppendLine(isAsync
                ? $"                using var reader = await cmd.ExecuteReaderAsync({behavior}, cancellationToken).ConfigureAwait(false);"
                : $"                using var reader = cmd.ExecuteReader({behavior});");
            string readCall = isAsync
                ? "await reader.ReadAsync(cancellationToken).ConfigureAwait(false)"
                : "reader.Read()";
            sb.AppendLine($"                if (!({readCall}))");
            sb.AppendLine("                    throw new System.InvalidOperationException(\"INSERT did not return an identity value.\");");
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
            sb.Append("System.Data.Common.DbConnection conn");

        for (int i = 0; i < paramInfos.Count; i++)
        {
            var p = paramInfos[i];
            if (sb.Length > 0) sb.Append(", ");
            sb.Append($"{p.CSharpType} {p.Name}");
            if (i >= firstDefault) sb.Append(" = default");
        }

        if (isAsync)
        {
            if (sb.Length > 0) sb.Append(", ");
            // Async streaming iterators annotate the token so the framework can
            // flow a token supplied via await foreach (...).WithCancellation(ct).
            if (enumeratorCancellation)
                sb.Append("[System.Runtime.CompilerServices.EnumeratorCancellation] ");
            sb.Append("System.Threading.CancellationToken cancellationToken = default");
        }

        return sb.ToString();
    }

    private static void EmitParameterBinding(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos, string? dialect = null)
    {
        for (int i = 0; i < paramInfos.Count; i++)
        {
            var param = paramInfos[i];
            string varName = $"p{i}";
            sb.AppendLine();

            // PostgreSQL: NpgsqlParameter<T>.TypedValue keeps the value
            // strongly typed end to end - no object boxing at the ADO.NET
            // boundary, and Npgsql infers the exact wire type from T (no
            // DbType). PostgreSQL parameter typing is OID-based, so the
            // per-length plan-cache concern does not apply and Size is not
            // emitted; the client-side write guards still run.
            if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"                var {varName} = new global::Npgsql.NpgsqlParameter<{param.CSharpType}> {{ ParameterName = \"@{param.Name}\", TypedValue = {param.Name} }};");
                sb.AppendLine($"                cmd.Parameters.Add({varName});");
                continue;
            }

            sb.AppendLine($"                var {varName} = cmd.CreateParameter();");
            sb.AppendLine($"                {varName}.ParameterName = \"@{param.Name}\";");
            string? adoDbType = MapCSharpTypeToAdoDbType(param.CSharpType);
            if (adoDbType != null)
            {
                sb.AppendLine($"                {varName}.DbType = System.Data.DbType.{adoDbType};");
            }
            EmitParameterSizing(sb, param, varName);
            // DbParameter.Value is object, so value types box exactly once per
            // call here - an ADO.NET boundary cost every library pays. For
            // non-nullable value types the DBNull coalesce is dead code, so
            // emit a plain assignment; the (object?) dance is only needed
            // where null is actually possible.
            if (IsNonNullableValueType(param.CSharpType))
            {
                sb.AppendLine($"                {varName}.Value = {param.Name};");
            }
            else
            {
                sb.AppendLine($"                {varName}.Value = (object?){param.Name} ?? System.DBNull.Value;");
            }
            sb.AppendLine($"                cmd.Parameters.Add({varName});");
        }
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
