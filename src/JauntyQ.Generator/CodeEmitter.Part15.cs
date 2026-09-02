using JauntyQ.Schema;

namespace JauntyQ.Generator;

public static partial class CodeEmitter
{
    /// <summary>
    /// One emittable scalar function: the accessor method name, the SQL text,
    /// the mapped return type, and the mapped parameters in call order.
    /// </summary>
    internal sealed class EmittableFunction
    {
        public string Method = "";
        public string Sql = "";
        public string ReturnType = "";
        public System.Collections.Generic.List<(string CsType, string CsName, string SqlName)> Params =
            new System.Collections.Generic.List<(string, string, string)>();
    }

    /// <summary>
    /// Spec 014 FR-001. Decides which captured functions can be emitted, and
    /// how — shared by the emitter and by the generator's diagnostic pass, so
    /// the set that produces methods and the set that escapes JNT2023/JNT2024
    /// cannot drift apart. A function reported as unmappable but emitted
    /// anyway, or dropped silently because only one of two independent
    /// implementations of "emittable" changed, is the failure this shape
    /// exists to make impossible.
    ///
    /// <paramref name="collisions"/> receives every accessor method name that
    /// two or more functions fold onto, mapped to the function names that fold
    /// there; <paramref name="unmappable"/> receives functions whose return or
    /// a parameter maps to <c>object</c>; <paramref name="tableValued"/>
    /// receives functions taking a parameter typed by a captured table type.
    /// All three are refused rather than emitted.
    /// </summary>
    internal static System.Collections.Generic.List<EmittableFunction> PlanFunctionEmission(
        DatabaseSchema? schema,
        System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>? collisions = null,
        System.Collections.Generic.List<(string Function, string Reason)>? unmappable = null,
        System.Collections.Generic.List<(string Function, string TypeName)>? tableValued = null)
    {
        var plan = new System.Collections.Generic.List<EmittableFunction>();
        if (schema == null || schema.Functions.Count == 0)
            return plan;

        // SQLite has no CREATE FUNCTION and never populates Functions; any
        // other dialect string means a snapshot this generator does not know
        // how to write SQL for, so it emits nothing rather than guessing at a
        // call syntax.
        bool isSqlServer = string.Equals(schema.Dialect, "sqlserver", System.StringComparison.OrdinalIgnoreCase);
        bool isPostgres = string.Equals(schema.Dialect, "postgres", System.StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(schema.Dialect, "mysql", System.StringComparison.OrdinalIgnoreCase);
        if (!isSqlServer && !isPostgres && !isMySql)
            return plan;

        // Collisions are computed over every function whose NAME is usable,
        // before mapping — so two overloads that fold to one method name are
        // reported as a collision even when one of them would separately have
        // been refused as unmappable. Reporting the narrower reason first
        // would tell the user to fix a type when the real fix is a rename.
        var byMethod = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<FunctionSchema>>(
            System.StringComparer.Ordinal);
        foreach (var fn in schema.Functions.Values)
        {
            if (!IsUsableSqlName(fn.Name, schema.Dialect))
                continue;
            string? fnSchema = fn.Schema;
            if (!isMySql && !string.IsNullOrEmpty(fnSchema) && !IsUsableSqlName(fnSchema!, schema.Dialect))
                continue;

            string method = DialectMapper.ToPascalCase(fn.Name);
            if (!byMethod.TryGetValue(method, out var list))
                byMethod[method] = list = new System.Collections.Generic.List<FunctionSchema>();
            list.Add(fn);
        }

        foreach (var pair in byMethod)
        {
            if (pair.Value.Count > 1)
            {
                if (collisions != null)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var fn in pair.Value)
                        names.Add(FunctionDisplayName(fn));
                    collisions[pair.Key] = names;
                }

                // Emit nothing for ANY of them. Keeping the first (the rule
                // EmitSequenceAccessor uses for JNT2009) is safe for a
                // sequence, where every accessor is nullary and the two differ
                // only in which sequence advances. A function's overloads
                // differ in their PARAMETERS, so keeping the first would
                // silently bind every call site to one arbitrary signature.
                continue;
            }

            var only = pair.Value[0];
            var emittable = TryMapFunction(only, pair.Key, schema, isMySql, unmappable, tableValued);
            if (emittable != null)
                plan.Add(emittable);
        }

