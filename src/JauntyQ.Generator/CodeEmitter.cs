using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static partial class CodeEmitter
{
    /// <summary>
    /// query.Parameters is ordered by each @name token's first appearance in
    /// the SQL text, not by any author-declared order. When a `-- @params`
    /// directive is present, its list is the one place a query author
    /// explicitly states the intended parameter order — honor it, so the
    /// generated method signature matches what a caller reading that
    /// directive expects instead of silently differing (surprising for
    /// positional calls, a CS1503 or silently-swapped argument otherwise).
    /// Parameters the directive didn't mention (auto-inferred bound-column
    /// params) keep their original first-appearance order, appended after.
    /// </summary>
    private static System.Collections.Generic.List<ParameterRef> OrderedParameters(QueryModel query, Directives.DirectiveModel? directives)
    {
        if (directives?.ExplicitParams == null || directives.ExplicitParams.Count == 0)
            return query.Parameters;

        var ordered = new System.Collections.Generic.List<ParameterRef>(query.Parameters.Count);
        var used = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ep in directives.ExplicitParams)
        {
            var match = query.Parameters.Find(p => string.Equals(p.Name, ep.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null && used.Add(match.Name))
                ordered.Add(match);
        }
        foreach (var p in query.Parameters)
        {
            if (used.Add(p.Name))
                ordered.Add(p);
        }
        return ordered;
    }

    /// <summary>
    /// True when some table in <paramref name="schema"/> generates a
    /// top-level type named exactly <paramref name="simpleName"/> in
    /// <c>namespace JauntyQ.Generated</c>. Two table-derived names land
    /// there per table, both from a fully user-controlled table name:
    /// the entity accessor class (<see cref="DialectMapper.ToPascalCase"/> --
    /// <c>public partial class {name}</c>) and the row POCO
    /// (<see cref="Inflector.RowTypeName"/>, the singularized entity name --
    /// <c>public class {name}</c>). Either shadows the BCL name of the same
    /// simple name for every file compiled into that namespace, not merely
    /// the file for that one table: a table named "task" shadows via its
    /// entity name, and a table named "date_times" shadows via its row POCO
    /// "DateTime" (AUD-R50-02) even though its entity name "DateTimes"
    /// collides with nothing. The row-POCO name is checked for every table
    /// regardless of whether a POCO is actually emitted for it this run --
    /// over-qualifying costs only cosmetics, under-qualifying breaks the
    /// build. Only table-derived names are checked (not hand-authored .sql
    /// entity names, which a developer chooses and is unlikely to pick as a
    /// BCL/provider type name by accident); this is the realistic,
    /// demonstrated collision shape, matching the PascalCase-folding defect
    /// class JNT2009/JNT2010 already guard.
    /// </summary>
    private static bool SchemaHasEntityNamed(DatabaseSchema? schema, string simpleName)
    {
        if (schema == null)
            return false;

        foreach (var table in schema.Tables.Values)
        {
            string entityName = DialectMapper.ToPascalCase(table.Name);
            if (string.Equals(entityName, simpleName, StringComparison.Ordinal))
                return true;
            if (string.Equals(Inflector.RowTypeName(entityName), simpleName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A BCL/provider type reference that survives the entity-name shadowing
    /// <see cref="SchemaHasEntityNamed"/> documents: <c>global::</c>-qualified
    /// exactly when the schema has an entity named <paramref name="simpleName"/>
    /// (guaranteed collision-proof regardless of which name segment collides),
    /// otherwise the short name -- the caller is expected to have added a
    /// <c>using {@namespace};</c> directive so the short form resolves.
    /// </summary>
    internal static string TypeRef(DatabaseSchema? schema, string simpleName, string @namespace)
    {
        if (!SchemaHasEntityNamed(schema, simpleName))
            return simpleName;

        return @namespace.Length == 0 ? $"global::{simpleName}" : $"global::{@namespace}.{simpleName}";
    }

    /// <summary>
    /// Shortens the fixed set of fully-qualified value types
    /// <see cref="DialectMapper.MapDbTypeToCSharp"/> emits (System.DateTime,
    /// System.Guid, System.TimeSpan, System.DateTimeOffset -- all resolvable
    /// via the <c>using System;</c> every per-file emission already adds) for
    /// display in emitted source, routed through <see cref="TypeRef"/> for the
    /// same collision guard as every other shortened type. Deliberately does
    /// NOT touch <see cref="DialectMapper.MapDbTypeToCSharp"/>'s own return
    /// value or <c>System.Net.IPAddress</c> (its "?"-stripped, fully-qualified
    /// form is a comparison key at several coordination points -- GetReaderCall
    /// in CodeEmitter.Part9.cs, IsNonNullableValueType/MapCSharpTypeToAdoDbType
    /// in Part4.cs -- so only the display copy at the point of emission is
    /// shortened, never the value flowing through inference/comparison logic).
    /// </summary>
    internal static string ShortenValueTypeName(DatabaseSchema? schema, string csharpType)
    {
        bool nullable = csharpType.EndsWith("?", StringComparison.Ordinal);
        string baseType = nullable ? csharpType.Substring(0, csharpType.Length - 1) : csharpType;
        string? simpleName = baseType switch
        {
            "System.DateTime" => "DateTime",
            "System.DateTimeOffset" => "DateTimeOffset",
            "System.TimeSpan" => "TimeSpan",
            "System.Guid" => "Guid",
            _ => null
        };
        if (simpleName == null)
            return csharpType;

        string shortened = TypeRef(schema, simpleName, "System");
        return nullable ? shortened + "?" : shortened;
    }

    /// <summary>
    /// The base set of usings every per-file query/CRUD emission needs, plus
    /// the provider namespace for whichever dialect this schema targets (only
    /// the dialect actually in play emits code that needs it -- e.g. Npgsql
    /// types only appear when EmitParameterBinding takes its postgres
    /// branch) and System.Runtime.CompilerServices only for files that emit
    /// an async streaming iterator (the only place [EnumeratorCancellation]
    /// is used).
    /// </summary>
    private static void EmitUsings(System.Text.StringBuilder sb, string? dialect, bool needsEnumeratorCancellation)
    {
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Data;");
        sb.AppendLine("using System.Data.Common;");
        if (needsEnumeratorCancellation)
            sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Text;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        if (string.Equals(dialect, "postgres", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine("using Npgsql;");
        sb.AppendLine();
    }

    public static string Emit(QueryModel query, ProjectionModel projection, string originalSql, string entityName, DatabaseSchema? schema = null, Directives.DirectiveModel? directives = null, string? canonicalRowType = null)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        EmitUsings(sb, schema?.Dialect, needsEnumeratorCancellation: directives?.IsStream == true);

        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");

        // Entity partial class containing nested Result class and query methods
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");

        // Full-row projections return the canonical per-table POCO (e.g.
        // Shipper) instead of a query-specific nested Result type; only
        // genuine custom projections (joins, partial selects) get their own.
        if (canonicalRowType == null)
        {
            sb.AppendLine("        public static partial class Result");
            sb.AppendLine("        {");
            EmitDto(sb, projection, schema);
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Expected result-set column names for the one-time shape guard
        EmitColumnNames(sb, query.Name, projection);
        sb.AppendLine();

        // Resolve proc name (null if not in proc mode)
        string? procName = ResolveProcName(directives, entityName, query.Name);

        bool isFirst = directives?.IsFirst == true;
        bool isStream = directives?.IsStream == true;

        // Query methods (use Result.X as the type)
        EmitQueryMethods(sb, query, projection, originalSql, entityName, schema, directives, procName, isFirst, canonicalRowType, isStream);

        // Emit Proc nested class with CREATE PROCEDURE script
        if (procName != null)
        {
            sb.AppendLine();
            EmitProcScript(sb, procName, query.Name, originalSql, query, projection, schema, directives, isCrud: false);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string EmitCrud(QueryModel query, string originalSql, string entityName, DatabaseSchema? schema = null, Directives.DirectiveModel? directives = null)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        EmitUsings(sb, schema?.Dialect, needsEnumeratorCancellation: false);

        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");

        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");

        // Build parameter lists
        var paramInfos = new System.Collections.Generic.List<EmittedParam>();
        foreach (var param in OrderedParameters(query, directives))
        {
            string paramType = InferCrudParameterType(param, query, schema, directives);
            bool isNullable = IsBoundColumnNullable(param, query, schema);
            var column = ResolveBoundColumn(param, query, schema, out string? boundTable);
            paramInfos.Add(CreateEmittedParam(param.Name, paramType, isNullable, column, boundTable, param.IsWriteTarget));
        }

        // Resolve proc name (null if not in proc mode)
        string? procName = ResolveProcName(directives, entityName, query.Name);

        // -- @identity: resolve the single identity key column of the target
        // table (the generator validates preconditions and reports JNT7001;
        // by the time we emit, this either resolves or the file was skipped).
        IdentityInfo? identity = ResolveIdentityInfo(query, schema, directives);

        // Instance sync
        EmitCrudMethodBody(sb, query, originalSql, paramInfos, "_conn", isStatic: false, isAsync: false, procName: procName, identity: identity, schema: schema);
        sb.AppendLine();
        // Static sync
        EmitCrudMethodBody(sb, query, originalSql, paramInfos, "conn", isStatic: true, isAsync: false, procName: procName, identity: identity, schema: schema);
        sb.AppendLine();
        // Instance async
        EmitCrudMethodBody(sb, query, originalSql, paramInfos, "_conn", isStatic: false, isAsync: true, procName: procName, identity: identity, schema: schema);
        sb.AppendLine();
        // Static async
        EmitCrudMethodBody(sb, query, originalSql, paramInfos, "conn", isStatic: true, isAsync: true, procName: procName, identity: identity, schema: schema);

        // Emit Proc nested class with CREATE PROCEDURE script
        if (procName != null)
        {
            sb.AppendLine();
            EmitProcScript(sb, procName, query.Name, originalSql, query, null, schema, directives, isCrud: true);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Source for the shared one-time result-set shape guard. Registered by the
    /// generator as post-initialization output so it exists exactly once.
    /// </summary>
    public static string EmitShapeGuardSource()
    {
        return """
// <auto-generated/>
#nullable enable

namespace JauntyQ.Generated
{
    /// <summary>
    /// Validates the result-set shape against the schema snapshot. The caller
    /// latches this so it runs on the first execution of each query per process
    /// and never again (never per row, never per subsequent call). Catches drift
    /// between the live database and the snapshot instead of silently mapping
    /// wrong ordinals.
    /// </summary>
    public static class JauntyQShapeGuard
    {
        public static void Validate(System.Data.Common.DbDataReader reader, string[] expectedColumns, string queryId)
        {
            if (reader.FieldCount < expectedColumns.Length)
            {
                throw new System.InvalidOperationException(
                    $"JauntyQ schema drift in {queryId}: expected {expectedColumns.Length} columns but the result set has {reader.FieldCount}. " +
                    "The database no longer matches the schema snapshot; re-run 'jaunty schema pull' and rebuild.");
            }

            for (int i = 0; i < expectedColumns.Length; i++)
            {
                string actual = reader.GetName(i);
                if (!string.Equals(actual, expectedColumns[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new System.InvalidOperationException(
                        $"JauntyQ schema drift in {queryId}: expected column '{expectedColumns[i]}' at ordinal {i} but the result set returned '{actual}'. " +
                        "The database no longer matches the schema snapshot; re-run 'jaunty schema pull' and rebuild.");
                }
            }
        }
    }
}
""";
    }

    private readonly struct EmittedParam
    {
        public readonly string Name;
        public readonly string CSharpType;

        /// <summary>
        /// Emission-safe C# identifier for the parameter: '@'-prefixed when
        /// <see cref="Name"/> is a C# keyword (e.g. a column named 'ref' or
        /// 'operator'). Use this in every CODE position (declaration, value
        /// reads, nameof); use the raw <see cref="Name"/> in STRING positions
        /// (DbParameter.ParameterName, message text), where the SQL-side name
        /// must survive verbatim.
        /// </summary>
        public string CSharpName => IdentifierGuard.Escape(Name);
        public readonly bool IsNullable;
        public readonly int? MaxLength;       // chars for text, bytes for binary; -1 = unbounded (MAX)
        public readonly int? Precision;
        public readonly int? Scale;
        public readonly bool IsWriteTarget;   // INSERT value / UPDATE SET / upsert column
        public readonly string? ColumnDisplay; // "table.column" for guard messages
        public readonly bool IsEach;          // -- @each: CSharpType is IReadOnlyList<T>, expanded as an IN-list at runtime

        public EmittedParam(string name, string csharpType, bool isNullable = false,
            int? maxLength = null, int? precision = null, int? scale = null,
            bool isWriteTarget = false, string? columnDisplay = null, bool isEach = false)
        {
            Name = name;
            CSharpType = csharpType;
            IsNullable = isNullable;
            MaxLength = maxLength;
            Precision = precision;
            Scale = scale;
            IsWriteTarget = isWriteTarget;
            ColumnDisplay = columnDisplay;
            IsEach = isEach;
        }
    }

}
