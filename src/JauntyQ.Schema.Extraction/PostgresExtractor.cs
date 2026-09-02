using JauntyQ.Schema;
using Npgsql;

namespace JauntyQ.Schema.Extraction;

public class PostgresExtractor : ISchemaExtractor
{
    /// <summary>
    /// Default schema scope. Every query below filters to exactly one schema
    /// (previously hardcoded to "public" with no way to target another one
    /// and no diagnostic that non-public tables were being dropped); making it
    /// a constructor parameter keeps the single-schema-per-pull contract
    /// (avoids the cross-schema table-name collision that <see cref="SqlServerExtractor"/>
    /// used to have) while letting a consumer opt into a non-default schema
    /// instead of silently losing it.
    /// </summary>
    public const string DefaultSchema = "public";

    private readonly string _schema;

    public PostgresExtractor(string schema = DefaultSchema)
    {
        _schema = string.IsNullOrWhiteSpace(schema) ? DefaultSchema : schema;
    }

    public async Task<DatabaseSchema> ExtractAsync(string connectionString)
    {
        var schema = new DatabaseSchema();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Extract enum types FIRST, so the column loop below can tag a column
        // with the type it belongs to (spec 013, FR-001/FR-004). pg_enum is
        // the only place the member list exists -- information_schema reports
        // an enum column as data_type 'USER-DEFINED' and carries nothing about
        // what the permitted values are, which is why the list was previously
        // discarded at pull time and unrecoverable downstream.
        //
        // enumsortorder, not enumlabel: the declaration order is what defines
        // '<' and ORDER BY for the type, so it is semantic, not presentation.
        // Scoped to the same single schema as everything else here.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.typname AS enum_name, e.enumlabel AS member
                FROM pg_type t
                JOIN pg_namespace n ON n.oid = t.typnamespace
                JOIN pg_enum e ON e.enumtypid = t.oid
                WHERE t.typtype = 'e' AND n.nspname = @schema
                ORDER BY t.typname, e.enumsortorder";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string enumName = reader.GetString(0);
                string member = reader.GetString(1);

                if (!schema.Enums.TryGetValue(enumName, out var enumSchema))
                {
                    enumSchema = new EnumSchema { Name = enumName };
                    schema.Enums[enumName] = enumSchema;
                }

                enumSchema.Members.Add(new EnumMember
                {
                    Value = member,
                    CSharpName = EnumMemberNaming.Fold(member)
                });
            }
        }

        // Extract tables and columns
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.table_name, c.column_name, c.data_type, c.udt_name, c.is_nullable,
                    CASE WHEN c.is_identity = 'YES' OR c.column_default LIKE 'nextval(%' THEN 1 ELSE 0 END AS is_identity,
                    CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
                    c.character_maximum_length AS max_length,
                    CASE WHEN c.data_type = 'numeric' THEN c.numeric_precision END AS num_precision,
                    CASE WHEN c.data_type = 'numeric' THEN c.numeric_scale END AS num_scale,
                    -- citext (case-insensitive text extension type) reports
                    -- data_type = USER-DEFINED, not one of the three strings
                    -- above -- without udt_name here, a citext column's
                    -- IsUnicode stayed null (unknown) even though Postgres
                    -- text of any kind is Unicode under the server's UTF-8
                    -- encoding.
                    CASE WHEN c.data_type IN ('character varying','character','text') OR c.udt_name = 'citext' THEN 1 END AS is_unicode,
                    CASE WHEN c.is_generated = 'ALWAYS' THEN 1 ELSE 0 END AS is_computed,
                    -- Spec 015: appended LAST on purpose. Every reader ordinal
                    -- below is positional, so a new column anywhere earlier in
                    -- this list silently shifts eleven of them.
                    CASE WHEN t.table_type = 'VIEW' THEN 1 ELSE 0 END AS is_view,
                    -- Spec 014, and appended LAST for the reason above.
                    -- PostgreSQL already reports a DOMAIN column's data_type as
                    -- the BASE type, so the mapping was never broken for one --
                    -- what was missing is that the column was declared through
                    -- a domain at all. Without this the snapshot claims the
                    -- column was declared varchar(11) when it was declared
                    -- 'ssn', and the contract comparer cannot report a change
                    -- to the domain behind it as drift.
                    c.domain_name AS domain_name
                FROM information_schema.tables t
                JOIN information_schema.columns c
                    ON t.table_name = c.table_name AND t.table_schema = c.table_schema
                LEFT JOIN (
                    SELECT kcu.table_schema, kcu.table_name, kcu.column_name
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                ) pk ON pk.table_schema = c.table_schema AND pk.table_name = c.table_name AND pk.column_name = c.column_name
                WHERE t.table_schema = @schema AND t.table_type IN ('BASE TABLE', 'VIEW')
                ORDER BY t.table_name, c.ordinal_position";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string tableName = reader.GetString(0);
                string columnName = reader.GetString(1);
                string dataType = reader.GetString(2);
                string udtName = reader.GetString(3);
                bool isNullable = reader.GetString(4) == "YES";
                bool isIdentity = reader.GetInt32(5) == 1;
                bool isPrimaryKey = reader.GetInt32(6) == 1;
                int? maxLength = await reader.IsDBNullAsync(7) ? null : reader.GetInt32(7);
                int? precision = await reader.IsDBNullAsync(8) ? null : reader.GetInt32(8);
                int? scale = await reader.IsDBNullAsync(9) ? null : reader.GetInt32(9);
                // PostgreSQL text is always Unicode (server encoding UTF8)
                bool? isUnicode = await reader.IsDBNullAsync(10) ? null : reader.GetInt32(10) == 1;
                bool isComputed = reader.GetInt32(11) == 1;
                bool isView = reader.GetInt32(12) == 1;
                string? domainName = await reader.IsDBNullAsync(13) ? null : reader.GetString(13);

                // information_schema reports data_type 'USER-DEFINED' for extension,
                // domain, and enum types (e.g. citext); the concrete type name lives
                // in udt_name. Record that instead so the mapper sees the real type.
                string dbType = ResolveDbType(dataType, udtName);

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
                    // ResolveDbType has already turned 'USER-DEFINED' into the
                    // concrete udt_name, so an enum column's dbType is the enum
                    // type's own name and this lookup is exact. A USER-DEFINED
                    // type that is not an enum (citext, a domain, a composite)
                    // simply isn't in the dictionary and stays untagged.
                    EnumName = schema.Enums.ContainsKey(dbType) ? dbType : null,
                    // Null for every ordinary column. Set here rather than left
                    // to UserTypeResolution.Apply because Postgres resolved the
                    // TYPE for us and only the provenance is missing; Apply
                    // skips a column that already carries this, so the two
                    // paths cannot both fire on one column.
                    ResolvedFromUserType = domainName
                };
            }
        }

        // Spec 015: materialized views, which the loop above CANNOT reach.
        // PostgreSQL omits them from information_schema entirely -- not just
        // from .tables (so no table_type widening finds them) but from
        // .columns too, so even knowing the name would not yield the shape.
        // The SQL standard has no materialized view, and PostgreSQL declines to
        // invent an information_schema row for one. The catalog is the only
        // source, hence a second pass rather than a wider predicate.
        //
        // Postgres-only by nature: SQL Server has no materialized view (an
        // indexed view is a different feature with different semantics and
        // stays out), and MySQL and SQLite have none at all.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT c.relname AS table_name,
                    a.attname AS column_name,
                    -- An array's element type is spelled differently by the two
                    -- sources: information_schema reports data_type 'ARRAY' with
                    -- udt_name '_int4', which ResolveDbType turns into 'int4[]',
                    -- while format_type spells the same type 'integer[]'. Both
                    -- map to int[] in C#, so nothing was ever mistyped -- but a
                    -- snapshot then holds two strings for one type and every
                    -- DbType comparison (migration impact, snapshot diffing)
                    -- reads it as a change. Reporting 'ARRAY' here routes both
                    -- passes through the same ResolveDbType branch, so they
                    -- agree by construction rather than by coincidence.
                    CASE WHEN t.typcategory = 'A' THEN 'ARRAY'
                         ELSE format_type(a.atttypid, NULL) END AS data_type,
                    t.typname AS udt_name,
                    CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END AS is_nullable,
                    -- format_type above is passed a NULL typmod, and the typmod
                    -- is what carries length, precision and scale -- so before
                    -- these three columns existed every matview column reported
                    -- no facets at all. The consumer-visible one is MaxLength:
                    -- JNT5001 refuses a string literal that cannot fit the
                    -- column, and a null silently skips that check.
                    --
                    -- These are information_schema's OWN helper functions, which
                    -- is the point: the base-table pass reads
                    -- information_schema.columns, so using anything else here
                    -- would be a second opinion that could drift from it.
                    information_schema._pg_char_max_length(a.atttypid, a.atttypmod) AS max_length,
                    -- Gated to numeric exactly as the base-table pass gates on
                    -- data_type = 'numeric'. Ungated, _pg_numeric_precision
                    -- answers 32 for a plain integer, and the matview would
                    -- claim a precision no table column ever reports -- parity
                    -- broken in the opposite direction.
                    CASE WHEN format_type(a.atttypid, NULL) = 'numeric'
                         THEN information_schema._pg_numeric_precision(a.atttypid, a.atttypmod) END AS num_precision,
                    CASE WHEN format_type(a.atttypid, NULL) = 'numeric'
                         THEN information_schema._pg_numeric_scale(a.atttypid, a.atttypmod) END AS num_scale,
                    -- Same predicate as the base-table pass, on the catalog's
                    -- spelling of the same three types.
                    CASE WHEN format_type(a.atttypid, NULL) IN ('character varying','character','text')
                              OR t.typname = 'citext' THEN 1 END AS is_unicode
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid
                JOIN pg_type t ON t.oid = a.atttypid
                WHERE c.relkind = 'm'
                  AND n.nspname = @schema
                  -- attnum > 0 skips the system columns; NOT attisdropped
                  -- skips columns removed by a later ALTER, whose pg_attribute
                  -- rows survive as tombstones with mangled names.
                  AND a.attnum > 0
                  AND NOT a.attisdropped
                ORDER BY c.relname, a.attnum";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string tableName = reader.GetString(0);
                string columnName = reader.GetString(1);
                string dataType = reader.GetString(2);
                string udtName = reader.GetString(3);
                bool isNullable = reader.GetString(4) == "YES";
                int? maxLength = await reader.IsDBNullAsync(5) ? null : reader.GetInt32(5);
                int? precision = await reader.IsDBNullAsync(6) ? null : reader.GetInt32(6);
                int? scale = await reader.IsDBNullAsync(7) ? null : reader.GetInt32(7);
                bool? isUnicode = await reader.IsDBNullAsync(8) ? null : reader.GetInt32(8) == 1;

                string dbType = ResolveDbType(dataType, udtName);

                if (!schema.Tables.ContainsKey(tableName))
                {
                    schema.Tables[tableName] = new TableSchema
                    {
                        Name = tableName,
                        IsView = true,
                        Columns = new Dictionary<string, ColumnSchema>()
                    };
                }

                // No primary key, identity or generated facets: a materialized
                // view has none of them. It CAN carry indexes, and the index
                // pass below reads pg_class without filtering on relkind, so
                // those are captured on the same terms as a table's.
                schema.Tables[tableName].Columns[columnName] = new ColumnSchema
                {
                    Name = columnName,
                    DbType = dbType,
                    IsNullable = isNullable,
                    MaxLength = maxLength,
                    Precision = precision,
                    Scale = scale,
                    IsUnicode = isUnicode,
                    EnumName = schema.Enums.ContainsKey(dbType) ? dbType : null
                };
            }
        }

        // Extract indexes (key columns in key order; JNT8xxx analyzer input).
        //
        // Two distinct hazards, both confirmed live (Postgres via
        // Testcontainers):
        //
        // 1. A partial index (indpred IS NOT NULL, e.g. CREATE UNIQUE INDEX
        //    ... WHERE deleted_at IS NULL) only enforces uniqueness among the
        //    rows matching its predicate, not the whole table. The column
        //    list is still useful for JNT8xxx index-coverage hints, so the
        //    index stays captured -- only IsUnique is downgraded to false so
        //    UpsertKeyResolver/NPlusOneAnalyzer don't treat it as a safe
        //    globally-unique key.
        //
        // 2. An expression index (indexprs IS NOT NULL, e.g. CREATE UNIQUE
        //    INDEX ... ON t (customer_id, lower(email))) has a key part with
        //    no real column: indkey reports attnum 0 for that position, which
        //    silently fails to match any row in pg_attribute (attnum 0 isn't
        //    a real user column). The old query's plain JOIN just dropped
        //    that key part instead of the whole index -- confirmed live: a
        //    composite (customer_id, lower(email)) unique index came back as
        //    IsUnique=true with cols=[customer_id] alone, which is not only
        //    wrong but actively dangerous (customer_id is not unique by
        //    itself; a naive consumer could pick it as an upsert key). The
        //    model cannot carry the expression itself, so such an index is
        //    captured with IndexSchema.HasExpressionKeyPart set and Columns
        //    holding only the real-column subset (pre-2026-07-30 it was
        //    excluded entirely, which left a UNIQUE expression index
        //    invisible to the competing-constraint check). The flag is what
        //    makes the subset safe: consumers needing the full ordinal key
        //    shape skip flagged indexes.
        await using (var cmd = conn.CreateCommand())
        {
            // LEFT JOIN, deliberately: an expression key part reports attnum 0
            // in indkey, which matches no pg_attribute row -- a plain JOIN
            // silently dropped that position (the truncation bug the old
            // "exclude expression indexes entirely" filter was written to
            // stop). The NULL column_name row now survives to mark the index
            // HasExpressionKeyPart instead, and the a.attnum <> 0 guard keeps
            // the join from ever matching a system column by accident.
            cmd.CommandText = @"
                SELECT t.relname AS table_name, i.relname AS index_name,
                       ix.indisunique AND ix.indpred IS NULL AS is_effectively_unique,
                       a.attname AS column_name,
                       ix.indexprs IS NOT NULL AS has_expression
                FROM pg_index ix
                JOIN pg_class i ON i.oid = ix.indexrelid
                JOIN pg_class t ON t.oid = ix.indrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum AND a.attnum <> 0
                WHERE n.nspname = @schema
                ORDER BY t.relname, i.relname, k.ord";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string tableName = reader.GetString(0);
                string indexName = reader.GetString(1);
                bool isUnique = reader.GetBoolean(2);
                bool hasExpression = reader.GetBoolean(4);
                if (await reader.IsDBNullAsync(3))
                {
                    // The expression key part itself: no column to add, but
                    // the index entry must exist (an ALL-expression unique
                    // index has no other rows) and carry the flag.
                    IndexCapture.EnsureIndex(schema, tableName, indexName, isUnique, hasExpression);
                    continue;
                }
                IndexCapture.AddIndexColumn(schema, tableName, indexName, isUnique,
                    columnName: reader.GetString(3),
                    hasPrefixKeyPart: false,
                    hasExpressionKeyPart: hasExpression);
            }
        }

        // Extract foreign keys. constraint_column_usage carries no ordinal
        // correlation to key_column_usage, so joining the two on
        // constraint_name alone cross-products every FK column against every
        // referenced column of a composite key (confirmed empirically against
        // a live Postgres 16 container: a 2-column FK produced 4 rows instead
        // of 2, pairing e.g. child.x against both parent.a and parent.b).
        // referential_constraints.unique_constraint_name links to the
        // referenced key's own key_column_usage rows, and
        // kcu.position_in_unique_constraint = ccu.ordinal_position pairs each
        // FK column with the exact referenced column at the same position in
        // the key (verified correct for composite keys, single-column keys,
        // and FKs referencing a UNIQUE constraint rather than a PK).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    kcu.table_name AS from_table,
                    kcu.column_name AS from_column,
                    ccu.table_name AS to_table,
                    ccu.column_name AS to_column
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON tc.constraint_name = kcu.constraint_name
                    AND tc.table_schema = kcu.table_schema
                JOIN information_schema.referential_constraints rc
                    ON tc.constraint_name = rc.constraint_name
                    AND tc.constraint_schema = rc.constraint_schema
                JOIN information_schema.key_column_usage ccu
                    ON rc.unique_constraint_name = ccu.constraint_name
                    AND rc.unique_constraint_schema = ccu.constraint_schema
                    AND kcu.position_in_unique_constraint = ccu.ordinal_position
                WHERE tc.constraint_type = 'FOREIGN KEY'
                    AND tc.table_schema = @schema
                ORDER BY kcu.table_name, kcu.constraint_name, kcu.ordinal_position";
            AddSchemaParam(cmd);

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

        // Extract sequences. information_schema.sequences reports the numeric
        // attributes as SQL-standard character_data (text), so CAST them to
        // bigint. pg_sequences.last_value carries the current value (NULL when
        // the sequence has never been advanced), joined by name in the public
        // schema to avoid an N+1 per-sequence probe.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT s.sequence_name,
                       CAST(s.start_value AS bigint) AS start_value,
                       CAST(s.increment AS bigint) AS increment,
                       CAST(s.minimum_value AS bigint) AS minimum_value,
                       CAST(s.maximum_value AS bigint) AS maximum_value,
                       ps.last_value
                FROM information_schema.sequences s
                LEFT JOIN pg_sequences ps
                    ON ps.schemaname = s.sequence_schema AND ps.sequencename = s.sequence_name
                WHERE s.sequence_schema = @schema
                ORDER BY s.sequence_name";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string seqName = reader.GetString(0);
                schema.Sequences[seqName] = new SequenceSchema
                {
                    Name = seqName,
                    StartValue = reader.GetInt64(1),
                    Increment = reader.GetInt64(2),
                    MinValue = await reader.IsDBNullAsync(3) ? null : reader.GetInt64(3),
                    MaxValue = await reader.IsDBNullAsync(4) ? null : reader.GetInt64(4),
                    CurrentValue = await reader.IsDBNullAsync(5) ? null : reader.GetInt64(5)
                };
            }
        }

        // Extract stored procedures: parameters only (see below for why no
        // result-set shape is attempted). Procs are captured so -- @call can
        // bind to them -- this whole block was previously entirely missing,
        // even though -- @call is documented and implemented as dialect-
        // agnostic (docs/06-reference/directives.md lists it as "any"
        // dialect, and JauntyQGenerator.Part2.cs's @call handling has no
        // JNT7002-style dialect gate at all, unlike -- @proc's SQL-Server-
        // only T-SQL emission). A real CREATE PROCEDURE already living in a
        // Postgres database (native since PG11) could therefore never be
        // bound to by -- @call: TryResolveProcedure always found
        // schema.Procedures empty and every such file misfired JNT2005
        // "procedure not found" on perfectly valid input -- a false-positive
        // Error (the audit criteria §2.6's FP bar).
        //
        // information_schema.routines/parameters are the same ANSI-standard
        // views SqlServerExtractor reads. routine_name (not specific_name) is
        // the name -- @call actually matches against; specific_name is only
        // the per-overload join key into information_schema.parameters.
        // Known limitation, not fixed here: unlike SQL Server/MySQL, Postgres
        // allows overloaded procedure names with different parameter lists --
        // two overloads sharing one routine_name would have their parameter
        // rows merged into a single ProcedureSchema, the same class of
        // simplification ForeignKeySchema/IndexSchema already make elsewhere
        // in this model (no per-overload identity concept). Postgres
        // procedures exist mainly for transaction control / side effects and
        // have no ad-hoc, pre-execution way to discover a result-set shape
        // (no equivalent of SQL Server's dm_exec_describe_first_result_set),
        // so Results is intentionally left empty here -- -- @call then emits
        // an int-returning call for it, the same degrade path already used
        // for a procedure that genuinely has no result set.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.routine_name AS proc_name,
                       p.parameter_name AS param_name,
                       p.data_type AS data_type,
                       p.parameter_mode AS mode,
                       p.character_maximum_length AS max_length,
                       p.numeric_precision AS num_precision,
                       p.numeric_scale AS num_scale
                FROM information_schema.routines r
                LEFT JOIN information_schema.parameters p
                    ON r.specific_name = p.specific_name AND r.specific_schema = p.specific_schema
                WHERE r.routine_type = 'PROCEDURE' AND r.specific_schema = @schema
                ORDER BY r.routine_name, p.ordinal_position";
            AddSchemaParam(cmd);

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
                string mode = await reader.IsDBNullAsync(3) ? "IN" : reader.GetString(3);
                int? paramMax = await reader.IsDBNullAsync(4) ? null : reader.GetInt32(4);
                int? paramPrecision = await reader.IsDBNullAsync(5) ? null : reader.GetInt32(5);
                int? paramScale = await reader.IsDBNullAsync(6) ? null : reader.GetInt32(6);

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

        // Extract DOMAINs and composite types (spec 014, FR-004). Both reach
        // DialectMapper's _ => fallback today and generate object + JNT2007;
        // capturing them splits that one diagnostic into the two different
        // things it was conflating. A DOMAIN resolves to its base type, so a
        // column typed 'ssn' generates string exactly as varchar(11) would. A
        // composite has members rather than an underlying scalar, so it is
        // captured unresolved and refused by name.
        //
        // format_type(typbasetype, typtypmod) renders the base type WITH its
        // modifiers ("character varying(11)"), which is not the bare shape
        // every other DbType in this snapshot carries -- SplitRenderedType
        // separates them again.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.typname AS type_name,
                       -- ::text is required, not cosmetic: typtype is the
                       -- internal single-byte ""char"" type, and Npgsql refuses
                       -- to read that as a string ('Reading as System.String is
                       -- not supported for fields having DataTypeName char').
                       t.typtype::text AS type_kind,
                       CASE WHEN t.typtype = 'd'
                            THEN pg_catalog.format_type(t.typbasetype, t.typtypmod) END AS base_type,
                       NOT t.typnotnull AS is_nullable
                FROM pg_type t
                JOIN pg_namespace n ON n.oid = t.typnamespace
                LEFT JOIN pg_class c ON c.oid = t.typrelid
                WHERE n.nspname = @schema
                  AND (t.typtype = 'd' OR (t.typtype = 'c' AND c.relkind = 'c'))
                ORDER BY t.typname";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string typeName = reader.GetString(0);
                char kind = reader.GetString(1)[0];
                string? baseType = await reader.IsDBNullAsync(2) ? null : reader.GetString(2);

                var ut = new UserTypeSchema
                {
                    Name = typeName,
                    Schema = _schema,
                    Kind = kind == 'd' ? UserTypeKind.Domain : UserTypeKind.Composite,
                    IsNullable = await reader.IsDBNullAsync(3) || reader.GetBoolean(3)
                };

                if (baseType != null)
                {
                    var (bare, max, prec, scale) = UserTypeResolution.SplitRenderedType(baseType);
                    ut.UnderlyingDbType = bare;
                    ut.MaxLength = max;
                    ut.Precision = prec;
                    ut.Scale = scale;
                }

                schema.UserTypes[typeName] = ut;
            }
        }

        // Composite members, so a refusal can name the shape it refused. Kept
        // in a second pass rather than joined above: pg_attribute has one row
        // per member and the outer query has one per type, and merging them
        // would make a type with no members vanish from the result set.
        foreach (var composite in schema.UserTypes.Values)
        {
            if (composite.Kind != UserTypeKind.Composite)
                continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT a.attname,
                       pg_catalog.format_type(a.atttypid, a.atttypmod) AS member_type,
                       NOT a.attnotnull AS is_nullable
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_type t ON t.typrelid = c.oid
                JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE t.typname = @typeName AND n.nspname = @schema
                  AND a.attnum > 0 AND NOT a.attisdropped
                ORDER BY a.attnum";
            AddSchemaParam(cmd);
            var nameParam = cmd.CreateParameter();
            nameParam.ParameterName = "@typeName";
            nameParam.Value = composite.Name;
            cmd.Parameters.Add(nameParam);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var (bare, max, prec, scale) = UserTypeResolution.SplitRenderedType(reader.GetString(1));
                composite.Members.Add(new ColumnSchema
                {
                    Name = reader.GetString(0),
                    DbType = bare,
                    MaxLength = max,
                    Precision = prec,
                    Scale = scale,
                    IsNullable = await reader.IsDBNullAsync(2) || reader.GetBoolean(2)
                });
            }
        }

        // Extract scalar functions (spec 014, FR-001). pg_proc rather than
        // information_schema.routines, because only pg_proc carries the two
        // exclusions this needs:
        //
        //   prokind = 'f'  -- excludes aggregates ('a'), window functions ('w')
        //                     and procedures ('p'). Aggregates and window
        //                     functions are out of scope (spec SS4) and have to
        //                     be excluded AT CAPTURE deliberately: an aggregate
        //                     reaching FunctionSchema would emit a method whose
        //                     call semantics the generated SELECT cannot
        //                     express. Procedures already have their own block.
        //   NOT proretset  -- excludes set-returning functions, which are the
        //                     table-valued functions deferred to their own spec
        //                     (014-plan.md SS6). Capturing one as a scalar would
        //                     generate a method returning the first column of
        //                     the first row and call it the function's value.
        //
        // prokind is PG11+. On PG10 and earlier this block captures nothing
        // rather than mis-capturing: the column does not exist, the query
        // errors, and an extractor that threw here would take the whole pull
        // down over an optional feature.
        //
        // Keyed by SIGNATURE, not by name. PostgreSQL permits genuine overloads
        // -- f(int) and f(text) are two functions sharing one name -- and a
        // name-keyed dictionary would silently keep whichever the catalog
        // returned last. Both are captured, both carry the same bare Name, and
        // the generator folds them to one method name and reports JNT2023. The
        // synthesized key has precedent: MySQL inline enums are keyed
        // {Table}{Column}, which is likewise not the type's own name.
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT p.proname AS fn_name,
                       pg_catalog.format_type(p.prorettype, NULL) AS return_type,
                       COALESCE(p.proargnames, ARRAY[]::text[]) AS arg_names,
                       ARRAY(SELECT pg_catalog.format_type(t, NULL)
                             FROM unnest(p.proargtypes) AS t) AS arg_types
                FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = @schema
                  AND p.prokind = 'f'
                  AND NOT p.proretset
                ORDER BY p.proname, p.oid";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string fnName = reader.GetString(0);
                var (retBare, retMax, retPrec, retScale) =
                    UserTypeResolution.SplitRenderedType(reader.GetString(1));
                string[] argNames = reader.GetFieldValue<string[]>(2);
                string[] argTypes = reader.GetFieldValue<string[]>(3);

                var fn = new FunctionSchema
                {
                    Name = fnName,
                    Schema = _schema,
                    Return = new FunctionReturn
                    {
                        DbType = retBare,
                        MaxLength = retMax,
                        Precision = retPrec,
                        Scale = retScale
                    }
                };

                for (int i = 0; i < argTypes.Length; i++)
                {
                    var (bare, max, prec, scale) = UserTypeResolution.SplitRenderedType(argTypes[i]);
                    fn.Params.Add(new FunctionParam
                    {
                        // proargnames is null for a function declared with
                        // unnamed arguments, and shorter than proargtypes is
                        // impossible but cheap to guard. $1/$2 matches what
                        // Postgres itself calls them.
                        Name = i < argNames.Length && !string.IsNullOrEmpty(argNames[i])
                            ? argNames[i]
                            : "$" + (i + 1),
                        DbType = bare,
                        MaxLength = max,
                        Precision = prec,
                        Scale = scale
                    });
                }

                schema.Functions[UserTypeResolution.FunctionKey(fnName, argTypes)] = fn;
            }
        }
        catch (PostgresException)
        {
            // PG10 and earlier: prokind does not exist. No functions captured,
            // every other entity unaffected, and the generator's back-compat
            // path (FR-008) is exactly the same one an empty dictionary takes.
        }

        UserTypeResolution.Apply(schema);

        return schema;
    }

    /// <summary>
    /// Resolves the dbType to record from information_schema's data_type and udt_name.
    /// Postgres reports data_type 'USER-DEFINED' for extension/domain/enum types
    /// (e.g. citext) while the concrete type name lives in udt_name, so fall back to
    /// udt_name in that case.
    ///
    /// Postgres also reports data_type 'ARRAY' (not the element type) for any
    /// array column, with the real shape in udt_name as an underscore-prefixed
    /// internal type name (confirmed live: text[] -> udt_name "_text", integer[]
    /// -> "_int4"). Without this, the captured DbType was the literal string
    /// "ARRAY" -- which DialectMapper.MapDbTypeToCSharp doesn't recognize (falls
    /// through to "object") and which never ends in "[]", making
    /// MapDbTypeToCSharp's own array-unwrapping branch unreachable dead code
    /// from every live extraction. Stripping the leading underscore and
    /// appending "[]" (e.g. "_text" -> "text[]") produces exactly the shape
    /// that branch expects, so it recurses into the element type normally.
    /// </summary>
    public static string ResolveDbType(string dataType, string udtName)
    {
        if (dataType == "USER-DEFINED" && !string.IsNullOrEmpty(udtName))
            return udtName;
        if (dataType == "ARRAY" && udtName.StartsWith("_", StringComparison.Ordinal))
            return udtName.Substring(1) + "[]";
        return dataType;
    }

    private void AddSchemaParam(NpgsqlCommand cmd)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@schema";
        p.Value = _schema;
        cmd.Parameters.Add(p);
    }
}
