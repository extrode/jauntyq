using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    private static void EmitColumnAssignments(System.Text.StringBuilder sb, ProjectionModel projection, string indent, DatabaseSchema? schema = null)
    {
        for (int i = 0; i < projection.Columns.Count; i++)
        {
            var col = projection.Columns[i];
            string readerCall = GetReaderCall(col.Type, col.Ordinal, schema);
            string comma = i < projection.Columns.Count - 1 ? "," : "";
            sb.AppendLine($"{indent}{IdentifierGuard.Escape(col.Name)} = {readerCall}{comma}");
        }
    }

    // AUD-R69-01: "readerVar", default "reader" -- every existing caller of
    // this method (EmitColumnAssignments, and the canonical-row-type /
    // proc-call-result mapper methods in Part5.cs/Part10.cs) is itself a
    // standalone `__Map...(DbDataReader reader)`-style method whose OWN sole
    // parameter is named "reader" and which takes no other parameters a
    // schema/query name could collide with -- structurally immune, so the
    // default is left as the friendly "reader" name for all of those. The one
    // caller that is NOT immune is CodeEmitter.Part3.cs's identity-insert
    // path, which declares its own `DbDataReader` LOCAL (renamed to
    // "__reader" as part of this round's fix, since a query parameter
    // literally named "@reader" collides with it, confirmed live) and reads
    // the identity value back via GetIdentityReaderCall -&gt; GetReaderCall;
    // that one call site passes readerVar: "__reader" explicitly.
    private static string GetReaderCall(string csharpType, int ordinal, DatabaseSchema? schema = null, string readerVar = "reader")
    {
        // Handle nullable types
        bool isNullable = csharpType.EndsWith("?");
        string baseType = isNullable ? csharpType.TrimEnd('?') : csharpType;

        string getMethod = baseType switch
        {
            "int" => $"{readerVar}.GetInt32({ordinal})",
            "long" => $"{readerVar}.GetInt64({ordinal})",
            "short" => $"{readerVar}.GetInt16({ordinal})",
            "byte" => $"{readerVar}.GetByte({ordinal})",
            "bool" => $"{readerVar}.GetBoolean({ordinal})",
            "decimal" => $"{readerVar}.GetDecimal({ordinal})",
            "double" => $"{readerVar}.GetDouble({ordinal})",
            "float" => $"{readerVar}.GetFloat({ordinal})",
            "string" => $"{readerVar}.GetString({ordinal})",
            "System.DateTime" => $"{readerVar}.GetDateTime({ordinal})",
            "System.Guid" => $"{readerVar}.GetGuid({ordinal})",
            // TimeSpan (Postgres "time"), IPAddress ("inet"/"cidr"), and every
            // array type (Postgres "int[]", "text[]", "uuid[]", ...) fell
            // through to the bare reader.GetValue(ordinal) below, which is
            // typed `object` -- an implicit object-to-T assignment at the
            // property initializer is CS0266. Confirmed live against Npgsql
            // that GetValue already returns the exact target runtime type in
            // every case (System.Net.IPAddress, System.TimeSpan, int[],
            // string[], Guid[]), so an explicit cast (matching the existing
            // "byte[]" case just below) is all that's missing.
            "System.TimeSpan" => $"({ShortenValueTypeName(schema, "System.TimeSpan")}){readerVar}.GetValue({ordinal})",
            // IPAddress is the one DialectMapper value type that stays fully
            // qualified even at the point of emission: unlike DateTime/Guid/
            // TimeSpan/DateTimeOffset (all resolvable via the `using System;`
            // every per-file emission already adds), a bare "IPAddress" needs
            // its own `using System.Net;`, which is not worth adding solely
            // for this rare (Postgres inet/cidr) case.
            "System.Net.IPAddress" => $"(System.Net.IPAddress){readerVar}.GetValue({ordinal})",
            // DialectMapper maps SQL Server "datetimeoffset" and Postgres "time
            // with time zone" to System.DateTimeOffset (task #26), which has no
            // dedicated IDataReader.Get* method either -- same CS0266 hazard as
            // TimeSpan/IPAddress above if left as bare GetValue.
            "System.DateTimeOffset" => $"({ShortenValueTypeName(schema, "System.DateTimeOffset")}){readerVar}.GetValue({ordinal})",
            // DialectMapper maps MySQL's UNSIGNED int/bigint/smallint to
            // uint/ulong/ushort (their signed CLR counterparts can't hold the
            // full unsigned range) -- MySqlConnector's GetValue returns
            // exactly these CLR types for such columns (confirmed live), so
            // the same explicit-cast pattern as TimeSpan/IPAddress above
            // applies; none of these three have a dedicated IDataReader.Get*
            // method either.
            "uint" => $"(uint){readerVar}.GetValue({ordinal})",
            "ulong" => $"(ulong){readerVar}.GetValue({ordinal})",
            "ushort" => $"(ushort){readerVar}.GetValue({ordinal})",
            "byte[]" => $"(byte[]){readerVar}.GetValue({ordinal})",
            _ when baseType.EndsWith("[]") => $"({baseType}){readerVar}.GetValue({ordinal})",
            _ => $"{readerVar}.GetValue({ordinal})"
        };

        if (isNullable)
        {
            // Bare `default` here infers the conditional's natural type from the getMethod arm
            // (a non-nullable value type), yielding e.g. DateTime.MinValue instead of null for
            // NULL columns. Carry the full nullable type explicitly so the null arm is truly null.
            return $"{readerVar}.IsDBNull({ordinal}) ? default({csharpType}) : {getMethod}";
        }

        // String is a reference type — always needs null check
        if (csharpType == "string")
        {
            return $"{readerVar}.IsDBNull({ordinal}) ? null! : {getMethod}";
        }

        return getMethod;
    }

    public static string InferParameterType(string paramName, QueryModel query, ProjectionModel projection, DatabaseSchema? schema = null, Directives.DirectiveModel? directives = null)
    {
        // 0. Check @params directive first (a full, unwrapped override)
        if (directives?.ExplicitParams != null)
        {
            foreach (var ep in directives.ExplicitParams)
            {
                if (string.Equals(ep.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return ep.CSharpType;
            }
        }

        string elementType = InferScalarParameterType(paramName, query, projection, schema);

        bool isEach = directives?.EachParams != null &&
            directives.EachParams.Exists(n => string.Equals(n, paramName, StringComparison.OrdinalIgnoreCase));

        return isEach ? $"System.Collections.Generic.IReadOnlyList<{elementType}>" : elementType;
    }

    private static string InferScalarParameterType(string paramName, QueryModel query, ProjectionModel projection, DatabaseSchema? schema)
    {
        // 1. Try binding-based inference (col = @param parsed by ExtractParameterBindings)
        var paramRef = query.Parameters.Find(p => p.Name == paramName);
        if (paramRef != null && !string.IsNullOrEmpty(paramRef.BoundColumnName) && schema != null)
        {
            var resolved = ResolveColumnType(paramRef.BoundTableAlias, paramRef.BoundColumnName, query, schema);
            if (resolved != null)
                return resolved;
        }

        // 2. Fallback: match parameter name to a projection column name
        foreach (var col in projection.Columns)
        {
            if (col.Name.Equals(DialectMapper.ToPascalCase(paramName), StringComparison.OrdinalIgnoreCase))
            {
                return col.Type;
            }
        }

        return "object";
    }

    /// <summary>
    /// Extracts T back out of the "System.Collections.Generic.IReadOnlyList&lt;T&gt;"
    /// shape InferParameterType wraps -- @each params in. Safe only because that
    /// wrapping is the sole producer of this exact string shape.
    /// </summary>
    private static string GetEachElementType(string csharpType)
    {
        const string prefix = "System.Collections.Generic.IReadOnlyList<";
        return csharpType.Substring(prefix.Length, csharpType.Length - prefix.Length - 1);
    }

    private static string? ResolveColumnType(string tableAlias, string columnName, QueryModel query, DatabaseSchema schema)
    {
        if (!string.IsNullOrEmpty(tableAlias))
        {
            // Qualified — resolve alias to table name
            string? tableName = ResolveAlias(tableAlias, query);
            if (tableName != null)
            {
                if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema) &&
                    SchemaLookup.TryGetColumn(tableSchema!, columnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema!, schema.Dialect);
                }
                // Not a schema table: the qualifier may name a CTE whose
                // virtual column traces back to a real one.
                var qualifiedViaCte = ProjectionBuilder.ResolveThroughCtes(tableName, columnName, query.Ctes, schema, depth: 0);
                if (qualifiedViaCte != null)
                    return DialectMapper.MapColumnToCSharp(qualifiedViaCte, schema.Dialect);
            }
        }
        else
        {
            // Unqualified — search all referenced tables
            foreach (var table in query.Tables)
            {
                if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema) &&
                    SchemaLookup.TryGetColumn(tableSchema!, columnName, out var colSchema))
                {
                    return DialectMapper.MapColumnToCSharp(colSchema!, schema.Dialect);
                }
            }

            // Not in any schema table: it may be a CTE's virtual column
            // (declared column list or aliased output).
            foreach (var table in query.Tables)
            {
                var viaCte = ProjectionBuilder.ResolveThroughCtes(table.TableName, columnName, query.Ctes, schema, depth: 0);
                if (viaCte != null)
                    return DialectMapper.MapColumnToCSharp(viaCte, schema.Dialect);
            }
        }

        return null;
    }

    private static string? ResolveAlias(string alias, QueryModel query)
    {
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            if (string.Equals(key, alias, StringComparison.OrdinalIgnoreCase))
                return table.TableName;
        }
        return null;
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static string? ResolveProcName(Directives.DirectiveModel? directives, string entityName, string methodName)
    {
        if (directives == null || !directives.IsProc)
            return null;
        return directives.ProcName ?? $"{entityName}_{methodName}";
    }

    /// <summary>
    /// Strips leading blank lines and leading whole-line `--` comments so
    /// commented-out old queries at the top of a .sql file are not embedded
    /// into every generated CommandText. Mid-query comments are preserved.
    /// </summary>
    internal static string StripLeadingSqlComments(string sql)
    {
        var lines = sql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int start = 0;
        while (start < lines.Length)
        {
            var trimmed = lines[start].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("--"))
            {
                start++;
                continue;
            }
            break;
        }
        return string.Join("\n", lines, start, lines.Length - start).Trim();
    }

}
