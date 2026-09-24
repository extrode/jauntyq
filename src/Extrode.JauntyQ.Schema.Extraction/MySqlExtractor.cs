using Extrode.JauntyQ.Schema;
using MySqlConnector;

namespace Extrode.JauntyQ.Schema.Extraction;

public class MySqlExtractor : ISchemaExtractor
{
    public async Task<DatabaseSchema> ExtractAsync(string connectionString)
    {
        var schema = new DatabaseSchema();

        await using var conn = new MySqlConnection(connectionString);

        // Every INFORMATION_SCHEMA query below filters on TABLE_SCHEMA = the
        // connection's database. MySQL happily connects with no database
        // selected, and the filter then matches nothing — a silently EMPTY
        // snapshot that looks like success. Fail before opening instead.
        if (string.IsNullOrEmpty(conn.Database))
            throw new InvalidOperationException(
                "The MySQL connection string must include 'Database=<name>': schema extraction reads INFORMATION_SCHEMA for that database only, and without it the result would be an empty snapshot.");

        await conn.OpenAsync();

        string database = conn.Database;

        // Extract tables and columns
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
                    -- DATA_TYPE alone never carries MySQL's UNSIGNED modifier
                    -- (only COLUMN_TYPE does, e.g. 'int(10) unsigned') --
                    -- DialectMapper needs it to route int/bigint/smallint
                    -- unsigned columns to uint/ulong/ushort instead of
                    -- overflowing the signed CLR type at read time.
                    CASE WHEN c.COLUMN_TYPE LIKE '%unsigned%' THEN 1 ELSE 0 END AS is_unsigned,
                    CASE WHEN c.EXTRA LIKE '%auto_increment%' THEN 1 ELSE 0 END AS is_identity,
                    CASE WHEN c.COLUMN_KEY = 'PRI' THEN 1 ELSE 0 END AS is_pk,
                    c.CHARACTER_MAXIMUM_LENGTH AS max_length,
                    -- For BIT(n), MySQL reports the declared bit length via
                    -- NUMERIC_PRECISION (confirmed empirically: bit(3) ->
                    -- NUMERIC_PRECISION=3), not CHARACTER_MAXIMUM_LENGTH.
                    -- DialectMapper needs it to tell BIT(1) (-> bool) apart
                    -- from BIT(n>1) (-> neither MySqlConnector nor Npgsql
                    -- returns that as bool; falls through to 'object').
                    CASE WHEN c.DATA_TYPE IN ('decimal','numeric','bit') THEN c.NUMERIC_PRECISION END AS num_precision,
                    CASE WHEN c.DATA_TYPE IN ('decimal','numeric') THEN c.NUMERIC_SCALE END AS num_scale,
                    CASE WHEN c.CHARACTER_SET_NAME IS NULL THEN NULL
                         WHEN c.CHARACTER_SET_NAME LIKE 'utf%' THEN 1 ELSE 0 END AS is_unicode,
                    CASE WHEN c.EXTRA LIKE '%GENERATED%' THEN 1 ELSE 0 END AS is_computed,
                    -- TINYINT's NUMERIC_PRECISION is always 3 regardless of
                    -- declared display width (confirmed empirically: tinyint,
                    -- tinyint(1), and tinyint(4) all report 3) -- the width
                    -- distinguishing a boolean-shaped column only survives in
                    -- COLUMN_TYPE's text ('tinyint(1)' vs bare 'tinyint').
                    -- MySqlConnector's 'Treat Tiny As Boolean' default (on
                    -- unless the consumer's own connection string overrides
                    -- it) recognizes a column as boolean-shaped by exactly
                    -- this same signal, and confirmed live (Testcontainers
                    -- mysql:8.0) that under that default EVERY accessor --
                    -- not just GetValue()/GetBoolean(), but the typed
                    -- reader.GetInt16()/GetByte() JauntyQ's own generated
                    -- code calls for a short/byte-mapped column -- silently
                    -- collapses any non-zero stored value (2, -1, 127, ...)
                    -- to 1; only a stored 0 reads back faithfully. DialectMapper
                    -- needs this flag (folded into Precision, the same
                    -- length-signal channel BIT(n) already uses since
                    -- NormalizeDbType strips anything in parens off the dbType
                    -- string itself) to route such a column to bool instead
                    -- of short, matching what the wire/driver actually hand
                    -- back rather than falsely implying full -128..127 fidelity.
                    CASE WHEN c.DATA_TYPE = 'tinyint' AND c.COLUMN_TYPE = 'tinyint(1)' THEN 1 ELSE 0 END AS is_tinyint1,
                    -- COLUMN_TYPE raw, for enum member capture (spec 013).
                    -- MySQL has no named enum type: the member list exists
                    -- nowhere but this string, e.g. enum('pending','shipped').
                    c.COLUMN_TYPE AS column_type,
                    -- Spec 015: appended LAST on purpose; every reader ordinal
                    -- below is positional.
                    CASE WHEN t.TABLE_TYPE = 'VIEW' THEN 1 ELSE 0 END AS is_view
                FROM INFORMATION_SCHEMA.TABLES t
                JOIN INFORMATION_SCHEMA.COLUMNS c
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                WHERE t.TABLE_SCHEMA = @db AND t.TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

