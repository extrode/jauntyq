using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

public static partial class CodeEmitter
{
    /// <summary>
    /// Emits a row-returning INSERT/UPDATE/DELETE (a statement carrying a
    /// user-written RETURNING clause). The RETURNING list is projected exactly
    /// like a SELECT, so this reuses the SELECT reader emission: the CRUD SQL is
    /// emitted verbatim and the RETURNING columns are read by ordinal.
    /// <c>-- @first</c> yields a single-row result; otherwise a list.
    /// </summary>
    internal static string EmitCrudReturning(QueryModel query, ProjectionModel projection, string originalSql,
        string entityName, DatabaseSchema? schema, Directives.DirectiveModel? directives)
        => Emit(query, projection, originalSql, entityName, schema, directives, canonicalRowType: null);

    /// <summary>
    /// Resolves the single identity key column for an -- @identity INSERT.
    /// Returns null when preconditions are not met (the generator reports
    /// JNT7001 for user files before emission; synthetics only carry the
    /// directive when resolvable).
    /// </summary>
    internal static IdentityInfo? ResolveIdentityInfo(QueryModel query, DatabaseSchema? schema, Directives.DirectiveModel? directives)
    {
        if (directives?.ReturnsIdentity != true || schema == null || string.IsNullOrEmpty(schema.Dialect))
            return null;
        if (query.StatementType != StatementType.Insert || query.TargetTable == null)
            return null;
        if (!SchemaLookup.TryGetTable(schema, query.TargetTable, out var tableSchema))
            return null;

        var identityCol = Extrode.JauntyQ.Analysis.CrudColumnRules.SingleIdentityColumn(tableSchema!.Columns.Values);
        if (identityCol == null)
            return null;

        string csharpType = DialectMapper.MapDbTypeToCSharp(identityCol.DbType, isNullable: false, dialect: schema.Dialect);
        return new IdentityInfo(identityCol.Name, csharpType, schema.Dialect);
    }

    /// <summary>
    /// Rewrites a plain INSERT so it returns the database-assigned identity:
    /// OUTPUT INSERTED.x (SQL Server, before VALUES), RETURNING x
    /// (PostgreSQL/SQLite), or a trailing SELECT LAST_INSERT_ID() (MySQL).
    /// </summary>
    internal static string BuildIdentityInsertSql(string sql, string dialect, string identityColumn)
    {
        string trimmed = sql.TrimEnd();
        while (trimmed.EndsWith(";"))
            trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

        // AUD-R10-03: schema.Dialect is a bare, unnormalized string straight
        // from the JSON snapshot (SchemaLoader.Load does a plain deserialize),
        // and JNT7003 accepts any casing via DialectMapper.IsKnownDialect's
        // OrdinalIgnoreCase membership check -- so a perfectly ordinary,
        // JNT7003-accepted "dialect": "SqlServer" (matching the extractor
        // class's own PascalCase name) must not silently fall through to
        // default here. ToLowerInvariant matches OrdinalIgnoreCase semantics
        // for these ASCII-only case labels.
        switch (dialect.ToLowerInvariant())
        {
            case "sqlserver":
                {
                    int idx = FindValuesKeyword(trimmed);
                    if (idx < 0)
                        return trimmed; // generator validated the shape; defensive only
                    return trimmed.Substring(0, idx) + $"OUTPUT INSERTED.{identityColumn}\n" + trimmed.Substring(idx);
                }
            case "postgres":
            case "sqlite":
                return trimmed + $"\nRETURNING {identityColumn}";
            case "mysql":
                return trimmed + ";\nSELECT last_insert_id()";
            default:
                // JNT7003 rejects an unrecognized schema.Dialect before emission
                // ever reaches here; defensive only.
                return trimmed;
        }
    }

    private static int FindValuesKeyword(string sql)
    {
        // Quoted regions and comments are skipped: a column literally named
        // [values] or "values" in the INSERT column list must not match the
        // keyword, and neither must the word appearing inside a -- line
        // comment or /* block comment */ (e.g. "-- restore old values here")
        // -- splicing "output inserted.x" into the middle of a comment would
        // corrupt it into live SQL, same class of bug this already guards
        // against for quoted identifiers.
        bool inBracket = false, inString = false, inQuote = false;
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (inString) { if (c == '\'') inString = false; i++; continue; }
            if (inBracket) { if (c == ']') inBracket = false; i++; continue; }
            if (inQuote) { if (c == '"') inQuote = false; i++; continue; }
            if (c == '\'') { inString = true; i++; continue; }
            if (c == '[') { inBracket = true; i++; continue; }
            if (c == '"') { inQuote = true; i++; continue; }
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                    i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int end = i + 2;
                while (end + 1 < sql.Length && !(sql[end] == '*' && sql[end + 1] == '/'))
                    end++;
                i = end + 1 < sql.Length ? end + 2 : sql.Length;
                continue;
            }

