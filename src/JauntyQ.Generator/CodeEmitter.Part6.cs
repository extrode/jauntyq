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
    public static string EmitUpsert(string entityName, TableSchema tableSchema, string dialect, DatabaseSchema? schema = null)
    {
        var keyCols = ResolveUpsertKey(tableSchema)
            ?? throw new System.InvalidOperationException(
                $"Table '{tableSchema.Name}' has no usable upsert key: no primary key, or an " +
                "identity-only primary key with no secondary UNIQUE index to match on instead.");

        // AUD-R37-01: keyCols may legitimately contain a Computed/RowVersion
        // primary-key column (post-AUD-R36-01, UpsertKeyResolver.Resolve
        // agrees with AutoCrud's own PK detection for this same table), but
        // CrudColumnRules.UpsertColumns excludes such a column outright --
        // unlike Identity, it has no "unless part of key" escape hatch,
        // because no dialect accepts an explicit INSERT value for either.
        // AutoCrud.Synthesize's gate already skips Upsert synthesis for such
        // a table entirely (matching "no usable key"); this is the same
        // defensive backstop the null-key throw above already is, for any
        // caller invoking EmitUpsert directly. Without it, the SQL Server
        // branch below would reference the excluded column in its "on"/
        // src-derived-table clause without ever selecting it, and a live
        // engine rejects the query at runtime ("Invalid column name",
        // confirmed via Testcontainers against SQL Server 2022).
        if (CrudColumnRules.HasUnbindableUpsertKeyColumn(tableSchema.Columns.Values, keyCols))
            throw new System.InvalidOperationException(
                $"Table '{tableSchema.Name}' has no usable upsert key: its primary key contains a " +
                "Computed or RowVersion column, and no dialect can bind a value for one on the " +
                "INSERT/conflict-target side of an upsert.");

        // CrudColumnRules.UpsertColumns/UpsertSetColumns (JauntyQ.Analysis) is
        // the single source of truth for this filtering -- AutoCrud.cs's
        // Upsert-synthesis gate and CodeEmitter.Part7.cs's EmitPocoOverloads
        // must both agree with the SQL built here byte-for-byte.
        var columns = CrudColumnRules.UpsertColumns(tableSchema.Columns.Values, keyCols);
        var setCols = CrudColumnRules.UpsertSetColumns(columns, keyCols);

        string colList = JoinColumns(columns, ", ", c => c.Name);
        string paramList = JoinColumns(columns, ", ", c => $"@{c.Name}");

        string sql;
        // AUD-R10-03: schema.Dialect is a bare, unnormalized string straight
        // from the JSON snapshot, and JNT7003 accepts any casing via
        // DialectMapper.IsKnownDialect's OrdinalIgnoreCase check -- so a
        // JNT7003-accepted "dialect": "SqlServer" must not fall through to
        // default and crash the generator. ToLowerInvariant matches
        // OrdinalIgnoreCase semantics for these ASCII-only case labels.
        switch (dialect.ToLowerInvariant())
        {
            case "sqlserver":
            {
                string srcSelect = JoinColumns(columns, ", ", c => $"@{c.Name} AS {c.Name}");
                string onClause = JoinColumns(keyCols, " AND ", c => $"target.{c.Name} = src.{c.Name}");
                string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = src.{c.Name}");
                string insertVals = JoinColumns(columns, ", ", c => $"src.{c.Name}");
                sql = $"MERGE INTO {tableSchema.Name} WITH (HOLDLOCK) AS target\n" +
                      $"USING (SELECT {srcSelect}) AS src\n" +
                      $"ON {onClause}\n" +
                      $"WHEN MATCHED THEN UPDATE SET {updateSet}\n" +
                      $"WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({insertVals});";
                break;
            }
            case "postgres":
            case "sqlite":
            {
                string conflictCols = JoinColumns(keyCols, ", ", c => c.Name);
                string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = EXCLUDED.{c.Name}");
                sql = $"INSERT INTO {tableSchema.Name} ({colList})\nVALUES ({paramList})\n" +
                      $"ON CONFLICT ({conflictCols}) DO UPDATE SET {updateSet}";
                break;
            }
            case "mysql":
            {
                // AUD-R4-16: ON DUPLICATE KEY UPDATE names no conflict target
                // -- MySQL/MariaDB match whichever UNIQUE the insert violates.
                // With exactly one UNIQUE on the table that is the key
                // ResolveUpsertKey picked, so the statement agrees with
                // postgres/sqlite's ON CONFLICT (cols) and sqlserver's MERGE
                // ... ON, and it stays. With a competing UNIQUE it does not
                // agree, and a row can be matched on a key the caller never
                // asked about.
                if (!UpsertKeyResolver.HasCompetingUniqueConstraint(tableSchema, keyCols))
                {
                    // VALUES(col) works on both MySQL and MariaDB (the 8.0.20+
                    // alias form is not MariaDB-compatible).
                    string updateSet = JoinColumns(setCols, ", ", c => $"{c.Name} = VALUES({c.Name})");
                    sql = $"INSERT INTO {tableSchema.Name} ({colList})\nVALUES ({paramList})\n" +
                          $"ON DUPLICATE KEY UPDATE {updateSet}";
                    break;
                }

                // Key-targeted form, for the competing-UNIQUE case only. Two
                // statements in one CommandText, which MySqlConnector executes
                // under a default connection string on both engines, with
                // @key bound once and referenced three times (all measured in
                // MySqlUpsertProbeTests before this was written).
                //
                // NOT EXISTS, deliberately, and not ROW_COUNT() = 0: after an
                // UPDATE that matched a row but changed no values ROW_COUNT()
                // is 1 or 0 depending on the consumer's UseAffectedRows
                // setting -- their connection string, not ours -- so an upsert
                // re-run with identical values would fall through to the
                // INSERT and take a duplicate-key error. NOT EXISTS asks the
                // question the conflict key actually poses and is immune to
                // that setting.
                //
                // FROM DUAL because MySQL rejects a WHERE on a SELECT with no
                // FROM. Both engines accept it.
                //
                // This form is not atomic the way ON DUPLICATE KEY UPDATE is:
                // two sessions upserting the same new key can both see NOT
                // EXISTS true and both insert, and one takes error 1062.
                // JNT2018 says so at generation time; the generated method
                // already honours an ambient transaction, which is the remedy.
                {
                    string setClause = JoinColumns(setCols, ", ", c => $"{c.Name} = @{c.Name}");
                    string keyPredicate = JoinColumns(keyCols, " AND ", c => $"{c.Name} = @{c.Name}");
                    sql = $"UPDATE {tableSchema.Name} SET {setClause} WHERE {keyPredicate};\n" +
                          $"INSERT INTO {tableSchema.Name} ({colList})\n" +
                          $"SELECT {paramList} FROM DUAL\n" +
                          $" WHERE NOT EXISTS (SELECT 1 FROM {tableSchema.Name} WHERE {keyPredicate})";
                }
                break;
            }
            default:
                // JNT7003 rejects an unrecognized schema.Dialect (case-insensitively)
                // before emission ever reaches here; defensive only.
                throw new System.InvalidOperationException($"Upsert synthesis requires a known dialect (got '{dialect}').");
        }

        var paramInfos = new System.Collections.Generic.List<EmittedParam>();
        foreach (var col in columns)
        {
            paramInfos.Add(CreateEmittedParam(col.Name, DialectMapper.MapColumnToCSharp(col, dialect, schema), col.IsNullable, col, tableSchema.Name, isWriteTarget: true));
        }

        var stub = new QueryModel { Name = "Upsert" };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        EmitUsings(sb, dialect, needsEnumeratorCancellation: false);
        sb.AppendLine("namespace JauntyQ.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    public partial class {entityName}");
        sb.AppendLine("    {");
        // AUD-R70-01: "this._conn", not "_conn" -- see CodeEmitter.cs's
        // identical fix comment.
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "this._conn", isStatic: false, isAsync: false, schema: schema);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "conn", isStatic: true, isAsync: false, schema: schema);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "this._conn", isStatic: false, isAsync: true, schema: schema);
        sb.AppendLine();
        EmitCrudMethodBody(sb, stub, sql, paramInfos, "conn", isStatic: true, isAsync: true, schema: schema);
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

}
