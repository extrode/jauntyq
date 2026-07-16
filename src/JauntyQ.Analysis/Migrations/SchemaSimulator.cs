using JauntyQ.Schema;

namespace JauntyQ.Analysis.Migrations;

/// <summary>
/// Applies parsed migration statements to a clone of the schema snapshot,
/// producing the effective (post-migration) schema the generator validates
/// and emits against. Invalid operations (create existing, drop/alter
/// missing) are JNT9002 errors; the statement is skipped and simulation
/// continues best-effort so later statements still surface their own issues.
/// </summary>
public static class SchemaSimulator
{
    public static DatabaseSchema Apply(
        DatabaseSchema snapshot,
        IEnumerable<(string FileName, List<MigrationStatement> Statements)> migrations,
        List<AnalysisDiagnostic> errors)
    {
        var schema = Clone(snapshot);

        foreach (var (fileName, statements) in migrations)
        {
            foreach (var stmt in statements)
            {
                switch (stmt.Kind)
                {
                    case MigrationStatementKind.Ignored:
                        break;

                    case MigrationStatementKind.Unsupported:
                        errors.Add(AnalysisDiagnostic.Warning("JNT9001",
                            $"{fileName}: statement not simulated (effective schema may be incomplete): {Truncate(stmt.RawText)}"));
                        break;

                    case MigrationStatementKind.CreateTable:
                        ApplyCreateTable(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.DropTable:
                        ApplyDropTable(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.AddColumn:
                        ApplyAddColumn(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.DropColumn:
                        ApplyDropColumn(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.AlterColumn:
                        ApplyAlterColumn(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.AlterColumnNullability:
                        ApplyAlterColumnNullability(schema, stmt, fileName, errors);
                        break;

                    case MigrationStatementKind.AddPrimaryKey:
                        ApplyAddPrimaryKey(schema, stmt, fileName, errors);
                        break;
                }
            }
        }

        return schema;
    }

    private static void ApplyCreateTable(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (schema.Tables.ContainsKey(stmt.TableName))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: table '{stmt.TableName}' already exists. If this migration was already deployed, archive it and re-run 'jaunty schema pull'."));
            return;
        }

        var table = new TableSchema { Name = stmt.TableName, Columns = new Dictionary<string, ColumnSchema>() };
        foreach (var col in stmt.Columns)
        {
            if (table.Columns.ContainsKey(col.Name))
            {
                errors.Add(AnalysisDiagnostic.Error("JNT9002",
                    $"{fileName}: duplicate column '{col.Name}' in create table '{stmt.TableName}'."));
                continue;
            }
            table.Columns[col.Name] = Finalize(col, schema.Dialect);
        }
        schema.Tables[stmt.TableName] = table;
    }

    private static void ApplyDropTable(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.Remove(stmt.TableName) && !stmt.IfExists)
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot drop table '{stmt.TableName}': it does not exist in the effective schema."));
        }
        schema.ForeignKeys.RemoveAll(fk =>
            string.Equals(fk.FromTable, stmt.TableName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fk.ToTable, stmt.TableName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyAddColumn(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.TryGetValue(stmt.TableName, out var table))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot add column to '{stmt.TableName}': table does not exist in the effective schema."));
            return;
        }
        foreach (var col in stmt.Columns)
        {
            if (table.Columns.ContainsKey(col.Name))
            {
                errors.Add(AnalysisDiagnostic.Error("JNT9002",
                    $"{fileName}: column '{stmt.TableName}.{col.Name}' already exists. If this migration was already deployed, archive it and re-run 'jaunty schema pull'."));
                continue;
            }
            table.Columns[col.Name] = Finalize(col, schema.Dialect);
        }
    }

    private static void ApplyDropColumn(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.TryGetValue(stmt.TableName, out var table))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot drop column from '{stmt.TableName}': table does not exist in the effective schema."));
            return;
        }

        foreach (var name in stmt.ColumnNames)
        {
            if (!table.Columns.ContainsKey(name))
            {
                errors.Add(AnalysisDiagnostic.Error("JNT9002",
                    $"{fileName}: cannot drop column '{stmt.TableName}.{name}': it does not exist in the effective schema."));
                continue;
            }

            // Rebuild the dictionary: generated ordinals depend on column
            // order, and Dictionary reuses freed slots after Remove, which
            // would corrupt the order of any column added later.
            var rebuilt = new Dictionary<string, ColumnSchema>();
            foreach (var kvp in table.Columns)
            {
                if (!string.Equals(kvp.Key, name, StringComparison.Ordinal))
                    rebuilt[kvp.Key] = kvp.Value;
            }
            table.Columns = rebuilt;
        }

        schema.ForeignKeys.RemoveAll(fk =>
            string.Equals(fk.FromTable, stmt.TableName, StringComparison.OrdinalIgnoreCase) &&
            stmt.ColumnNames.Any(n => string.Equals(fk.FromColumn, n, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ApplyAlterColumn(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.TryGetValue(stmt.TableName, out var table))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot alter column on '{stmt.TableName}': table does not exist in the effective schema."));
            return;
        }

        foreach (var col in stmt.Columns)
        {
            if (!table.Columns.TryGetValue(col.Name, out var existing))
            {
                errors.Add(AnalysisDiagnostic.Error("JNT9002",
                    $"{fileName}: cannot alter column '{stmt.TableName}.{col.Name}': it does not exist in the effective schema."));
                continue;
            }

            // ALTER COLUMN changes type/nullability/facets; key and identity
            // status stay with the existing column.
            var updated = Finalize(col, schema.Dialect);
            updated.IsPrimaryKey = existing.IsPrimaryKey;
            updated.IsIdentity = existing.IsIdentity;
            table.Columns[col.Name] = updated; // in-place value swap keeps order
        }
    }

    /// <summary>
    /// PostgreSQL ALTER COLUMN c SET/DROP NOT NULL: a nullability-only
    /// change. Unlike ApplyAlterColumn, this mutates the existing column in
    /// place rather than replacing it, so DbType/facets/identity/etc. are
    /// completely untouched -- there is no freshly-(mis)parsed replacement
    /// column to merge fields from or forget to preserve.
    /// </summary>
    private static void ApplyAlterColumnNullability(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.TryGetValue(stmt.TableName, out var table))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot alter column on '{stmt.TableName}': table does not exist in the effective schema."));
            return;
        }

        string colName = stmt.ColumnNames.Count > 0 ? stmt.ColumnNames[0] : string.Empty;
        if (!table.Columns.TryGetValue(colName, out var existing))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot alter column '{stmt.TableName}.{colName}': it does not exist in the effective schema."));
            return;
        }

        existing.IsNullable = stmt.NullableAfter;
    }