            if (i + 6 <= sql.Length && string.Compare(sql, i, "values", 0, 6, StringComparison.OrdinalIgnoreCase) == 0)
            {
                bool startOk = i == 0 || !char.IsLetterOrDigit(sql[i - 1]) && sql[i - 1] != '_' && sql[i - 1] != '@';
                bool endOk = i + 6 == sql.Length || !char.IsLetterOrDigit(sql[i + 6]) && sql[i + 6] != '_';
                if (startOk && endOk)
                    return i;
            }
            i++;
        }
        return -1;
    }

    // AUD-R69-01: "readerVar" (default "reader") threads through to
    // GetReaderCall -- see that method's own comment. The one non-default
    // caller is CodeEmitter.Part3.cs's identity-insert path, passing
    // "__reader".
    private static string GetIdentityReaderCall(IdentityInfo identity, DatabaseSchema? schema = null, string readerVar = "reader")
    {
        // MySQL's LAST_INSERT_ID() is BIGINT UNSIGNED regardless of the key
        // type; read as long and narrow explicitly.
        //
        // AUD-R34-01: identity.Dialect is a bare, unnormalized string straight
        // from the JSON snapshot (see BuildIdentityInsertSql's own comment
        // above), and JNT7003 accepts any casing via
        // DialectMapper.IsKnownDialect's OrdinalIgnoreCase membership check --
        // so an ordinary, JNT7003-accepted "dialect": "MySql"/"MYSQL" must
        // compare case-insensitively here too, exactly like the sibling
        // dialect switches in this same file/fan-out (BuildIdentityInsertSql,
        // EmitUpsert in CodeEmitter.Part6.cs) already do.
        if (string.Equals(identity.Dialect, "mysql", StringComparison.OrdinalIgnoreCase) && identity.CSharpType != "long")
            return $"checked(({ShortenValueTypeName(schema, identity.CSharpType)}){readerVar}.GetInt64(0))";
        return GetReaderCall(identity.CSharpType, 0, schema, readerVar);
    }

    // AUD-R69-01: "__weOpened", not "weOpened" -- this helper is shared by 7
    // call sites across the emitter (Part3.cs, Part8.cs, Part11.cs,
    // Part12.cs, Part13.cs x3), every one of which now declares its own
    // local as "__weOpened" (round 68 had deliberately left this rename out
    // of Part11.cs's own fix specifically because this shared helper was not
    // yet updated in lockstep with all 7 declaration sites; this round does
    // exactly that, closing a residual carried forward since round 67).
    // Confirmed live that a schema/query parameter literally named
    // "WeOpened"/"@weOpened" collides (CS0136) with the un-prefixed local.
    private static void EmitFinallyClose(System.Text.StringBuilder sb, string connVar, bool isAsync)
    {
        sb.AppendLine("            }");
        sb.AppendLine("            finally");
        sb.AppendLine("            {");
        sb.AppendLine(isAsync
            ? $"                if (__weOpened) await {connVar}.CloseAsync().ConfigureAwait(false);"
            : $"                if (__weOpened) {connVar}.Close();");
        sb.AppendLine("            }");
    }

    /// <summary>
    /// True when <paramref name="csharpType"/> is a value type that cannot
    /// hold null. Every parameter-binding and bulk-copy branch keys off this
    /// to decide between a plain assignment and the
    /// <c>(object?)x ?? DBNull.Value</c> / <c>x is null</c> reference-type
    /// shapes.
    ///
    /// <paramref name="schema"/> is what makes a generated enum answer
    /// correctly (spec 013). The list below is closed and cannot name a type
    /// that did not exist when the generator was compiled, so without the
    /// schema an emitted enum reports false and takes the reference-type
    /// branches — which is not merely suboptimal: <c>CodeEmitter.Part13</c>'s
    /// bulk-copy path then emits <c>if (row.Status is null)</c> against a
    /// struct property, and the generated code does not compile at all.
    /// </summary>
    private static bool IsNonNullableValueType(string csharpType, DatabaseSchema? schema = null)
    {
        switch (csharpType)
        {
            case "int" or "long" or "short" or "byte" or "bool" or "decimal" or "double" or "float"
                or "uint" or "ulong" or "ushort"
                or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or "System.Guid":
                return true;
        }

        return IsEmittedEnumType(csharpType, schema);
    }

    /// <summary>
    /// True when <paramref name="csharpType"/> is the name of an enum this
    /// generator emits for <paramref name="schema"/>. Matches the
    /// non-nullable spelling only; a caller holding "OrderStatus?" trims the
    /// '?' first, exactly as it does for every other value type.
    /// </summary>
    internal static bool IsEmittedEnumType(string csharpType, DatabaseSchema? schema)
    {
        if (schema == null || schema.Enums.Count == 0 || string.IsNullOrEmpty(csharpType))
            return false;
        if (csharpType.EndsWith("?"))
            return false;

        foreach (var e in schema.Enums.Values)
        {
            if (e.Members.Count > 0 &&
                string.Equals(DialectMapper.EnumTypeName(e.Name), csharpType, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Maps an emitted C# parameter type to System.Data.DbType so providers skip
    /// runtime type inference. Returns null when no static mapping exists.
    /// </summary>
    private static string? MapCSharpTypeToAdoDbType(string csharpType)
    {
        string baseType = csharpType.EndsWith("?") ? csharpType.Substring(0, csharpType.Length - 1) : csharpType;
        return baseType switch
        {
            "int" => "Int32",
            "long" => "Int64",
            "short" => "Int16",
            "byte" => "Byte",
            "bool" => "Boolean",
            "decimal" => "Decimal",
            "double" => "Double",
            "float" => "Single",
            "string" => "String",
            "uint" => "UInt32",
            "ulong" => "UInt64",
            "ushort" => "UInt16",
            "System.DateTime" => "DateTime",
            "System.DateTimeOffset" => "DateTimeOffset",
            "System.TimeSpan" => "Time",
            "System.Guid" => "Guid",
            "byte[]" => "Binary",
            _ => null
        };
    }

    private static bool IsBoundColumnNullable(ParameterRef param, QueryModel query, DatabaseSchema? schema)
        => ResolveBoundColumn(param, query, schema, out _)?.IsNullable ?? false;

    internal static string InferCrudParameterType(ParameterRef param, QueryModel query, DatabaseSchema? schema, Directives.DirectiveModel? directives = null)
    {
        // Check @params directive first
        if (directives?.ExplicitParams != null)
        {
            foreach (var ep in directives.ExplicitParams)
            {
                if (string.Equals(ep.Name, param.Name, StringComparison.OrdinalIgnoreCase))
                    return ep.CSharpType;
            }
        }

        if (!string.IsNullOrEmpty(param.BoundColumnName) && schema != null)
        {
            // Alias-qualified first, then the CRUD target table, then a
            // unique unqualified match (see ResolveBoundColumn for why an
            // explicit qualifier must not be shadowed by the target table).
            var resolvedCol = ResolveBoundColumn(param, query, schema, out _);
            if (resolvedCol != null)
                return DialectMapper.MapColumnToCSharp(resolvedCol, schema.Dialect, schema);

            // Feature B fallback: for INSERT...SELECT the source tables are on
            // the model, and for a WITH chain the binding may resolve inside a
            // CTE body. Search every referenced table, then every CTE body's
            // tables, for a column of the bound name.
            var wide = ResolveBoundColumnWide(param.BoundColumnName, query, schema);
            if (wide != null)
                return wide;
        }

        return "object";
    }

    /// <summary>
    /// Resolves a bound-column name to a CLR type by scanning all tables the
    /// statement references and, failing that, the tables of every CTE body.
    /// Used for INSERT...SELECT and WITH chains where the target table is not
    /// where the parameter's column lives.
    /// </summary>
    private static string? ResolveBoundColumnWide(string columnName, QueryModel query, DatabaseSchema schema)
    {
        foreach (var table in query.Tables)
        {
            if (SchemaLookup.TryGetTable(schema, table.TableName, out var ts) &&
                SchemaLookup.TryGetColumn(ts!, columnName, out var col))
                return DialectMapper.MapColumnToCSharp(col!, schema.Dialect, schema);
        }

        foreach (var cte in query.Ctes)
        {
            foreach (var table in cte.Body.Tables)
            {
                if (SchemaLookup.TryGetTable(schema, table.TableName, out var ts) &&
                    SchemaLookup.TryGetColumn(ts!, columnName, out var col))
                    return DialectMapper.MapColumnToCSharp(col!, schema.Dialect, schema);
            }
        }

        // The bound name may be a CTE's VIRTUAL column (a declared column
        // list or an aliased output), not a real column of any body table:
        // trace it through the CTE outputs the same way projections resolve.
        foreach (var table in query.Tables)
        {
            var viaCte = ProjectionBuilder.ResolveThroughCtes(table.TableName, columnName, query.Ctes, schema, depth: 0);
            if (viaCte != null)
                return DialectMapper.MapColumnToCSharp(viaCte, schema.Dialect, schema);
        }

        return null;
    }

}