        plan.Sort((a, b) => string.CompareOrdinal(a.Method, b.Method));
        return plan;
    }

    /// <summary>
    /// The name a diagnostic prints for a function: the bare name plus its
    /// argument types, so two PostgreSQL overloads reported under one
    /// colliding accessor name are distinguishable in the message. Without the
    /// arguments, JNT2023 on a real overload pair reads as though the same
    /// function were listed twice.
    /// </summary>
    private static string FunctionDisplayName(FunctionSchema fn)
    {
        if (fn.Params.Count == 0)
            return fn.Name + "()";

        var types = new System.Collections.Generic.List<string>();
        foreach (var p in fn.Params)
            types.Add(p.DbType);
        return fn.Name + "(" + string.Join(", ", types) + ")";
    }

    /// <summary>
    /// A name safe to embed unquoted in emitted SQL: a bare identifier, not
    /// reserved in the target dialect, and not case-folding away. The same
    /// three gates EmitSequenceAccessor applies, factored out because the
    /// function path needs them for the schema qualifier as well as the name.
    /// </summary>
    private static bool IsUsableSqlName(string name, string? dialect)
    {
        if (!IdentifierGuard.IsValidIdentifier(name))
            return false;
        if (JauntyQ.Analysis.DialectReservedWords.IsReservedInDialect(
                name, dialect, JauntyQ.Analysis.SqlIdentifierPosition.Object))
            return false;
        if (JauntyQ.Analysis.DialectReservedWords.RequiresQuotingForCase(name, dialect))
            return false;
        return true;
    }

    private static EmittableFunction? TryMapFunction(
        FunctionSchema fn,
        string method,
        DatabaseSchema schema,
        bool isMySql,
        System.Collections.Generic.List<(string Function, string Reason)>? unmappable,
        System.Collections.Generic.List<(string Function, string TypeName)>? tableValued)
    {
        // A table type reaching a parameter is JNT2025, and it is checked
        // before mapping: a TVP maps to `object` like any other unknown type,
        // so without this it would be reported as merely unmappable and the
        // user would be told to change a type they cannot change. Only
        // procedures can take one in SQL Server, so this is defense against a
        // snapshot shape rather than a case the extractors produce today --
        // which is the point, since the deferred spec will produce it.
        foreach (var p in fn.Params)
        {
            string typeName = p.ResolvedFromUserType ?? p.DbType;
            if (schema.UserTypes.TryGetValue(typeName, out var ut) &&
                (ut.Kind == UserTypeKind.TableType || ut.Kind == UserTypeKind.Composite))
            {
                tableValued?.Add((FunctionDisplayName(fn), ut.Name));
                return null;
            }
        }

        string returnType = DialectMapper.MapDbTypeToCSharp(
            fn.Return.DbType, fn.Return.IsNullable, fn.Return.MaxLength, schema.Dialect);
        if (IsUnmappedType(returnType))
        {
            unmappable?.Add((FunctionDisplayName(fn), $"its return type '{fn.Return.DbType}'"));
            return null;
        }

        var result = new EmittableFunction { Method = method, ReturnType = returnType };

        // The three formal parameters the emitted signatures introduce
        // themselves. A function parameter named any of them produces a
        // duplicate-parameter compile failure inside generated code, which is
        // the worst place for it to surface -- the same defect family
        // AUD-R67-01/AUD-R68-01 fixed for procedures. Here every function
        // parameter is required and there is no overload without the
        // bookkeeping ones, so the collision is resolved by renaming the
        // SCHEMA-derived parameter rather than the bookkeeping one; the SQL
        // placeholder keeps the database's own name regardless, so the call
        // still binds correctly.
        var reserved = new System.Collections.Generic.HashSet<string>(
            new[] { "conn", "transaction", "cancellationToken" }, System.StringComparer.Ordinal);
        var used = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        foreach (var p in fn.Params)
        {
            string csType = DialectMapper.MapDbTypeToCSharp(
                p.DbType, p.IsNullable, p.MaxLength, schema.Dialect);
            if (IsUnmappedType(csType))
            {
                unmappable?.Add((FunctionDisplayName(fn), $"parameter '{p.Name}' of type '{p.DbType}'"));
                return null;
            }

            // Bare-identifier shape only, and deliberately NOT the reserved-word
            // or case-folding gates IsUsableSqlName applies. A parameter name
            // appears as "@name", which is a placeholder rather than an object
            // identifier: "@transaction" is legal in SQL Server even though
            // TRANSACTION is reserved, and the placeholder's case cannot fold
            // away because the same string is emitted as the ParameterName it
            // is matched against. Running the object-position gate here refused
            // a function for a parameter name the engine accepts.
            if (!IdentifierGuard.IsValidIdentifier(p.Name))
            {
                unmappable?.Add((FunctionDisplayName(fn), $"parameter '{p.Name}' is not a usable identifier"));
                return null;
            }

            string csName = IdentifierGuard.Escape(ToCamelCase(DialectMapper.ToPascalCase(p.Name)));
            if (reserved.Contains(csName) || !used.Add(csName))
            {
                csName = "__" + csName.TrimStart('@');
                if (!used.Add(csName))
                {
                    unmappable?.Add((FunctionDisplayName(fn),
                        $"parameter '{p.Name}' cannot be given a unique C# name"));
                    return null;
                }
            }

            result.Params.Add((ShortenValueTypeName(schema, csType), csName, p.Name));
        }

        // MySQL's "schema" IS the database the connection is already on, so
        // qualifying would emit `jauntyq_live.fn(...)` and break the moment the
        // same snapshot is used against a differently-named database. SQL
        // Server and PostgreSQL both have a schema inside the database, where
        // the qualifier is what makes the call independent of search_path /
        // default schema.
        string qualified = isMySql || string.IsNullOrEmpty(fn.Schema)
            ? fn.Name
            : fn.Schema + "." + fn.Name;

        var placeholders = new System.Collections.Generic.List<string>();
        foreach (var p in result.Params)
            placeholders.Add("@" + p.SqlName);

        result.Sql = "SELECT " + qualified + "(" + string.Join(", ", placeholders) + ")";
        return result;
    }

    /// <summary>
    /// The column list JNT2025 prints for a refused table type, as
    /// <c> (id int, label nvarchar)</c>, or the empty string when the type was
    /// not captured with members.
    ///
    /// This is the whole reason the extractors capture a type the generator
    /// will never emit for. A refusal that says only "table-valued parameters
    /// are not supported" leaves the reader to go and look the type up; one
    /// that names its columns is enough to write the temporary table the
    /// message suggests instead.
    /// </summary>
    internal static string DescribeUserTypeMembers(DatabaseSchema schema, string typeName)
    {
        if (!schema.UserTypes.TryGetValue(typeName, out var ut) || ut.Members.Count == 0)
            return string.Empty;

        var parts = new System.Collections.Generic.List<string>();
        foreach (var m in ut.Members)
            parts.Add(m.Name + " " + m.DbType);
        return " (" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// True when DialectMapper fell back to the unmapped-type placeholder.
    /// Matched on both the nullable and non-nullable spellings, since the
    /// fallback's nullability follows the column's.
    /// </summary>
    private static bool IsUnmappedType(string csharpType) =>
        csharpType == "object" || csharpType == "object?";

    /// <summary>
    /// Emits the lazily-constructed <c>db.Functions</c> accessor plus its
    /// nested <c>FunctionAccessor</c> class, one method per emittable scalar
    /// function in four overloads (instance/static x sync/async), matching
    /// every other emitted call.
    ///
    /// The call goes out as <c>CommandType.Text</c> through the abstract
    /// <c>DbCommand</c>/<c>DbParameter</c> pair, never a provider type. That is
    /// what keeps a generated function call usable from a consumer that only
    /// ever holds a <c>DbConnection</c> from a <c>DbProviderFactory</c> — and
    /// it is the specific property table-valued parameters could not have,
    /// which is why they are deferred (014-plan.md §1.1) rather than emitted
    /// alongside these.
    /// </summary>
    private static void EmitFunctionAccessor(System.Text.StringBuilder sb, DatabaseSchema? schema)
    {
        var plan = PlanFunctionEmission(schema);
        if (plan.Count == 0)
            return;

        string taskType = TypeRef(schema, "Task", "System.Threading.Tasks");

        sb.AppendLine();
        sb.AppendLine("        private FunctionAccessor? _functions;");
        sb.AppendLine("        public FunctionAccessor Functions => _functions ??= new FunctionAccessor(this);");
        sb.AppendLine();
        sb.AppendLine("        /// <summary>Typed accessors for the database's scalar functions; each");
        sb.AppendLine("        /// method calls the function and returns its single value.</summary>");
        sb.AppendLine("        public sealed class FunctionAccessor");
        sb.AppendLine("        {");
        sb.AppendLine("            private readonly JauntyDb _db;");
        sb.AppendLine();
        sb.AppendLine("            internal FunctionAccessor(JauntyDb db) => _db = db;");
        sb.AppendLine();
        sb.AppendLine("            private static void __BindFunctionParam(DbCommand cmd, string name, object? value)");
        sb.AppendLine("            {");
        sb.AppendLine("                DbParameter __p = cmd.CreateParameter();");
        sb.AppendLine("                __p.ParameterName = name;");
        sb.AppendLine("                __p.Value = value ?? DBNull.Value;");
        sb.AppendLine("                cmd.Parameters.Add(__p);");
        sb.AppendLine("            }");

        foreach (var fn in plan)
        {
            EmitFunctionBody(sb, fn, schema, taskType, isStatic: false, isAsync: false);
            EmitFunctionBody(sb, fn, schema, taskType, isStatic: true, isAsync: false);
            EmitFunctionBody(sb, fn, schema, taskType, isStatic: false, isAsync: true);
            EmitFunctionBody(sb, fn, schema, taskType, isStatic: true, isAsync: true);
        }

        sb.AppendLine("        }");
    }

    private static void EmitFunctionBody(
        System.Text.StringBuilder sb,
        EmittableFunction fn,
        DatabaseSchema? schema,
        string taskType,
        bool isStatic,
        bool isAsync)
    {
        string modifier = isStatic ? "public static" : "public";
        string asyncModifier = isAsync ? " async" : "";
        string ret = isAsync ? $"{taskType}<{fn.ReturnType}>" : fn.ReturnType;
        string name = isAsync ? fn.Method + "Async" : fn.Method;
        string connExpr = isStatic ? "conn" : "_db.Connection";
        string txExpr = isStatic ? "transaction" : "_db.CurrentTransaction";

        var parts = new System.Collections.Generic.List<string>();
        if (isStatic)
            parts.Add("DbConnection conn");
        foreach (var p in fn.Params)
            parts.Add($"{p.CsType} {p.CsName}");
        if (isStatic)
            parts.Add("DbTransaction? transaction = null");
        if (isAsync)
            parts.Add("CancellationToken cancellationToken = default");

        sb.AppendLine();
        sb.AppendLine($"            {modifier}{asyncModifier} {ret} {name}({string.Join(", ", parts)})");
        sb.AppendLine("            {");
        sb.AppendLine($"                bool __weOpened = {connExpr}.State != ConnectionState.Open;");
        sb.AppendLine(isAsync
            ? $"                if (__weOpened) await {connExpr}.OpenAsync(cancellationToken).ConfigureAwait(false);"
            : $"                if (__weOpened) {connExpr}.Open();");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine($"                    using DbCommand __cmd = {connExpr}.CreateCommand();");
        sb.AppendLine($"                    __cmd.CommandText = \"{IdentifierGuard.ToStringLiteral(fn.Sql)}\";");
        sb.AppendLine("                    __cmd.CommandType = CommandType.Text;");
        sb.AppendLine($"                    __cmd.Transaction = {txExpr};");
        foreach (var p in fn.Params)
        {
            sb.AppendLine(
                $"                    __BindFunctionParam(__cmd, \"@{IdentifierGuard.ToStringLiteral(p.SqlName)}\", {p.CsName});");
        }

        // A reader rather than ExecuteScalar, so the return value goes through
        // GetReaderCall -- the same per-type rules every other materialization
        // uses, including the ones ExecuteScalar's `object` would have needed
        // a second, drifting copy of (TimeSpan, IPAddress, arrays, the
        // unsigned MySQL integers, and spec 013's captured enums).
        sb.AppendLine(isAsync
            ? "                    using DbDataReader __reader = await __cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);"
            : "                    using DbDataReader __reader = __cmd.ExecuteReader();");
        sb.AppendLine(isAsync
            ? "                    if (!await __reader.ReadAsync(cancellationToken).ConfigureAwait(false))"
            : "                    if (!__reader.Read())");
        sb.AppendLine($"                        return default({fn.ReturnType})!;");
        sb.AppendLine($"                    return {GetReaderCall(fn.ReturnType, 0, schema, "__reader")};");
        sb.AppendLine("                }");
        sb.AppendLine("                finally");
        sb.AppendLine("                {");
        sb.AppendLine(isAsync
            ? $"                    if (__weOpened) await {connExpr}.CloseAsync().ConfigureAwait(false);"
            : $"                    if (__weOpened) {connExpr}.Close();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
    }
}