    private static void ApplyAddPrimaryKey(DatabaseSchema schema, MigrationStatement stmt, string fileName, List<AnalysisDiagnostic> errors)
    {
        if (!schema.Tables.TryGetValue(stmt.TableName, out var table))
        {
            errors.Add(AnalysisDiagnostic.Error("JNT9002",
                $"{fileName}: cannot add a primary key to '{stmt.TableName}': table does not exist in the effective schema."));
            return;
        }

        foreach (var name in stmt.ColumnNames)
        {
            if (!table.Columns.TryGetValue(name, out var existing))
            {
                errors.Add(AnalysisDiagnostic.Error("JNT9002",
                    $"{fileName}: cannot add a primary key on '{stmt.TableName}.{name}': it does not exist in the effective schema."));
                continue;
            }

            existing.IsPrimaryKey = true;
            existing.IsNullable = false;
        }
    }

    /// <summary>
    /// Post-processing the parser cannot do without dialect knowledge:
    /// SQL Server reports rowversion as 'timestamp', which is a datetime on
    /// PostgreSQL, so the concurrency-token flag is dialect-gated.
    /// </summary>
    private static ColumnSchema Finalize(ColumnSchema col, string dialect)
    {
        if (col.DbType == "rowversion" ||
            (col.DbType == "timestamp" && string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase)))
        {
            col.IsRowVersion = true;
            col.IsNullable = false;
        }
        return col;
    }

    // The clone must carry EVERY part of the snapshot, not just what the
    // simulator mutates. It once copied only Tables (minus Indexes) and
    // ForeignKeys, so the moment a project had a single pending migration the
    // effective schema lost its procedures (every -- @call broke with a false
    // JNT2005), sequences (the db.Sequences accessor vanished), and indexes
    // (JNT8004/8006/8007 and unique-index upsert keys silently disappeared).
    private static DatabaseSchema Clone(DatabaseSchema source)
    {
        var clone = new DatabaseSchema { Dialect = source.Dialect };
        foreach (var table in source.Tables.Values)
        {
            var columns = new Dictionary<string, ColumnSchema>();
            foreach (var col in table.Columns.Values)
                columns[col.Name] = CloneColumn(col);

            var indexes = new List<IndexSchema>();
            foreach (var ix in table.Indexes)
            {
                indexes.Add(new IndexSchema
                {
                    Name = ix.Name,
                    Columns = new List<string>(ix.Columns),
                    IsUnique = ix.IsUnique
                });
            }

            clone.Tables[table.Name] = new TableSchema { Name = table.Name, Columns = columns, Indexes = indexes };
        }
        foreach (var fk in source.ForeignKeys)
        {
            clone.ForeignKeys.Add(new ForeignKeySchema
            {
                FromTable = fk.FromTable,
                FromColumn = fk.FromColumn,
                ToTable = fk.ToTable,
                ToColumn = fk.ToColumn
            });
        }
        foreach (var proc in source.Procedures.Values)
        {
            var cloneProc = new ProcedureSchema { Name = proc.Name };
            foreach (var p in proc.Params)
            {
                cloneProc.Params.Add(new ProcedureParam
                {
                    Name = p.Name,
                    DbType = p.DbType,
                    Direction = p.Direction,
                    IsNullable = p.IsNullable,
                    MaxLength = p.MaxLength
                });
            }
            foreach (var rc in proc.Results)
                cloneProc.Results.Add(CloneColumn(rc));
            clone.Procedures[proc.Name] = cloneProc;
        }
        foreach (var seq in source.Sequences.Values)
        {
            clone.Sequences[seq.Name] = new SequenceSchema
            {
                Name = seq.Name,
                StartValue = seq.StartValue,
                Increment = seq.Increment,
                MinValue = seq.MinValue,
                MaxValue = seq.MaxValue,
                CurrentValue = seq.CurrentValue
            };
        }
        return clone;
    }

    private static ColumnSchema CloneColumn(ColumnSchema col) => new()
    {
        Name = col.Name,
        DbType = col.DbType,
        IsNullable = col.IsNullable,
        IsPrimaryKey = col.IsPrimaryKey,
        IsIdentity = col.IsIdentity,
        MaxLength = col.MaxLength,
        Precision = col.Precision,
        Scale = col.Scale,
        IsUnicode = col.IsUnicode,
        IsRowVersion = col.IsRowVersion,
        IsComputed = col.IsComputed
    };

    private static string Truncate(string text) =>
        text.Length <= 80 ? text : text.Substring(0, 77) + "...";
}