            var p = cmd.CreateParameter();
            p.ParameterName = "@db";
            p.Value = database;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string tableName = reader.GetString(0);
                string columnName = reader.GetString(1);
                string dataType = reader.GetString(2);
                bool isNullable = reader.GetString(3) == "YES";
                bool isUnsigned = reader.GetInt32(4) == 1;
                bool isIdentity = reader.GetInt32(5) == 1;
                bool isPrimaryKey = reader.GetInt32(6) == 1;
                // CHARACTER_MAXIMUM_LENGTH is bigint in MySQL; TEXT family
                // lengths overflow int — treat anything above int.MaxValue
                // as unbounded (-1).
                long? maxLengthRaw = await reader.IsDBNullAsync(7) ? null : reader.GetInt64(7);
                int? maxLength = maxLengthRaw == null ? null
                    : maxLengthRaw > int.MaxValue ? -1 : (int)maxLengthRaw;
                int? precision = await reader.IsDBNullAsync(8) ? null : (int)reader.GetInt64(8);
                int? scale = await reader.IsDBNullAsync(9) ? null : (int)reader.GetInt64(9);
                bool? isUnicode = await reader.IsDBNullAsync(10) ? null : reader.GetInt32(10) == 1;
                bool isComputed = reader.GetInt32(11) == 1;
                bool isTinyint1 = reader.GetInt32(12) == 1;
                // Stryker disable once String : INFORMATION_SCHEMA.COLUMNS.COLUMN_TYPE is NOT NULL, so the string.Empty arm never runs
                string columnType = await reader.IsDBNullAsync(13) ? string.Empty : reader.GetString(13);
                bool isView = reader.GetInt32(14) == 1;
                // A boolean-shaped TINYINT(1)/BOOLEAN column has no
                // NUMERIC_PRECISION signal of its own (see the query comment
                // above) -- fold the is_tinyint1 flag into Precision (the
                // exact channel BIT(n)'s length already rides on) as 1, the
                // same sentinel DialectMapper's existing "bit" when length <=1
                // arm uses for "this is boolean-shaped". A real decimal/numeric
                // /bit column's own `precision` is never simultaneously
                // non-null here (DATA_TYPE can't be both 'tinyint' and
                // 'decimal'/'numeric'/'bit'), so this can't clobber it.
                if (isTinyint1)
                    precision = 1;

                // DbType carries "unsigned" as a literal suffix (matching what
                // QueryValidator.Part2's numeric-literal range check already
                // expects to find via string.Contains("unsigned")) so both the
                // C# type mapping (DialectMapper) and that range check pick up
                // MySQL's widened positive-only range.
                string dbType = isUnsigned ? dataType + " unsigned" : dataType;

