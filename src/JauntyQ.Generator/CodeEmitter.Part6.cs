using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;
public static partial class CodeEmitter
{
    /// <summary>
    /// Manual replacement for string.Join(sep, cols.Select(selector)) --
    /// product code stays System.Linq-free for NativeAOT compatibility.
    /// </summary>
    private static string JoinColumns(List<ColumnSchema> cols, string separator, System.Func<ColumnSchema, string> selector)
    {
        if (cols.Count == 0)
            return "";
        var parts = new List<string>(cols.Count);
        foreach (var c in cols)
            parts.Add(selector(c));
        return string.Join(separator, parts);
    }

    /// <summary>
    /// Resolves the column set an auto-CRUD Upsert matches on before deciding
    /// insert-vs-update: the primary key when at least one PK column is not
    /// database-assigned, otherwise the first secondary UNIQUE index whose
    /// columns are all real, non-identity columns (an identity-only PK has
    /// no value to match on before the row exists — e.g. a queue table keyed
    /// by an idempotency token instead). Null when neither exists: callers
    /// skip Upsert synthesis for that table.
    /// </summary>
    internal static List<ColumnSchema>? ResolveUpsertKey(TableSchema tableSchema) =>
        UpsertKeyResolver.Resolve(tableSchema);

    /// <summary>
    /// Dialect-native upsert keyed on ResolveUpsertKey's result (the primary
    /// key, or a secondary UNIQUE index for an identity-only PK). Bypasses
    /// the SQL parser deliberately: MERGE / ON CONFLICT / ON DUPLICATE KEY
    /// are outside the minimal grammar, and the SQL is correct by
    /// construction from the schema snapshot.
    /// </summary>
    public static string EmitUpsert(string entityName, TableSchema tableSchema, string dialect)
    {
        var keyCols = ResolveUpsertKey(tableSchema)
            ?? throw new System.InvalidOperationException(
                $"Table '{tableSchema.Name}' has no usable upsert key: no primary key, or an " +
                "identity-only primary key with no secondary UNIQUE index to match on instead.");

        // CrudColumnRules.UpsertColumns/UpsertSetColumns (JauntyQ.Analysis) is
        // the single source of truth for this filtering -- AutoCrud.cs's
        // Upsert-synthesis gate and CodeEmitter.Part7.cs's EmitPocoOverloads
        // must both agree with the SQL built here byte-for-byte.
        var columns = CrudColumnRules.UpsertColumns(tableSchema.Columns.Values, keyCols);
        var setCols = CrudColumnRules.UpsertSetColumns(columns, keyCols);

        string colList = JoinColumns(columns, ", ", c => c.Name);
        string paramList = JoinColumns(columns, ", ", c => $"@{c.Name}");

        string sql;
        switch (dialect)
        {
            case "sqlserver":
            {
                string srcSelect = JoinColumns(columns, ", ", c => $"@{c.Name} as {c.Name}");
                string onClause = JoinColumns(keyCols, " and ", c => $"target.{c.Name} = src.{c.Name}");
                string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = src.{c.Name}");
                string insertVals = JoinColumns(columns, ", ", c => $"src.{c.Name}");
                sql = $"merge into {tableSchema.Name} with (holdlock) as target\n" +
                      $"using (select {srcSelect}) as src\n" +
                      $"on {onClause}\n" +
                      $"when matched then update set {updateSet}\n" +
                      $"when not matched then insert ({colList}) values ({insertVals});";
                break;
            }
            case "postgres":
            case "sqlite":
            {
                string conflictCols = JoinColumns(keyCols, ", ", c => c.Name);
                string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = excluded.{c.Name}");
                sql = $"insert into {tableSchema.Name} ({colList})\nvalues ({paramList})\n" +
                      $"on conflict ({conflictCols}) do update set {updateSet}";
                break;
            }
            case "mysql":
            {
                // VALUES(col) works on both MySQL and MariaDB (the 8.0.20+
                // alias form is not MariaDB-compatible). ON DUPLICATE KEY
                // does not name the conflicting key: MySQL/MariaDB detect it
                // from whichever UNIQUE constraint the insert violates.
                string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = values({c.Name})");
                sql = $"insert into {tableSchema.Name} ({colList})\nvalues ({paramList})\n" +
                      $"on duplicate key update {updateSet}";
                break;
            }
            default:
                throw new System.InvalidOperationException($"Upsert synthesis requires a known dialect (got '{dialect}').");
        }

        var paramInfos = new System.Collections.Generic.List<EmittedParam>();
        foreach (var col in columns)
        {
            paramInfos.Add(CreateEmittedParam(col.Name, DialectMapper.MapColumnToCSharp(col, dialect), col.IsNullable, col, tableSchema.Name, isWriteTarget: true));
        }

        var stub = new QueryModel { Name = "Upsert" };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "_conn", isStatic: false, isAsync: false, dialect: dialect);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "conn", isStatic: true, isAsync: false, dialect: dialect);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "_conn", isStatic: false, isAsync: true, dialect: dialect);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "conn", isStatic: true, isAsync: true, dialect: dialect);
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

}
