using Extrode.JauntyQ.Schema;
using Microsoft.Data.SqlClient;

namespace Extrode.JauntyQ.Schema.Extraction;

public class SqlServerExtractor : ISchemaExtractor
{
    /// <summary>
    /// Default schema scope, matching <c>PostgresExtractor</c>'s configurable
    /// schema parameter (default "public") and <c>MySqlExtractor</c>'s
    /// single-database parameterization.
    /// Without a schema filter here, two same-named tables in different
    /// schemas (e.g. "dbo.Orders" and "archive.Orders" — routine on SQL
    /// Server, which encourages schema-per-module) silently merge their
    /// columns into one corrupted <c>TableSchema</c>, since
    /// <c>DatabaseSchema.Tables</c> is keyed by bare table name with no
    /// schema concept. Scoping to a single schema (dbo by default) makes all
    /// three relational extractors consistent: each pulls exactly one
    /// schema/database, never merges across scopes.
    /// </summary>
    public const string DefaultSchema = "dbo";

    private readonly string _schema;

    public SqlServerExtractor(string schema = DefaultSchema)
    {
        _schema = string.IsNullOrWhiteSpace(schema) ? DefaultSchema : schema;
    }

    public async Task<DatabaseSchema> ExtractAsync(string connectionString)
    {
        var schema = new DatabaseSchema();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        // Extract tables and columns
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE,
                    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS is_identity,
                    CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
                    CAST(c.CHARACTER_MAXIMUM_LENGTH AS int) AS max_length,
                    CASE WHEN c.DATA_TYPE IN ('decimal','numeric','money','smallmoney') THEN CAST(c.NUMERIC_PRECISION AS int) END AS num_precision,
                    CASE WHEN c.DATA_TYPE IN ('decimal','numeric','money','smallmoney') THEN CAST(c.NUMERIC_SCALE AS int) END AS num_scale,
                    CASE WHEN c.DATA_TYPE IN ('nchar','nvarchar','ntext') THEN 1
                         WHEN c.DATA_TYPE IN ('char','varchar','text') THEN 0 END AS is_unicode,
                    CASE WHEN c.DATA_TYPE IN ('timestamp','rowversion') THEN 1 ELSE 0 END AS is_rowversion,
                    COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsComputed') AS is_computed,
                    -- Spec 015: appended LAST on purpose; every reader ordinal
                    -- below is positional.
                    CASE WHEN t.TABLE_TYPE = 'VIEW' THEN 1 ELSE 0 END AS is_view,
                    -- Spec 014, appended LAST for the reason above. SQL Server
                    -- already reports an alias-typed column's DATA_TYPE as the
                    -- BASE type and names the alias here, exactly as Postgres
                    -- does with domain_name -- so what was missing was never the
                    -- mapping, only the record that the column was declared
                    -- through a user type at all.
                    c.DOMAIN_NAME AS domain_name
                FROM INFORMATION_SCHEMA.TABLES t
                JOIN INFORMATION_SCHEMA.COLUMNS c
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                LEFT JOIN (
                    SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                        ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
                WHERE t.TABLE_TYPE IN ('BASE TABLE', 'VIEW') AND t.TABLE_SCHEMA = @schema
                ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string tableName = reader.GetString(0);
                string columnName = reader.GetString(1);
                string dataType = reader.GetString(2);
                bool isNullable = reader.GetString(3) == "YES";
                bool isIdentity = !await reader.IsDBNullAsync(4) && reader.GetInt32(4) == 1;
                bool isPrimaryKey = reader.GetInt32(5) == 1;
                int? maxLength = await reader.IsDBNullAsync(6) ? null : reader.GetInt32(6);
                int? precision = await reader.IsDBNullAsync(7) ? null : reader.GetInt32(7);
                int? scale = await reader.IsDBNullAsync(8) ? null : reader.GetInt32(8);
                bool? isUnicode = await reader.IsDBNullAsync(9) ? null : reader.GetInt32(9) == 1;
                bool isRowVersion = reader.GetInt32(10) == 1;
                bool isComputed = !await reader.IsDBNullAsync(11) && reader.GetInt32(11) == 1;
                bool isView = reader.GetInt32(12) == 1;
                string? domainName = await reader.IsDBNullAsync(13) ? null : reader.GetString(13);

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
                    DbType = dataType,
                    IsNullable = isNullable,
                    IsPrimaryKey = isPrimaryKey,
                    IsIdentity = isIdentity,
                    MaxLength = maxLength,
                    Precision = precision,
                    Scale = scale,
                    IsUnicode = isUnicode,
                    IsRowVersion = isRowVersion,
                    IsComputed = isComputed,
                    ResolvedFromUserType = domainName
                };
            }
        }

        // Extract indexes (key columns in key order; JNT8xxx analyzer input).
        // A filtered index (has_filter = 1, e.g. CREATE UNIQUE INDEX ... WHERE
        // deleted_at IS NULL) only enforces uniqueness among the rows that
        // match its predicate, not the whole table -- confirmed live (SQL
        // Server via Testcontainers): such an index came back IsUnique=true
        // with no signal at all that it was conditional, which would let
        // UpsertKeyResolver/NPlusOneAnalyzer treat a column as a safe globally
        // -unique upsert/scoping key when two "active" rows could share it.
        // The column list is still useful for the JNT8xxx index-coverage
        // hints, so the index stays captured -- only its IsUnique flag is
        // downgraded. SQL Server has no expression-index concept distinct
        // from a computed column (already a real column in sys.columns), so
        // no separate expression handling is needed here.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.name AS table_name, i.name AS index_name,
                       CAST(CASE WHEN i.is_unique = 1 AND i.has_filter = 0 THEN 1 ELSE 0 END AS bit) AS is_effectively_unique,
                       c.name AS column_name
                FROM sys.indexes i
                JOIN sys.tables t ON i.object_id = t.object_id
                JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id AND ic.is_included_column = 0
                JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE i.name IS NOT NULL AND SCHEMA_NAME(t.schema_id) = @schema
                ORDER BY t.name, i.name, ic.key_ordinal";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                IndexCapture.AddIndexColumn(schema,
                    tableName: reader.GetString(0),
                    indexName: reader.GetString(1),
                    isUnique: reader.GetBoolean(2),
                    columnName: reader.GetString(3));
            }
        }

        // Extract foreign keys
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    fk_tab.name AS from_table,
                    fk_col.name AS from_column,
                    pk_tab.name AS to_table,
                    pk_col.name AS to_column
                FROM sys.foreign_keys fk
                JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                JOIN sys.tables fk_tab ON fkc.parent_object_id = fk_tab.object_id
                JOIN sys.columns fk_col ON fkc.parent_object_id = fk_col.object_id AND fkc.parent_column_id = fk_col.column_id
                JOIN sys.tables pk_tab ON fkc.referenced_object_id = pk_tab.object_id
                JOIN sys.columns pk_col ON fkc.referenced_object_id = pk_col.object_id AND fkc.referenced_column_id = pk_col.column_id
                WHERE SCHEMA_NAME(fk_tab.schema_id) = @schema AND SCHEMA_NAME(pk_tab.schema_id) = @schema";
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

        // Extract stored procedures: parameters, then the first result-set
        // shape per proc. Procs are captured so -- @call can bind to them.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.SPECIFIC_NAME AS proc_name,
                       p.PARAMETER_NAME AS param_name,
                       p.DATA_TYPE AS data_type,
                       p.PARAMETER_MODE AS mode,
                       p.CHARACTER_MAXIMUM_LENGTH AS max_length,
                       CASE WHEN p.DATA_TYPE IN ('decimal','numeric','money','smallmoney') THEN CAST(p.NUMERIC_PRECISION AS int) END AS num_precision,
                       CASE WHEN p.DATA_TYPE IN ('decimal','numeric','money','smallmoney') THEN CAST(p.NUMERIC_SCALE AS int) END AS num_scale,
                       -- Spec 014, appended LAST. A table-valued parameter
                       -- reports a NULL DATA_TYPE and carries its type name
                       -- only here, so without this column a TVP arrives as a
                       -- parameter with an empty type -- which is how it used
                       -- to reach DialectMapper's fallback and generate object
                       -- + JNT2007. Named, it can be refused by JNT2025
                       -- instead.
                       p.USER_DEFINED_TYPE_NAME AS param_udt
                FROM INFORMATION_SCHEMA.ROUTINES r
                LEFT JOIN INFORMATION_SCHEMA.PARAMETERS p
                    ON r.SPECIFIC_NAME = p.SPECIFIC_NAME AND r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA
                WHERE r.ROUTINE_TYPE = 'PROCEDURE' AND r.SPECIFIC_SCHEMA = @schema
                ORDER BY p.SPECIFIC_NAME, p.ORDINAL_POSITION";
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
                // Measured live 2026-08-30, against the assumption: a
                // table-valued parameter reports DATA_TYPE as the literal
                // string 'table type', NOT null. So the pull never crashed on
                // one -- it captured every TVP parameter under a single shared
                // pseudo-type, which is worse in one specific way: two
                // procedures taking two DIFFERENT TVPs looked identical in the
                // snapshot, so drift between them could never be reported, and
                // JNT2025 would have had no type name to print.
                //
                // USER_DEFINED_TYPE_NAME carries the real one. Preferring it
                // for exactly this DATA_TYPE (rather than whenever it is
                // present) keeps an ALIAS-typed parameter on its resolved base
                // type, which is what the alias case wants.
                string paramType = reader.GetString(2) == "table type" &&
                                   !await reader.IsDBNullAsync(7)
                    ? reader.GetString(7)
                    : reader.GetString(2);
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

        // Extract sequences: sys.sequences stores the numeric attributes as
        // sql_variant, so CAST each to bigint for a stable typed read.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT name,
                       CAST(start_value AS bigint) AS start_value,
                       CAST(increment AS bigint) AS increment,
                       CAST(minimum_value AS bigint) AS minimum_value,
                       CAST(maximum_value AS bigint) AS maximum_value,
                       CAST(current_value AS bigint) AS current_value
                FROM sys.sequences
                WHERE SCHEMA_NAME(schema_id) = @schema
                ORDER BY name";
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

        // First result-set shape per proc (may be undeterminable for some).
        foreach (var proc in schema.Procedures.Values)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT name, system_type_name, is_nullable " +
                    "FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 0) " +
                    "WHERE name IS NOT NULL ORDER BY column_ordinal";
                var p = cmd.CreateParameter();
                p.ParameterName = "@sql";
                // Bracket-quoted: an unquoted proc.Name containing a space,
                // hyphen or other non-identifier character breaks the EXEC
                // syntax outright, and the catch below swallows that failure
                // indistinguishably from the genuinely-undeterminable case.
                p.Value = "EXEC [" + proc.Name.Replace("]", "]]") + "]";
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    string colName = reader.GetString(0);
                    string typeName = reader.GetString(1); // e.g. "int", "nvarchar(40)"
                    bool colNullable = !await reader.IsDBNullAsync(2) && reader.GetBoolean(2);
                    int paren = typeName.IndexOf('(');
                    string baseType = paren >= 0 ? typeName.Substring(0, paren).Trim() : typeName.Trim();

                    proc.Results.Add(new ColumnSchema
                    {
                        Name = colName,
                        DbType = baseType,
                        IsNullable = colNullable
                    });
                }
            }
            catch
            {
                // Result shape not statically determinable (dynamic SQL, temp
                // tables, etc.). Leave Results empty; -- @call still works for
                // its parameters / row count, just without a typed row.
            }
        }

        // Extract alias types and table types (spec 014, FR-004).
        //
        // sys.types rather than INFORMATION_SCHEMA.DOMAINS: the latter carries
        // no is_table_type, so a TVP type would arrive looking like an alias
        // over its first column's type and resolve to it -- generating a scalar
        // property for a multi-column type, which is worse than the object it
        // replaces.
        //
        // max_length is BYTES in sys.types, while every MaxLength already in
        // this snapshot is characters (INFORMATION_SCHEMA.CHARACTER_MAXIMUM_LENGTH).
        // Halving it for the Unicode types is what keeps an nvarchar(50) alias
        // from claiming 100.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT t.name AS type_name,
                       bt.name AS base_type,
                       t.is_nullable,
                       CASE WHEN t.max_length = -1 THEN -1
                            WHEN bt.name IN ('nchar','nvarchar') THEN t.max_length / 2
                            ELSE t.max_length END AS max_length,
                       CAST(t.precision AS int) AS num_precision,
                       CAST(t.scale AS int) AS num_scale
                FROM sys.types t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                LEFT JOIN sys.types bt
                    ON bt.user_type_id = t.system_type_id AND bt.is_user_defined = 0
                WHERE t.is_user_defined = 1 AND t.is_table_type = 0 AND s.name = @schema
                ORDER BY t.name";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string typeName = reader.GetString(0);
                string? baseType = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);
                bool isNullable = !await reader.IsDBNullAsync(2) && reader.GetBoolean(2);
                int? maxLength = await reader.IsDBNullAsync(3) ? null : reader.GetInt32(3);
                int? precision = await reader.IsDBNullAsync(4) ? null : reader.GetInt32(4);
                int? scale = await reader.IsDBNullAsync(5) ? null : reader.GetInt32(5);

                // precision/scale are reported for every type, including the
                // string ones where they are 0 and meaningless. Recording a 0
                // would make a varchar alias claim numeric facets it does not
                // have, and DialectMapper reads them.
                bool numeric = baseType is "decimal" or "numeric" or "money" or "smallmoney";

                schema.UserTypes[typeName] = new UserTypeSchema
                {
                    Name = typeName,
                    Schema = _schema,
                    Kind = UserTypeKind.Alias,
                    UnderlyingDbType = baseType,
                    IsNullable = isNullable,
                    MaxLength = maxLength == 0 ? null : maxLength,
                    Precision = numeric ? precision : null,
                    Scale = numeric ? scale : null
                };
            }
        }

        // Table types, WITH their columns. Captured but never resolved: a table
        // type has members rather than an underlying scalar, and the members
        // are the whole reason to capture it. JNT2025 refuses a TVP-taking
        // procedure and prints this list, so a consumer has the shape they need
        // to hand-write the call -- the difference between a refusal and a dead
        // end.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT tt.name AS type_name,
                       c.name AS column_name,
                       ty.name AS column_type,
                       c.is_nullable,
                       CASE WHEN c.max_length = -1 THEN -1
                            WHEN ty.name IN ('nchar','nvarchar') THEN c.max_length / 2
                            ELSE c.max_length END AS max_length,
                       CAST(c.precision AS int) AS num_precision,
                       CAST(c.scale AS int) AS num_scale
                FROM sys.table_types tt
                JOIN sys.schemas s ON s.schema_id = tt.schema_id
                LEFT JOIN sys.columns c ON c.object_id = tt.type_table_object_id
                LEFT JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE s.name = @schema
                ORDER BY tt.name, c.column_id";
            AddSchemaParam(cmd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string typeName = reader.GetString(0);
                if (!schema.UserTypes.TryGetValue(typeName, out var tableType))
                {
                    tableType = new UserTypeSchema
                    {
                        Name = typeName,
                        Schema = _schema,
                        Kind = UserTypeKind.TableType
                    };
                    schema.UserTypes[typeName] = tableType;
                }

                // A table type with no columns cannot be declared, but the LEFT
                // JOIN makes that unrepresentable rather than a crash.
                if (await reader.IsDBNullAsync(1))
                    continue;

                string columnType = await reader.IsDBNullAsync(2) ? string.Empty : reader.GetString(2);
                bool numeric = columnType is "decimal" or "numeric" or "money" or "smallmoney";
                int? maxLength = await reader.IsDBNullAsync(4) ? null : reader.GetInt32(4);
                int? precision = await reader.IsDBNullAsync(5) ? null : reader.GetInt32(5);
                int? scale = await reader.IsDBNullAsync(6) ? null : reader.GetInt32(6);

                tableType.Members.Add(new ColumnSchema
                {
                    Name = reader.GetString(1),
                    DbType = columnType,
                    IsNullable = !await reader.IsDBNullAsync(3) && reader.GetBoolean(3),
                    MaxLength = maxLength == 0 ? null : maxLength,
                    Precision = numeric ? precision : null,
                    Scale = numeric ? scale : null
                });
            }
        }

        // Extract scalar functions (spec 014, FR-001).
        //
        // DATA_TYPE <> 'TABLE' is the exclusion that matters: SQL Server
        // reports a table-valued function's return as 'TABLE', and TVFs are
        // deferred to their own spec (014-plan.md SS6). Captured as scalars they
        // would generate a method returning the first column of the first row.
        // CLR aggregates (sys.objects type 'AF') are not ROUTINE_TYPE
        // 'FUNCTION' here, so they are excluded by this view's own shape rather
        // than by a filter that could rot.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT r.ROUTINE_NAME AS fn_name,
                       r.DATA_TYPE AS return_type,
                       CAST(r.CHARACTER_MAXIMUM_LENGTH AS int) AS return_max_length,
                       CASE WHEN r.DATA_TYPE IN ('decimal','numeric','money','smallmoney')
                            THEN CAST(r.NUMERIC_PRECISION AS int) END AS return_precision,
                       CASE WHEN r.DATA_TYPE IN ('decimal','numeric','money','smallmoney')
                            THEN CAST(r.NUMERIC_SCALE AS int) END AS return_scale,
                       p.PARAMETER_NAME AS param_name,
                       p.DATA_TYPE AS param_type,
                       CAST(p.CHARACTER_MAXIMUM_LENGTH AS int) AS param_max_length,
                       CASE WHEN p.DATA_TYPE IN ('decimal','numeric','money','smallmoney')
                            THEN CAST(p.NUMERIC_PRECISION AS int) END AS param_precision,
                       CASE WHEN p.DATA_TYPE IN ('decimal','numeric','money','smallmoney')
                            THEN CAST(p.NUMERIC_SCALE AS int) END AS param_scale,
                       p.USER_DEFINED_TYPE_NAME AS param_udt
                FROM INFORMATION_SCHEMA.ROUTINES r
                LEFT JOIN INFORMATION_SCHEMA.PARAMETERS p
                    ON r.SPECIFIC_NAME = p.SPECIFIC_NAME
                   AND r.SPECIFIC_SCHEMA = p.SPECIFIC_SCHEMA
                   AND p.IS_RESULT = 'NO'
                WHERE r.ROUTINE_TYPE = 'FUNCTION'
                  AND r.SPECIFIC_SCHEMA = @schema
                  AND r.DATA_TYPE IS NOT NULL
                  AND r.DATA_TYPE <> 'TABLE'
                ORDER BY r.ROUTINE_NAME, p.ORDINAL_POSITION";
            AddSchemaParam(cmd);

            var pending = new Dictionary<string, FunctionSchema>(StringComparer.Ordinal);
            var pendingArgTypes = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string fnName = reader.GetString(0);
                if (!pending.TryGetValue(fnName, out var fn))
                {
                    fn = new FunctionSchema
                    {
                        Name = fnName,
                        Schema = _schema,
                        Return = new FunctionReturn
                        {
                            DbType = reader.GetString(1),
                            MaxLength = await reader.IsDBNullAsync(2) ? null : reader.GetInt32(2),
                            Precision = await reader.IsDBNullAsync(3) ? null : reader.GetInt32(3),
                            Scale = await reader.IsDBNullAsync(4) ? null : reader.GetInt32(4)
                        }
                    };
                    pending[fnName] = fn;
                    pendingArgTypes[fnName] = new List<string>();
                }

                // A function with no parameters yields one row with a null name.
                if (await reader.IsDBNullAsync(5))
                    continue;

                // A parameter typed by an alias reports DATA_TYPE as the BASE
                // type and carries the alias in USER_DEFINED_TYPE_NAME, the
                // same shape a column takes. A TVP cannot reach a scalar
                // function at all -- SQL Server permits table types only on
                // procedure parameters -- so the fallback here is for the
                // alias case, and the 'table type' branch the procedure block
                // needs has nothing to do on this path.
                string paramType = await reader.IsDBNullAsync(6)
                    ? (await reader.IsDBNullAsync(10) ? string.Empty : reader.GetString(10))
                    : reader.GetString(6);

                fn.Params.Add(new FunctionParam
                {
                    Name = reader.GetString(5).TrimStart('@'),
                    DbType = paramType,
                    MaxLength = await reader.IsDBNullAsync(7) ? null : reader.GetInt32(7),
                    Precision = await reader.IsDBNullAsync(8) ? null : reader.GetInt32(8),
                    Scale = await reader.IsDBNullAsync(9) ? null : reader.GetInt32(9),
                    ResolvedFromUserType = await reader.IsDBNullAsync(10) ? null : reader.GetString(10)
                });
                pendingArgTypes[fnName].Add(paramType);
            }

            foreach (var kv in pending)
                schema.Functions[UserTypeResolution.FunctionKey(kv.Key, pendingArgTypes[kv.Key])] = kv.Value;
        }

        UserTypeResolution.Apply(schema);

        return schema;
    }

    private void AddSchemaParam(SqlCommand cmd)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "@schema";
        p.Value = _schema;
        cmd.Parameters.Add(p);
    }
}