                if (!schema.Tables.ContainsKey(tableName))
                {
                    schema.Tables[tableName] = new TableSchema
                    {
                        Name = tableName,
                        IsView = isView,
                        Columns = new Dictionary<string, ColumnSchema>()
                    };
                }

                schema.Tables[tableName].Columns[columnName] = new ColumnSchema
                {
                    Name = columnName,
                    DbType = dbType,
                    IsNullable = isNullable,
                    IsPrimaryKey = isPrimaryKey,
                    IsIdentity = isIdentity,
                    MaxLength = maxLength,
                    Precision = precision,
                    Scale = scale,
                    IsUnicode = isUnicode,
                    IsComputed = isComputed,
                    EnumName = CaptureInlineEnum(schema, tableName, columnName, dataType, columnType)
                };
            }
        }

        // Extract indexes (key columns in key order; JNT8xxx analyzer input).
        //
        // A MySQL 8.0.13+ functional index key part (CREATE INDEX ... ON t
        // (col, (lower(other_col)))) has no real column: INFORMATION_SCHEMA
        // .STATISTICS.COLUMN_NAME is NULL for that position (the expression
        // itself lives in the separate EXPRESSION column instead). Confirmed
        // live (Testcontainers MySQL): reading COLUMN_NAME unconditionally via
        // reader.GetString() throws InvalidCastException the moment any table
        // in the schema has such an index -- a hard crash that takes down the
        // entire extraction, not just a wrong answer for that one index. Rows
        // are buffered per (table, index) so a functional key part can be
        // detected before committing any of that index's columns; the index is
        // then captured with IndexSchema.HasExpressionKeyPart set and Columns
        // holding only the real-column subset (pre-2026-07-30 it was excluded
        // entirely, which left a UNIQUE expression index invisible to
        // UpsertKeyResolver.HasCompetingUniqueConstraint). The flag is what
        // keeps the subset from misrepresenting the key shape: consumers that
        // need the full ordinal key (upsert key resolution, index-coverage
        // hints) skip flagged indexes, matching PostgresExtractor/
        // SqliteExtractor's identical represent-with-flag handling.
        await using (var cmd = conn.CreateCommand())
        {
            // SUB_PART is non-NULL for a column-prefix key part -- UNIQUE
            // (id(3), tenant) reports SUB_PART=3 for id's row. Such an index
            // fires on rows sharing only the prefix, independently of the full
            // column values, so it must not reach analysis looking like the
            // full-column index of the same name list: UpsertKeyResolver would
            // then either dismiss it as redundant (restoring AUD-R4-16) or pick
            // it as a full-column conflict key it does not enforce. Captured as
            // IndexSchema.HasPrefixKeyPart rather than excluded like functional
            // indexes below, because a prefix UNIQUE is still a real competing
            // constraint HasCompetingUniqueConstraint must see.
            cmd.CommandText = @"
                SELECT s.TABLE_NAME, s.INDEX_NAME, s.NON_UNIQUE, s.COLUMN_NAME, s.SUB_PART
                FROM INFORMATION_SCHEMA.STATISTICS s
                WHERE s.TABLE_SCHEMA = @db
                ORDER BY s.TABLE_NAME, s.INDEX_NAME, s.SEQ_IN_INDEX";

            var ip = cmd.CreateParameter();
            ip.ParameterName = "@db";
            ip.Value = database;
            cmd.Parameters.Add(ip);

            var rows = new List<(string Table, string Index, bool IsUnique, string? Column, bool HasSubPart)>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    rows.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetInt64(2) == 0,
                        await reader.IsDBNullAsync(3) ? null : reader.GetString(3),
                        !await reader.IsDBNullAsync(4)));
                }
            }

            // Manual replacement for rows.GroupBy(r => (r.Table, r.Index)) --
            // a Dictionary keyed on the ordinal (Table, Index) tuple, same as
            // GroupBy's default comparer, with group order tracked separately
            // to match GroupBy's documented first-occurrence ordering. A
            // contiguous-run scan over the SQL's ORDER BY would look simpler,
            // but INFORMATION_SCHEMA.STATISTICS name columns collate
            // case-insensitively in MySQL: two ordinally-distinct but
            // collation-equal names (e.g. "Foo" vs "foo") can tie in the
            // ORDER BY and interleave, fragmenting one logical group across
            // multiple non-adjacent runs -- which would let a functional
            // index's NULL-column row exclude only its own fragment while
            // the index's other (non-NULL) key parts still got emitted.
            var groupOrder = new List<(string Table, string Index)>();
            var groups = new Dictionary<(string Table, string Index), List<(string Table, string Index, bool IsUnique, string? Column, bool HasSubPart)>>();
            foreach (var row in rows)
            {
                var key = (row.Table, row.Index);
                if (!groups.TryGetValue(key, out var members))
                {
                    members = new List<(string, string, bool, string?, bool)>();
                    groups[key] = members;
                    groupOrder.Add(key);
                }
                members.Add(row);
            }

            foreach (var key in groupOrder)
            {
                var members = groups[key];
                bool hasExpressionKeyPart = false;
                bool hasPrefixKeyPart = false;
                foreach (var row in members)
                {
                    if (row.Column is null)
                        hasExpressionKeyPart = true;
                    else if (row.HasSubPart)
                        hasPrefixKeyPart = true;
                }
                // An expression key part no longer excludes the index (the
                // pre-2026-07-30 handling): it is captured with the flag set
                // and Columns holding the real-column subset, so a UNIQUE one
                // stays visible as a competing constraint. Consumers that
                // need the full ordinal key shape skip flagged indexes.
                bool anyRealColumn = false;
                foreach (var row in members)
                {
                    if (row.Column is null)
                        continue;
                    // Stryker disable once Boolean : with anyRealColumn left false, EnsureIndex below finds the index AddIndexColumn just created (or returns null for a missing table) and only re-applies the same expression flag
                    anyRealColumn = true;
                    IndexCapture.AddIndexColumn(schema, row.Table, row.Index, row.IsUnique, row.Column, hasPrefixKeyPart, hasExpressionKeyPart);
                }
                if (!anyRealColumn)
                    IndexCapture.EnsureIndex(schema, key.Table, key.Index, members[0].IsUnique, hasExpressionKeyPart);
            }
        }

        // Extract foreign keys
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    kcu.TABLE_NAME AS from_table,
                    kcu.COLUMN_NAME AS from_column,
                    kcu.REFERENCED_TABLE_NAME AS to_table,
                    kcu.REFERENCED_COLUMN_NAME AS to_column
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                WHERE kcu.TABLE_SCHEMA = @db
                    AND kcu.REFERENCED_TABLE_NAME IS NOT NULL";

            var p = cmd.CreateParameter();
            p.ParameterName = "@db";
            p.Value = database;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                schema.ForeignKeys.Add(new ForeignKeySchema
                {
                    FromTable = reader.GetString(0),
                    FromColumn = reader.GetString(1),
                    ToTable = reader.GetString(2),
                    ToColumn = reader.GetString(3)
                });
            }
        }

        // Extract sequences. AUD-R10-02 (§2.7 extractor parity sibling-sweep
        // of AUD-R10-01, sequenceSchema x 4 extractors): real/Oracle MySQL
        // genuinely has no sequence object, so this whole block was
        // correctly absent for that dialect -- but MariaDB (which
        // DialectMapper intentionally maps to this SAME "mysql" dialect
        // string, specifically BECAUSE it is wire/SQL-compatible for
        // everything the generator emits, and which is a first-class
        // supported variant with its own sample-matrix row -- see
        // samples/Extrode.JauntyQ.Sakila.MariaDb.Tests) has supported a true,
        // durable CREATE SEQUENCE object since 10.3, precisely one of the
        // "real behavioral divergences" DialectMapper's own doc comment
        // calls out. Confirmed live (Testcontainers mariadb:11): before this
        // fix, a MariaDB CREATE SEQUENCE was completely invisible to
        // MySqlExtractor -- schema.Sequences stayed empty, so CodeEmitter's
        // "no-op when schema.Sequences.Count == 0" guard (CodeEmitter.Part5)
        // silently generated no db.Sequences accessor at all for a
        // sequence that genuinely exists server-side, with no diagnostic --
        // silent schema-model data loss (the audit criteria §6's W2), not
        // merely an incomplete-but-flagged limitation.
        //
        // MariaDB implements a sequence as a special one-row table:
        // INFORMATION_SCHEMA.TABLES reports it with TABLE_TYPE = 'SEQUENCE'
        // (a value real MySQL's TABLES view never produces, so this query is
        // naturally a no-op -- zero rows, not an error -- against real
        // MySQL; no separate dialect branch is needed, matching how the
        // other extractors let a dialect-absent construct just not match).
        // The sequence's own attributes are read by selecting from the
        // sequence "table" itself (MariaDB's documented introspection
        // mechanism -- there is no sys.sequences-style catalog view), so its
        // name has to be interpolated into the FROM clause rather than
        // bound as a parameter; the name came from INFORMATION_SCHEMA
        // itself (trusted server metadata from this same schema pull, not
        // attacker-controlled input), and backtick-doubling escapes any
        // literal backtick in it, mirroring SqliteExtractor's identical
        // quote-and-double-embedded-quote handling for its PRAGMA
        // table/index names.
        var sequenceNames = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @db AND TABLE_TYPE = 'SEQUENCE'
                ORDER BY TABLE_NAME";

            var p = cmd.CreateParameter();
            p.ParameterName = "@db";
            p.Value = database;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                sequenceNames.Add(reader.GetString(0));
        }

        foreach (string seqName in sequenceNames)
        {
            await using var cmd = conn.CreateCommand();
            string quotedDb = "`" + database.Replace("`", "``") + "`";
            string quotedSeq = "`" + seqName.Replace("`", "``") + "`";
            cmd.CommandText = $@"
                SELECT start_value, increment, minimum_value, maximum_value, next_not_cached_value
                FROM {quotedDb}.{quotedSeq}";

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                schema.Sequences[seqName] = new SequenceSchema
                {
                    Name = seqName,
                    StartValue = reader.GetInt64(0),
                    Increment = reader.GetInt64(1),
                    MinValue = await reader.IsDBNullAsync(2) ? null : reader.GetInt64(2),
                    MaxValue = await reader.IsDBNullAsync(3) ? null : reader.GetInt64(3),
                    // next_not_cached_value is the closest MariaDB analog to
                    // SQL Server's sys.sequences.current_value ("the last
                    // value allocated, or the initial value if never used")
                    // -- both represent the next value NEXTVAL will hand out
                    // absent a cache jump. MariaDB exposes no exact
                    // "last consumed value" without a session-scoped
                    // LASTVAL() call, which has no meaning in a stateless
                    // schema pull.
                    CurrentValue = await reader.IsDBNullAsync(4) ? null : reader.GetInt64(4)
                };
            }
        }

        // Extract stored procedures: parameters only. See PostgresExtractor's
        // parallel block for the full rationale -- this whole block was
        // previously entirely missing here too, even though -- @call is
        // documented and implemented as dialect-agnostic (no JNT7002-style
        // gate exists for it, unlike -- @proc's SQL-Server-only T-SQL
        // emission). A real CREATE PROCEDURE already living in a MySQL
        // database (a bog-standard MySQL feature, not an edge case) could
        // therefore never be bound to by -- @call: schema.Procedures was
        // always empty, so every such file misfired JNT2005 "procedure not
        // found" on perfectly valid input -- a false-positive Error
        // (the audit criteria §2.6's FP bar).
        //
        // INFORMATION_SCHEMA.ROUTINES/PARAMETERS mirror the same ANSI-
        // standard views SqlServerExtractor reads. Unlike Postgres, MySQL has
        // no procedure-overloading concept, so SPECIFIC_NAME always equals
        // ROUTINE_NAME here (same assumption SqlServerExtractor already
        // makes). CHARACTER_MAXIMUM_LENGTH is bigint in MySQL's
        // INFORMATION_SCHEMA (same overflow risk the columns query above
        // already guards against, even though a parameter's declared length
        // realistically never approaches int.MaxValue). MySQL procedures can
        // technically emit a result set via a bare SELECT, but there is no
        // static, pre-execution way to discover its shape (no equivalent of
        // SQL Server's dm_exec_describe_first_result_set) -- Results is
        // intentionally left empty, the same degrade-to-int-return path
        // already used for a procedure that genuinely has no result set.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.SPECIFIC_NAME AS proc_name,
                       p.PARAMETER_NAME AS param_name,
                       p.DATA_TYPE AS data_type,
                       p.PARAMETER_MODE AS mode,
                       p.CHARACTER_MAXIMUM_LENGTH AS max_length,
                       p.NUMERIC_PRECISION AS num_precision,
                       p.NUMERIC_SCALE AS num_scale
                FROM INFORMATION_SCHEMA.ROUTINES r
                LEFT JOIN INFORMATION_SCHEMA.PARAMETERS p
                    ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.ROUTINE_SCHEMA = p.SPECIFIC_SCHEMA
                WHERE r.ROUTINE_TYPE = 'PROCEDURE' AND r.ROUTINE_SCHEMA = @db
                ORDER BY r.SPECIFIC_NAME, p.ORDINAL_POSITION";

            var procParam = cmd.CreateParameter();
            procParam.ParameterName = "@db";
            procParam.Value = database;
            cmd.Parameters.Add(procParam);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string procName = reader.GetString(0);
                if (!schema.Procedures.TryGetValue(procName, out var proc))
                {
                    proc = new ProcedureSchema { Name = procName };
                    schema.Procedures[procName] = proc;
                }

                // A proc with no parameters yields one row with a null param name.
                if (await reader.IsDBNullAsync(1))
                    continue;

                string paramName = reader.GetString(1).TrimStart('@');
                string paramType = reader.GetString(2);
                // Stryker disable once String : PARAMETER_MODE is never NULL for a procedure parameter, and "" would fall into the same _ => In arm as "IN"
                string mode = await reader.IsDBNullAsync(3) ? "IN" : reader.GetString(3);
                long? paramMaxRaw = await reader.IsDBNullAsync(4) ? null : reader.GetInt64(4);
                int? paramMax = paramMaxRaw == null ? null
                    : paramMaxRaw > int.MaxValue ? -1 : (int)paramMaxRaw;
                int? paramPrecision = await reader.IsDBNullAsync(5) ? null : (int)reader.GetInt64(5);
                int? paramScale = await reader.IsDBNullAsync(6) ? null : (int)reader.GetInt64(6);

                proc.Params.Add(new ProcedureParam
                {
                    Name = paramName,
                    DbType = paramType,
                    Direction = mode switch
                    {
                        "OUT" => ProcedureParamDirection.Out,
                        "INOUT" => ProcedureParamDirection.InOut,
                        _ => ProcedureParamDirection.In
                    },
                    MaxLength = paramMax,
                    Precision = paramPrecision,
                    Scale = paramScale
                });
            }
        }

        // Extract stored functions (spec 014, FR-001). MySQL has functions but
        // no user-defined types at all -- no DOMAIN, no composite, no table
        // type -- so UserTypes stays empty here and FR-004 is a no-op for this
        // dialect. That is a real difference between the engines, not a gap.
        //
        // The return type lives on ROUTINES itself (DTD_IDENTIFIER carries the
        // full declaration, DATA_TYPE the bare type), and IS_RESULT does not
        // exist in MySQL's PARAMETERS view: a function's return is the row with
        // ORDINAL_POSITION 0 and a NULL name, which is why that row is skipped
        // by the same null-name guard the proc block uses rather than by a
        // filter of its own.
        //
        // MySQL permits no overloads, so the signature key always resolves to
        // one entry per function; it is used anyway so the three dialects
        // cannot drift into different key shapes.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.SPECIFIC_NAME AS fn_name,
                       r.DATA_TYPE AS return_type,
                       r.CHARACTER_MAXIMUM_LENGTH AS return_max_length,
                       r.NUMERIC_PRECISION AS return_precision,
                       r.NUMERIC_SCALE AS return_scale,
                       p.PARAMETER_NAME AS param_name,
                       p.DATA_TYPE AS param_type,
                       p.CHARACTER_MAXIMUM_LENGTH AS param_max_length,
                       p.NUMERIC_PRECISION AS param_precision,
                       p.NUMERIC_SCALE AS param_scale
                FROM INFORMATION_SCHEMA.ROUTINES r
                LEFT JOIN INFORMATION_SCHEMA.PARAMETERS p
                    ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.ROUTINE_SCHEMA = p.SPECIFIC_SCHEMA
                WHERE r.ROUTINE_TYPE = 'FUNCTION' AND r.ROUTINE_SCHEMA = @db
                ORDER BY r.SPECIFIC_NAME, p.ORDINAL_POSITION";

            var fnParam = cmd.CreateParameter();
            fnParam.ParameterName = "@db";
            fnParam.Value = database;
            cmd.Parameters.Add(fnParam);

            var pending = new Dictionary<string, FunctionSchema>(StringComparer.Ordinal);
            var pendingArgTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string fnName = reader.GetString(0);
                if (!pending.TryGetValue(fnName, out var fn))
                {
                    long? retMaxRaw = await reader.IsDBNullAsync(2) ? null : reader.GetInt64(2);
                    // Stryker disable once String : ROUTINES.DATA_TYPE is never NULL for a function, so the string.Empty return type arm never runs
                    fn = new FunctionSchema
                    {
                        Name = fnName,
                        Schema = database,
                        Return = new FunctionReturn
                        {
                            DbType = await reader.IsDBNullAsync(1) ? string.Empty : reader.GetString(1),
                            MaxLength = retMaxRaw == null ? null
                                : retMaxRaw > int.MaxValue ? -1 : (int)retMaxRaw,
                            Precision = await reader.IsDBNullAsync(3) ? null : (int)reader.GetInt64(3),
                            Scale = await reader.IsDBNullAsync(4) ? null : (int)reader.GetInt64(4)
                        }
                    };
                    pending[fnName] = fn;
                    pendingArgTypes[fnName] = new List<string>();
                }

                // The function's RETURN row carries a null PARAMETER_NAME, and
                // so does the single row a zero-argument function yields. Both
                // mean "not an argument", so one guard covers them.
                if (await reader.IsDBNullAsync(5))
                    continue;

                long? paramMaxRaw = await reader.IsDBNullAsync(7) ? null : reader.GetInt64(7);
                // Stryker disable once String : PARAMETERS.DATA_TYPE is never NULL for a named parameter row, so the string.Empty arm never runs
                string paramType = await reader.IsDBNullAsync(6) ? string.Empty : reader.GetString(6);

                fn.Params.Add(new FunctionParam
                {
                    Name = reader.GetString(5).TrimStart('@'),
                    DbType = paramType,
                    MaxLength = paramMaxRaw == null ? null
                        : paramMaxRaw > int.MaxValue ? -1 : (int)paramMaxRaw,
                    Precision = await reader.IsDBNullAsync(8) ? null : (int)reader.GetInt64(8),
                    Scale = await reader.IsDBNullAsync(9) ? null : (int)reader.GetInt64(9)
                });
                pendingArgTypes[fnName].Add(paramType);
            }

            foreach (var kv in pending)
                schema.Functions[UserTypeResolution.FunctionKey(kv.Key, pendingArgTypes[kv.Key])] = kv.Value;
        }

        // Stryker disable once Statement : MySQL and MariaDB have no user-defined types, so UserTypes is always empty and Apply returns without changing anything
        UserTypeResolution.Apply(schema);

        return schema;
    }

    /// <summary>
    /// Captures a MySQL inline column enum (spec 013, FR-002) and returns the
    /// key to store on the column, or null when the column is not an enum.
    ///
    /// MySQL has no named enum type -- the members are declared on the column
    /// and exist nowhere but COLUMN_TYPE -- so the entity is named
    /// {Table}{Column}. Two columns that happen to declare identical member
    /// lists therefore stay two entities: merging them would make the emitted
    /// type name depend on which column the extractor reached first.
    ///
    /// DATA_TYPE 'set' is deliberately not captured (FR-011). SET is
    /// multi-valued ('a,b' in one column) and keeps its existing string
    /// mapping; a [Flags] design is its own decision.
    /// </summary>
    internal static string? CaptureInlineEnum(
        DatabaseSchema schema, string tableName, string columnName, string dataType, string columnType)
    {
        if (!string.Equals(dataType, "enum", StringComparison.OrdinalIgnoreCase))
            return null;

        var members = ParseEnumMembers(columnType);
        if (members.Count == 0)
            return null;

        string name = EnumMemberNaming.Fold(tableName) + EnumMemberNaming.Fold(columnName);

        var enumSchema = new EnumSchema { Name = name };
        foreach (string value in members)
            enumSchema.Members.Add(new EnumMember { Value = value, CSharpName = EnumMemberNaming.Fold(value) });

        schema.Enums[name] = enumSchema;
        return name;
    }

    /// <summary>
    /// Pulls the member values out of a MySQL COLUMN_TYPE string such as
    /// <c>enum('pending','shipped')</c>, in declaration order and verbatim.
    ///
    /// Splitting on ',' would be wrong twice over, and both cases are real --
    /// confirmed live against MySQL 8.4, which reports
    /// <c>enum('a,b','it''s','','in-progress')</c> for a column declared with
    /// those four members:
    ///   * a member may CONTAIN a comma, so commas inside quotes are data;
    ///   * a member may contain a quote, escaped by doubling it ('') -- and
    ///     the empty member '' is legal, which looks identical to the start of
    ///     an escape until the next character is examined.
    /// So this walks the string instead, tracking quote state.
    /// </summary>
    internal static List<string> ParseEnumMembers(string columnType)
    {
        var members = new List<string>();
        if (string.IsNullOrEmpty(columnType))
            return members;

        int open = columnType.IndexOf('(');
        int close = columnType.LastIndexOf(')');
        if (open < 0 || close <= open)
            return members;

        string body = columnType.Substring(open + 1, close - open - 1);

        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool sawQuotedRun = false;

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];

            if (inQuotes)
            {
                if (c == '\'')
                {
                    // A doubled quote inside a quoted run is a literal quote;
                    // a single one ends the run.
                    if (i + 1 < body.Length && body[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            if (c == '\'')
            {
                inQuotes = true;
                sawQuotedRun = true;
            }
            else if (c == ',')
            {
                if (sawQuotedRun)
                    members.Add(sb.ToString());
                sb.Clear();
                sawQuotedRun = false;
            }
            // Anything else outside quotes is separator noise (whitespace).
        }

        if (sawQuotedRun)
            members.Add(sb.ToString());

        return members;
    }
}
