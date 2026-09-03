using Extrode.JauntyQ.Schema;
using Microsoft.Data.Sqlite;

namespace Extrode.JauntyQ.Schema.Extraction;

public class SqliteExtractor : ISchemaExtractor
{
    public async Task<DatabaseSchema> ExtractAsync(string connectionString)
    {
        var schema = new DatabaseSchema();

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        // Get all relation names. Spec 015: views come along, flagged, because
        // PRAGMA table_xinfo works on a view exactly as it does on a table --
        // SQLite reports a view's projected columns through the same pragma,
        // so nothing below needs a second code path.
        var relations = new List<(string Name, bool IsView)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name, type FROM sqlite_master " +
                "WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                relations.Add((reader.GetString(0), reader.GetString(1) == "view"));
            }
        }

        // For each relation, get columns via PRAGMA
        foreach (var (tableName, isView) in relations)
        {
            var tableSchema = new TableSchema
            {
                Name = tableName,
                IsView = isView,
                Columns = new Dictionary<string, ColumnSchema>()
            };

            // Declared types that are exactly "INTEGER" (no facet, no other
            // keywords) are tracked separately from the normalized DbType:
            // SQLite only aliases a single-column PRIMARY KEY to the rowid --
            // gaining auto-assigned, auto-incrementing values -- when the
            // column's declared type is the literal word "INTEGER". Confirmed
            // live (sqlite3 CLI): "id BIGINT PRIMARY KEY", "id INT PRIMARY
            // KEY", and even "id INTEGER(10) PRIMARY KEY" all insert NULL for
            // an omitted id (no rowid aliasing), while only "id INTEGER
            // PRIMARY KEY" auto-assigns 1, 2, 3, .... NormalizeSqliteType
            // collapses SMALLINT/MEDIUMINT/TINYINT/INT/INTEGER down to the
            // single DbType "int" (keeping BIGINT distinct -- see
            // NormalizeSqliteType), so that normalized value alone can't be
            // used to decide IsIdentity -- doing so previously mismarked a
            // "BIGINT PRIMARY KEY" column as an auto-generated identity, which
            // made AutoCrud drop it from the generated INSERT column list
            // entirely, silently inserting NULL into it every time.
            var rowidAliasCandidates = new HashSet<string>();

            await using (var cmd = conn.CreateCommand())
            {
                // table_xinfo (not table_info): identical columns 0-5 plus a
                // 7th "hidden" column that table_info doesn't expose at all.
                // hidden=2/3 marks a GENERATED ALWAYS AS (expr) VIRTUAL/STORED
                // column (SQLite 3.31+). Confirmed live (sqlite3 CLI):
                // table_info doesn't just fail to flag a generated column as
                // computed, it OMITS the column from its result set entirely
                // (both STORED and VIRTUAL) -- so before this fix a
                // live-pulled generated column vanished from the schema
                // altogether (same failure class as the MigrationParser
                // computed-column drop), not merely reported IsComputed=false.
                // table_xinfo is required to see the column at all, and its
                // hidden value is what makes IsComputed derivable once it's
                // visible.
                cmd.CommandText = $"PRAGMA table_xinfo(\"{tableName.Replace("\"", "\"\"")}\")";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string columnName = reader.GetString(1);   // name
                    string dataType = reader.GetString(2);     // type
                    bool notNull = reader.GetInt32(3) == 1;    // notnull
                    bool isPk = reader.GetInt32(5) > 0;        // pk (ordinal within the PK, 0 = not part of it)
                    int hidden = reader.GetInt32(6);           // 0=normal, 1=hidden, 2=VIRTUAL, 3=STORED
                    bool isComputed = hidden is 2 or 3;

                    // SQLite has no length metadata catalog; honor what the
                    // declared type says (VARCHAR(40), DECIMAL(10,2)).
                    string normalized = NormalizeSqliteType(dataType);
                    var (first, second) = ParseDeclaredNumbers(dataType);

                    if (dataType.Trim().Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
                        rowidAliasCandidates.Add(columnName);

                    tableSchema.Columns[columnName] = new ColumnSchema
                    {
                        Name = columnName,
                        DbType = normalized,
                        IsNullable = !notNull,
                        IsPrimaryKey = isPk,
                        MaxLength = normalized is "varchar" or "bytea" ? first : null,
                        Precision = normalized == "decimal" ? first : null,
                        Scale = normalized == "decimal" ? second : null,
                        // SQLite stores text as UTF-8/UTF-16
                        IsUnicode = normalized == "varchar" ? true : null,
                        IsComputed = isComputed
                    };
                }
            }

            // SQLite auto-assigns rowid for a single-column INTEGER PRIMARY KEY
            // (declared exactly as "INTEGER" -- see rowidAliasCandidates above).
            var pkCols = new List<ColumnSchema>();
            foreach (var c in tableSchema.Columns.Values)
            {
                if (c.IsPrimaryKey)
                    pkCols.Add(c);
            }
            if (pkCols.Count == 1 && rowidAliasCandidates.Contains(pkCols[0].Name))
            {
                pkCols[0].IsIdentity = true;
            }

            schema.Tables[tableName] = tableSchema;

            // Extract indexes via PRAGMA (JNT8xxx analyzer input).
            //
            // Two distinct hazards, both surfaced by columns this code used
            // to read and then ignore:
            //
            // 1. index_list's 5th column ("partial", already visible in the
            //    comment below but never read) is 1 for a partial index (e.g.
            //    CREATE UNIQUE INDEX ... WHERE deleted_at IS NULL), which only
            //    enforces uniqueness among the rows matching its WHERE clause,
            //    not the whole table. The column list is still useful for
            //    JNT8xxx index-coverage hints, so the index stays captured --
            //    only IsUnique is downgraded to false.
            //
            // 2. index_info reports a NULL column name for an expression key
            //    part (e.g. CREATE UNIQUE INDEX ... ON t (customer_id,
            //    lower(email))) -- already detected here (the DBNull check),
            //    but the old code just skipped that one row and kept adding
            //    the index's other, real-column key parts, silently
            //    truncating a composite expression index down to only its
            //    real-column prefix while still reporting it unique -- e.g.
            //    cols=[customer_id] with IsUnique=true, even though
            //    customer_id alone is not unique at all. The model cannot
            //    carry the expression itself, so such an index is captured
            //    with IndexSchema.HasExpressionKeyPart set and Columns
            //    holding only the real-column subset (pre-2026-07-30 it was
            //    excluded entirely, which left a UNIQUE expression index
            //    invisible to the competing-constraint check). The flag is
            //    what makes the subset safe: consumers needing the full
            //    ordinal key shape skip flagged indexes.
            var indexNames = new List<(string Name, bool IsUnique, bool IsPartial)>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA index_list(\"{tableName.Replace("\"", "\"\"")}\")";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // (seq, name, unique, origin, partial)
                    indexNames.Add((reader.GetString(1), reader.GetInt32(2) == 1, reader.GetInt32(4) == 1));
                }
            }
            foreach (var (indexName, isUnique, isPartial) in indexNames)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA index_info(\"{indexName.Replace("\"", "\"\"")}\")";
                await using var reader = await cmd.ExecuteReaderAsync();
                var cols = new List<string>();
                bool hasExpressionColumn = false;
                while (await reader.ReadAsync())
                {
                    // (seqno, cid, name) — rows arrive in key order
                    if (await reader.IsDBNullAsync(2))
                        hasExpressionColumn = true;
                    else
                        cols.Add(reader.GetString(2));
                }
                if (!hasExpressionColumn && cols.Count == 0)
                    continue;
                if (hasExpressionColumn && cols.Count == 0)
                {
                    // ALL-expression index: no column rows, but the entry must
                    // exist so a unique one is visible as a competing
                    // constraint.
                    IndexCapture.EnsureIndex(schema, tableName, indexName, isUnique && !isPartial, hasExpressionKeyPart: true);
                    continue;
                }
                foreach (var col in cols)
                    IndexCapture.AddIndexColumn(schema, tableName, indexName, isUnique && !isPartial, col,
                        hasPrefixKeyPart: false, hasExpressionKeyPart: hasExpressionColumn);
            }

            // Extract foreign keys via PRAGMA
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA foreign_key_list(\"{tableName.Replace("\"", "\"\"")}\")";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string toTable = reader.GetString(2);      // table
                    string fromColumn = reader.GetString(3);   // from
                    string toColumn = reader.GetString(4);     // to

                    schema.ForeignKeys.Add(new ForeignKeySchema
                    {
                        FromTable = tableName,
                        FromColumn = fromColumn,
                        ToTable = toTable,
                        ToColumn = toColumn
                    });
                }
            }
        }

        return schema;
    }

    /// <summary>Parses "(a)" or "(a,b)" facets from a declared type.</summary>
    private static (int? First, int? Second) ParseDeclaredNumbers(string declaredType)
    {
        int open = declaredType.IndexOf('(');
        int close = declaredType.IndexOf(')');
        if (open < 0 || close <= open + 1)
            return (null, null);
        var parts = declaredType.Substring(open + 1, close - open - 1).Split(',');
        int? first = int.TryParse(parts[0].Trim(), out int f) ? f : null;
        int? second = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int sec) ? sec : null;
        return (first, second);
    }

    private static string NormalizeSqliteType(string sqliteType)
    {
        // SQLite has flexible typing — normalize to common types
        var upper = sqliteType.ToUpperInvariant().Trim();

        // BIGINT must stay distinct from the other INTEGER-affinity spellings
        // below: DialectMapper.MapDbTypeToCSharp maps "int" to System.Int32
        // but "bigint" to System.Int64. SQLite's storage affinity doesn't
        // itself limit an INTEGER-affinity column to 4 bytes -- SMALLINT/
        // TINYINT/plain INT collapsing to "int" is the safe direction (Int32
        // is a superset of their declared range), but BIGINT is the opposite
        // direction: a column explicitly declared to hold 64-bit values that
        // collapses to "int" generates Int32-typed reader access, throwing
        // InvalidCastException/overflowing the moment a live row actually
        // stores a value outside Int32's range.
        if (upper.Contains("BIGINT")) return "bigint";
        if (upper.Contains("INT")) return "int";
        if (upper.Contains("CHAR") || upper.Contains("CLOB") || upper.Contains("TEXT")) return "varchar";
        if (upper.Contains("BLOB") || string.IsNullOrEmpty(upper)) return "bytea";
        if (upper.Contains("REAL") || upper.Contains("FLOA") || upper.Contains("DOUB")) return "float";
        if (upper.Contains("BOOL")) return "bool";
        if (upper.Contains("DATE") || upper.Contains("TIME")) return "datetime";
        if (upper.Contains("NUMERIC") || upper.Contains("DECIMAL")) return "decimal";

        return "varchar";
    }
}
