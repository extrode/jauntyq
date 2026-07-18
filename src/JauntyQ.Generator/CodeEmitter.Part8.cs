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
        foreach (var param in OrderedParameters(query, directives))
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
        // inline - see the design notes (row materialization).
        // Canonical full-row queries go one step further and share the
        // single Read() materializer on the row POCO itself.
        string mapperCall;
        if (canonicalRowType != null)
        {
            // AUD-R69-01: passes "__reader" (EmitMethodBody's own renamed
            // local), not "reader" -- the callee's OWN "reader" parameter
            // (declared right below / on the row type itself) is a separate,
            // immune scope and is intentionally left unrenamed.
            mapperCall = $"{canonicalRowType}.Read(__reader)";
        }
        else
        {
            mapperCall = $"__Map{query.Name}(__reader)";
            sb.AppendLine($"        private static {returnType} __Map{query.Name}(DbDataReader reader) => new {returnType}");
            sb.AppendLine("        {");
            EmitColumnAssignments(sb, projection, indent: "            ", schema);
            sb.AppendLine("        };");
            sb.AppendLine();
        }

        // Instance sync
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "_conn", isStatic: false, isAsync: false, isFirst, queryId, mapperCall, procName, schema, isStream);
        sb.AppendLine();
        // Static sync
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "conn", isStatic: true, isAsync: false, isFirst, queryId, mapperCall, procName, schema, isStream);
        sb.AppendLine();
        // Instance async
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "_conn", isStatic: false, isAsync: true, isFirst, queryId, mapperCall, procName, schema, isStream);
        sb.AppendLine();
        // Static async
        EmitMethodBody(sb, query, projection, returnType, originalSql, paramInfos, "conn", isStatic: true, isAsync: true, isFirst, queryId, mapperCall, procName, schema, isStream);
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
        DatabaseSchema? schema = null,
        bool isStream = false)
    {
        string? dialect = schema?.Dialect;
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
                ? $"{TypeRef(schema, "IAsyncEnumerable", "System.Collections.Generic")}<{returnType}>"
                : $"{TypeRef(schema, "IEnumerable", "System.Collections.Generic")}<{returnType}>";
        else
            syncReturn = isFirst ? $"{returnType}?" : $"{TypeRef(schema, "List", "System.Collections.Generic")}<{returnType}>";
        // Non-stream async wraps the sync return in Task<>; stream async returns
        // IAsyncEnumerable<T> directly (it is itself awaitable-by-iteration).
        string declaredReturn = (isAsync && !isStream)
            ? $"{TypeRef(schema, "Task", "System.Threading.Tasks")}<{syncReturn}>"
            : syncReturn;
        string methodName = isAsync ? $"{query.Name}Async" : query.Name;
        // Async streaming iterators need [EnumeratorCancellation] on the token so
        // `await foreach (... .WithCancellation(ct))` flows the token through.
        bool cancelAttr = isAsync && isStream;

        // AUD-R69-01: same defect family as AUD-R67-01/AUD-R68-01 (which only
        // covered -- @call's EmitProcCallBody), applied here to the ordinary
        // hand-written .sql query path -- see EmitCrudMethodBody's identical
        // comment (CodeEmitter.Part3.cs) for full detail. "conn"/
        // "cancellationToken" DO appear in the public signature, so only
        // escalate to a guaranteed-unique fallback in the actual collision case.
        bool connNameCollides = isStatic && AnyParamNameCollidesWith(paramInfos, "conn");
        if (connNameCollides)
            connVar = "__conn";
        bool cancellationTokenNameCollides = isAsync && AnyParamNameCollidesWith(paramInfos, "cancellationToken");
        string tokenParamName = cancellationTokenNameCollides ? "__cancellationToken" : "cancellationToken";

        string paramList = BuildParamList(paramInfos, isStatic, isAsync, connVarName: connVar, tokenParamName: tokenParamName, enumeratorCancellation: cancelAttr, schema: schema);

        sb.AppendLine($"        {modifier}{asyncModifier} {declaredReturn} {methodName}({paramList})");
        sb.AppendLine("        {");

        EmitValueGuards(sb, paramInfos, schema);
        EmitEachListGuards(sb, paramInfos, returnType, isFirst, isStream, schema);

        // Connection lifecycle. AUD-R69-01: "__weOpened", "__cmd" -- pure
        // internal locals, unconditionally renamed (see EmitCrudMethodBody's
        // identical comment).
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
            sb.AppendLine("                if (_db?.CurrentTransaction != null) __cmd.Transaction = _db.CurrentTransaction;");
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
        else
        {
            EmitCommandText(sb, StripLeadingSqlComments(originalSql), paramInfos);
        }

        EmitParameterBinding(sb, paramInfos, schema);

        // Execute reader + one-time shape guard. SingleResult (and SingleRow
        // for @first) lets the provider optimize buffering for the shape we
        // are guaranteed to consume.
        string behavior = isFirst
            ? "CommandBehavior.SingleRow | CommandBehavior.SingleResult"
            : "CommandBehavior.SingleResult";
        sb.AppendLine();
        // AUD-R69-01: "__reader", not "reader" -- confirmed live that a query
        // parameter literally named "@reader" collides (CS0136) with the
        // bare "reader" local.
        sb.AppendLine(isAsync
            ? $"                using DbDataReader __reader = await __cmd.ExecuteReaderAsync({behavior}, {tokenParamName}).ConfigureAwait(false);"
            : $"                using DbDataReader __reader = __cmd.ExecuteReader({behavior});");
        // AUD-R50-03 (residual): Volatile was emitted bare — a table named
        // "volatiles" (row POCO "Volatile") shadowed System.Threading.Volatile
        // namespace-wide, breaking Volatile.Read/Write with CS1615/CS0117.
        string volatileType = TypeRef(schema, "Volatile", "System.Threading");
        sb.AppendLine($"                if ({volatileType}.Read(ref __{query.Name}Validated) == 0)");
        sb.AppendLine("                {");
        sb.AppendLine($"                    JauntyQShapeGuard.Validate(__reader, __{query.Name}Columns, \"{queryId}\");");
        sb.AppendLine($"                    {volatileType}.Write(ref __{query.Name}Validated, 1);");
        sb.AppendLine("                }");

        string readCall = isAsync
            ? $"await __reader.ReadAsync({tokenParamName}).ConfigureAwait(false)"
            : "__reader.Read()";

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
            // AUD-R69-01: "__results", not "results" -- confirmed live that a
            // query parameter literally named "@results" collides (CS0136)
            // with the bare "results" local, same defect AUD-R67-01 fixed for
            // -- @call's identical row-list-local convention.
            sb.AppendLine($"                var __results = new {TypeRef(schema, "List", "System.Collections.Generic")}<{returnType}>();");
            sb.AppendLine($"                while ({readCall})");
            sb.AppendLine("                {");
            sb.AppendLine($"                    __results.Add({mapperCall});");
            sb.AppendLine("                }");
            sb.AppendLine("                return __results;");
        }

        EmitFinallyClose(sb, connVar, isAsync);
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Per -- @each list argument, before the connection is opened:
    /// an empty list short-circuits (`IN ()` is invalid SQL in every supported
    /// dialect, and an empty list unambiguously means an empty result set —
    /// mirrors the hand-written IN-list workaround this directive replaces,
    /// see the test log, bug #2); an oversize list fails fast with
    /// the dialect's parameter budget in the message instead of an obscure
    /// server/provider error mid-command.
    /// </summary>
    private static void EmitEachListGuards(System.Text.StringBuilder sb, System.Collections.Generic.List<EmittedParam> paramInfos, string returnType, bool isFirst, bool isStream, DatabaseSchema? schema)
    {
        int cap = EachParameterCap(schema?.Dialect);
        bool any = false;
        foreach (var param in paramInfos)
        {
            if (!param.IsEach)
                continue;
            sb.AppendLine($"            if ({param.CSharpName}.Count == 0)");
            if (isStream)
                sb.AppendLine("                yield break;");
            else if (isFirst)
                sb.AppendLine("                return null;");
            else
                sb.AppendLine($"                return new {TypeRef(schema, "List", "System.Collections.Generic")}<{returnType}>();");
            sb.AppendLine($"            if ({param.CSharpName}.Count > {cap})");
            sb.AppendLine($"                throw new {TypeRef(schema, "ArgumentException", "System")}($\"-- @each list '{param.Name}' has {{{param.CSharpName}.Count}} elements, exceeding the {cap}-parameter budget for this database. Batch the call into smaller chunks.\", nameof({param.CSharpName}));");
            any = true;
        }
        if (any)
            sb.AppendLine();
    }

    /// <summary>
    /// The per-list element budget for -- @each expansion, from each engine's
    /// hard per-command parameter limit with headroom left for the query's
    /// scalar parameters: SQL Server rejects &gt;2100 parameters per request;
    /// SQLite's default SQLITE_MAX_VARIABLE_NUMBER is 32766 (3.32+, as bundled
    /// by SQLitePCLRaw); the PostgreSQL extended-protocol Bind and MySQL
    /// prepared-statement formats both carry a 16-bit parameter count. An
    /// unknown/missing dialect gets the most conservative budget.
    /// </summary>
    private static int EachParameterCap(string? dialect) => dialect?.ToLowerInvariant() switch
    {
        "postgres" => 65_000,
        "mysql" => 65_000,
        "sqlite" => 32_000,
        "sqlserver" => 2_000,
        _ => 2_000,
    };

    /// <summary>
    /// Emits CommandText. With no -- @each params this is the existing single
    /// verbatim string; otherwise the compile-time-known SQL text is split
    /// around every "@ParamName" occurrence for an @each param, and each
    /// occurrence is replaced at runtime with a computed "@ParamName0,@ParamName1,..."
    /// expansion sized to the caller's list -- no runtime regex/string-scanning.
    /// </summary>
    private static void EmitCommandText(System.Text.StringBuilder sb, string sql, System.Collections.Generic.List<EmittedParam> paramInfos)
    {
        var eachParams = paramInfos.FindAll(p => p.IsEach);
        if (eachParams.Count == 0)
        {
            sb.AppendLine($"                __cmd.CommandText = @\"{EscapeVerbatimString(IndentSqlContinuationLines(sql, 36))}\";");
            return;
        }

        foreach (var ep in eachParams)
        {
            string loopVar = $"__i_{ep.Name}";
            sb.AppendLine($"                var __each_{ep.Name} = new StringBuilder();");
            sb.AppendLine($"                for (int {loopVar} = 0; {loopVar} < {ep.CSharpName}.Count; {loopVar}++)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    if ({loopVar} > 0) __each_{ep.Name}.Append(',');");
            sb.AppendLine($"                    __each_{ep.Name}.Append(\"@{ep.Name}\").Append({loopVar});");
            sb.AppendLine("                }");
        }

        var segments = SplitSqlForEach(sql, eachParams);
        sb.Append("                __cmd.CommandText = ");
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
    /// never mistaken for a reference to an @each param named "Ids"; skips
    /// string literals, quoted/bracketed identifiers, and comments so text
    /// like <c>'ops@Ids'</c> is never expanded (it is not a parameter there).
    /// </summary>
    private static System.Collections.Generic.List<(bool IsEachRef, string Value)> SplitSqlForEach(string sql, System.Collections.Generic.List<EmittedParam> eachParams)
    {
        var segments = new System.Collections.Generic.List<(bool, string)>();
        var literal = new System.Text.StringBuilder();
        int i = 0;
        while (i < sql.Length)
        {
            // Regions where '@' is plain text, copied through verbatim:
            // 'string' (with '' escape), "ident"/`ident` (doubled-char escape),
            // [ident], -- line comments, /* block comments */.
            char c = sql[i];
            if (c == '\'' || c == '"' || c == '`')
            {
                int end = SkipQuotedRun(sql, i, c);
                literal.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c == '[')
            {
                int end = i + 1;
                while (end < sql.Length && sql[end] != ']')
                    end++;
                if (end < sql.Length) end++; // include the closing ]
                literal.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                int end = i;
                while (end < sql.Length && sql[end] != '\n')
                    end++;
                literal.Append(sql, i, end - i);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int end = i + 2;
                while (end + 1 < sql.Length && !(sql[end] == '*' && sql[end + 1] == '/'))
                    end++;
                end = end + 1 < sql.Length ? end + 2 : sql.Length;
                literal.Append(sql, i, end - i);
                i = end;
                continue;
            }

            if (sql[i] == '@')
            {
                int start = i + 1;
                int j = start;
                while (j < sql.Length && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_'))
                    j++;
                string token = sql.Substring(start, j - start);
                var matched = eachParams.Find(p => string.Equals(p.Name, token, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Returns the index just past a quoted run starting at <paramref name="start"/>
    /// (which holds the opening <paramref name="quote"/>). A doubled delimiter is
    /// an escape in every supported dialect ('' / "" / ``). An unterminated run
    /// extends to end-of-input — by emission time the tokenizer has already
    /// rejected unterminated constructs, so this is defensive only.
    /// </summary>
    private static int SkipQuotedRun(string sql, int start, char quote)
    {
        int i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == quote)
            {
                if (i + 1 < sql.Length && sql[i + 1] == quote)
                {
                    i += 2; // escaped delimiter
                    continue;
                }
                return i + 1; // past the closing quote
            }
            i++;
        }
        return sql.Length;
    }

}
