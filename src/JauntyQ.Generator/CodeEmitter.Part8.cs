using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitQueryMethods(
        System.Text.StringBuilder sb,
        QueryModel query,
        ProjectionModel projection,
        string originalSql,
        string entityName,
        DatabaseSchema? schema,
        Directives.DirectiveModel? directives = null,
        string? procName = null,
        bool isFirst = false,
        string? canonicalRowType = null,
        bool isStream = false)
    {
        string returnType = canonicalRowType ?? $"Result.{projection.Name}";

        var paramInfos = new System.Collections.Generic.List<EmittedParam>();
        foreach (var param in query.Parameters)
        {
            string paramType = InferParameterType(param.Name, query, projection, schema, directives);
            var column = ResolveBoundColumn(param, query, schema, out string? boundTable);
            bool isEach = directives?.EachParams != null &&
                directives.EachParams.Exists(n => string.Equals(n, param.Name, StringComparison.OrdinalIgnoreCase));
            paramInfos.Add(CreateEmittedParam(param.Name, paramType, isNullable: false, column, boundTable, isWriteTarget: false, isEach: isEach));
        }

        string queryId = $"{entityName}.{query.Name}";

        // One materializer shared by all four variants. A direct static call
        // (not a delegate): the JIT inlines small static methods, while
        // delegate invocations are indirect calls it will not reliably
        // inline - see docs/02-design/design-decisions.md (row materialization).
        // Canonical full-row queries go one step further and share the
        // single Read() materializer on the row POCO itself.
        string mapperCall;
        if (canonicalRowType != null)
        {
            mapperCall = $"{canonicalRowType}.Read(reader)";
        }
        else
        {
            mapperCall = $"__Map{query.Name}(reader)";
            sb.AppendLine($"        private static {returnType} __Map{query.Name}(System.Data.Common.DbDataReader reader) => new {returnType}");
            sb.AppendLine("        {");
            EmitColumnAssignments(sb, projection, indent: "            ");
            sb.AppendLine("        };");
            sb.AppendLine();
        }

        // Instance sync
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "_conn", isStatic: false, isAsync: false, isFirst, queryId, mapperCall, procName, schema?.Dialect, isStream);
        sb.AppendLine();
        // Static sync
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "conn", isStatic: true, isAsync: false, isFirst, queryId, mapperCall, procName, schema?.Dialect, isStream);
        sb.AppendLine();
        // Instance async
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "_conn", isStatic: false, isAsync: true, isFirst, queryId, mapperCall, procName, schema?.Dialect, isStream);
        sb.AppendLine();
        // Static async
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "conn", isStatic: true, isAsync: true, isFirst, queryId, mapperCall, procName, schema?.Dialect, isStream);
    }

    private static void EmitMethodBody(
        System.Text.StringBuilder sb,
        QueryModel query,
        ProjectionModel projection,
        string returnType,
        string originalSql,
        System.Collections.Generic.List<EmittedParam> paramInfos,
        string connVar,
        bool isStatic,
        bool isAsync,
        bool isFirst,
        string queryId,
        string mapperCall,
        string? procName = null,
        string? dialect = null,
        bool isStream = false)
    {
        string modifier = isStatic ? "public static" : "public";
        // A streaming method is an iterator: sync -> IEnumerable<T> (no `async`),
        // async -> IAsyncEnumerable<T> with `async`. yield return inside the
        // existing try/finally is legal (no catch), and disposing the enumerator
        // early runs the finally, closing the reader/connection deterministically.
        string asyncModifier = (isAsync && !isStream) ? " async"
            : (isAsync && isStream) ? " async" : "";
        string syncReturn;
        if (isStream)
            syncReturn = isAsync
                ? $"System.Collections.Generic.IAsyncEnumerable<{returnType}>"
                : $"System.Collections.Generic.IEnumerable<{returnType}>";
        else
            syncReturn = isFirst ? $"{returnType}?" : $"System.Collections.Generic.List<{returnType}>";
        // Non-stream async wraps the sync return in Task<>; stream async returns
        // IAsyncEnumerable<T> directly (it is itself awaitable-by-iteration).
        string declaredReturn = (isAsync && !isStream)
            ? $"System.Threading.Tasks.Task<{syncReturn}>"
            : syncReturn;
        string methodName = isAsync ? $"{query.Name}Async" : query.Name;
        // Async streaming iterators need [EnumeratorCancellation] on the token so
        // `await foreach (... .WithCancellation(ct))` flows the token through.
        bool cancelAttr = isAsync && isStream;
        string paramList = BuildParamList(paramInfos, isStatic, isAsync, enumeratorCancellation: cancelAttr);

        sb.AppendLine($"        {modifier}{asyncModifier} {declaredReturn} {methodName}({paramList})");
        sb.AppendLine("        {");

        EmitValueGuards(sb, paramInfos);
        EmitEachEmptyGuards(sb, paramInfos, returnType, isFirst, isStream);

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
        else
        {
            EmitCommandText(sb, StripLeadingSqlComments(originalSql), paramInfos);
        }

        EmitParameterBinding(sb, paramInfos, dialect);

        // Execute reader + one-time shape guard. SingleResult (and SingleRow
        // for @first) lets the provider optimize buffering for the shape we
        // are guaranteed to consume.
        string behavior = isFirst
            ? "System.Data.CommandBehavior.SingleRow | System.Data.CommandBehavior.SingleResult"
            : "System.Data.CommandBehavior.SingleResult";
        sb.AppendLine();
        sb.AppendLine(isAsync
            ? $"                using var reader = await cmd.ExecuteReaderAsync({behavior}, cancellationToken).ConfigureAwait(false);"
            : $"                using var reader = cmd.ExecuteReader({behavior});");
        sb.AppendLine($"                if (System.Threading.Volatile.Read(ref __{query.Name}Validated) == 0)");
        sb.AppendLine("                {");
        sb.AppendLine($"                    global::JauntyQ.Generated.JauntyQShapeGuard.Validate(reader, __{query.Name}Columns, \"{queryId}\");");
        sb.AppendLine($"                    System.Threading.Volatile.Write(ref __{query.Name}Validated, 1);");
        sb.AppendLine("                }");

        string readCall = isAsync
            ? "await reader.ReadAsync(cancellationToken).ConfigureAwait(false)"
            : "reader.Read()";

        if (isStream)
        {
            // Iterator: yield each row straight off the reader. Nothing is
            // buffered, so memory stays constant regardless of result-set size.
            // The reader/command/connection are held open by the enclosing
            // try/finally until the consumer finishes (or disposes) enumeration.
            sb.AppendLine($"                while ({readCall})");
            sb.AppendLine("                {");
            sb.AppendLine($"                    yield return {mapperCall};");
            sb.AppendLine("                }");
        }
        else if (isFirst)
        {
            sb.AppendLine($"                if (!({readCall}))");
            sb.AppendLine("                    return null;");
            sb.AppendLine($"                return {mapperCall};");
        }
        else
        {
            sb.AppendLine($"                var results = new System.Collections.Generic.List<{returnType}>();");
            sb.AppendLine($"                while ({readCall})");
            sb.AppendLine("                {");
            sb.AppendLine($"                    results.Add({mapperCall});");
            sb.AppendLine("                }");
            sb.AppendLine("                return results;");
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Short-circuits before the connection is opened when an -- @each list
    /// argument is empty: `IN ()` is invalid SQL in every supported dialect,
    /// and an empty list unambiguously means an empty result set. Mirrors the
    /// hand-written IN-list workaround this directive replaces (see
    /// the test log, bug #2).
    /// </summary>
    private static void EmitEachEmptyGuards(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos, string returnType, bool isFirst, bool isStream)
    {
        bool any = false;
        foreach (var param in paramInfos)
        {
            if (!param.IsEach)
                continue;
            sb.AppendLine($"            if ({param.Name}.Count == 0)");
            if (isStream)
                sb.AppendLine("                yield break;");
            else if (isFirst)
                sb.AppendLine("                return null;");
            else
                sb.AppendLine($"                return new System.Collections.Generic.List<{returnType}>();");
            any = true;
        }
        if (any)
            sb.AppendLine();
    }

    /// <summary>
    /// Emits CommandText. With no -- @each params this is the existing single
    /// verbatim string; otherwise the compile-time-known SQL text is split
    /// around every "@ParamName" occurrence for an @each param, and each
    /// occurrence is replaced at runtime with a computed "@ParamName0,@ParamName1,..."
    /// expansion sized to the caller's list -- no runtime regex/string-scanning.
    /// </summary>
    private static void EmitCommandText(System.Text.StringBuilder sb, string sql, System.Collections.Generic.List<EmittedParam> paramInfos)
    {
        var eachParams = paramInfos.Where(p => p.IsEach).ToList();
        if (eachParams.Count == 0)
        {
            sb.AppendLine($"                cmd.CommandText = @\"{EscapeVerbatimString(sql)}\";");
            return;
        }

        foreach (var ep in eachParams)
        {
            string loopVar = $"__i_{ep.Name}";
            sb.AppendLine($"                var __each_{ep.Name} = new System.Text.StringBuilder();");
            sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {ep.Name}.Count; {loopVar}++)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    if ({loopVar} > 0) __each_{ep.Name}.Append(',');");
            sb.AppendLine($"                    __each_{ep.Name}.Append(\"@{ep.Name}\").Append({loopVar});");
            sb.AppendLine("                }");
        }

        var segments = SplitSqlForEach(sql, eachParams);
        sb.Append("                cmd.CommandText = ");
        for (int i = 0; i < segments.Count; i++)
        {
            if (i > 0) sb.Append(" + ");
            sb.Append(segments[i].IsEachRef
                ? $"__each_{segments[i].Value}.ToString()"
                : $"@\"{EscapeVerbatimString(segments[i].Value)}\"");
        }
        sb.AppendLine(";");
    }

    /// <summary>
    /// Splits SQL text into literal segments and @each-param references. Scans
    /// whole "@identifier" tokens (not a substring search) so "@IdsFoo" is
    /// never mistaken for a reference to an @each param named "Ids".
    /// </summary>
    private static System.Collections.Generic.List<(bool IsEachRef, string Value)> SplitSqlForEach(string sql, System.Collections.Generic.List<EmittedParam> eachParams)
    {
        var segments = new System.Collections.Generic.List<(bool, string)>();
        var literal = new System.Text.StringBuilder();
        int i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '@')
            {
                int start = i + 1;
                int j = start;
                while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
                    j++;
                string token = sql.Substring(start, j - start);
                var matched = eachParams.FirstOrDefault(p => string.Equals(p.Name, token, StringComparison.OrdinalIgnoreCase));
                if (token.Length > 0 && matched.Name != null)
                {
                    if (literal.Length > 0)
                    {
                        segments.Add((false, literal.ToString()));
                        literal.Clear();
                    }
                    segments.Add((true, matched.Name));
                    i = j;
                    continue;
                }
            }
            literal.Append(sql[i]);
            i++;
        }
        if (literal.Length > 0)
            segments.Add((false, literal.ToString()));
        return segments;
    }

}
