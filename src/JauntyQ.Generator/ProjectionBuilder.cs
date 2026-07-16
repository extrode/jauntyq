using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static class ProjectionBuilder
{
    public static ProjectionModel Build(QueryModel query, DatabaseSchema schema,
        Directives.DirectiveModel? directives = null)
        => Build(query, query.Columns, schema, directives);

    /// <summary>
    /// Builds a projection from an explicit source column list (SELECT list or a
    /// RETURNING list). Plain columns resolve against the query's tables; expression
    /// items type from built-in inference or a -- @type directive.
    /// </summary>
    public static ProjectionModel Build(QueryModel query, List<ColumnRef> sourceColumns,
        DatabaseSchema schema, Directives.DirectiveModel? directives = null)
    {
        string? dialect = schema.Dialect;
        var projection = new ProjectionModel
        {
            Name = query.Name
        };

        // Build alias-to-table map, plus which tables sit on the optional
        // side of an outer join: LEFT JOIN makes the newly-joined table
        // optional; RIGHT/FULL JOIN instead (or also) makes every table
        // already in the FROM/JOIN chain optional, since the preserved side
        // flips. Once a table is marked, it stays marked even if it's the
        // preserved side of a later join in the same chain -- the projected
        // row can still be all-NULL for it.
        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outerJoinedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keysSoFar = new List<string>();
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            aliasToTable[key] = table.TableName;

            if (table.Join == JoinKind.Left || table.Join == JoinKind.Full)
                outerJoinedKeys.Add(key);
            if (table.Join == JoinKind.Right || table.Join == JoinKind.Full)
            {
                foreach (var earlierKey in keysSoFar)
                    outerJoinedKeys.Add(earlierKey);
            }

            keysSoFar.Add(key);
        }

        int ordinal = 0;

        foreach (var col in sourceColumns)
        {
            if (col.IsExpression)
            {
                projection.Columns.Add(BuildExpressionColumn(col, ordinal++, schema, directives, dialect, query, aliasToTable));
                continue;
            }

            if (col.ColumnName == "*")
            {
                // Expand star to all columns from referenced tables
                foreach (var table in query.Tables)
                {
                    if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema))
                    {
                        string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
                        bool starForceNullable = outerJoinedKeys.Contains(key);
                        foreach (var schemaCol in tableSchema!.Columns.Values)
                        {
                            projection.Columns.Add(new ProjectionColumn
                            {
                                Name = DialectMapper.ToPascalCase(schemaCol.Name),
                                Type = MapProjectedColumnType(schemaCol, dialect, starForceNullable),
                                Ordinal = ordinal++,
                                SourceName = schemaCol.Name
                            });
                        }
                    }
                }
                continue;
            }

            // Resolve column to schema
            ColumnSchema? schemaColumn = ResolveColumn(col.TableAlias, col.ColumnName, query, schema, aliasToTable, out string? resolvedTableKey);
            bool forceNullable = resolvedTableKey != null && outerJoinedKeys.Contains(resolvedTableKey);

            // Determine property name
            string propName = !string.IsNullOrEmpty(col.OutputAlias)
                ? DialectMapper.ToPascalCase(col.OutputAlias)
                : DialectMapper.ToPascalCase(col.ColumnName);

            // Determine C# type
            string csharpType = schemaColumn != null
                ? MapProjectedColumnType(schemaColumn, dialect, forceNullable)
                : "object";

            projection.Columns.Add(new ProjectionColumn
            {
                Name = propName,
                Type = csharpType,
                Ordinal = ordinal++,
                SourceName = !string.IsNullOrEmpty(col.OutputAlias) ? col.OutputAlias : col.ColumnName
            });
        }

        return projection;
    }

    /// <summary>
    /// Resolves a (optionally alias-qualified) column reference against the
    /// query's tables, exactly as a plain SELECT-list column would be.
    /// </summary>
    private static ColumnSchema? ResolveColumn(string tableAlias, string columnName,
        QueryModel query, DatabaseSchema schema, Dictionary<string, string> aliasToTable)
        => ResolveColumn(tableAlias, columnName, query, schema, aliasToTable, out _);

    /// <summary>
    /// Same resolution as above, but also reports the alias-or-table-name key
    /// of the table the column was resolved against (null when resolved
    /// through a CTE, or unresolved) so the caller can check it against the
    /// outer-join nullability set.
    /// </summary>
    private static ColumnSchema? ResolveColumn(string tableAlias, string columnName,
        QueryModel query, DatabaseSchema schema, Dictionary<string, string> aliasToTable,
        out string? resolvedTableKey)
    {
        resolvedTableKey = null;

        if (!string.IsNullOrEmpty(tableAlias))
        {
            if (!aliasToTable.TryGetValue(tableAlias, out var tableName))
                return null;
            if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema) &&
                SchemaLookup.TryGetColumn(tableSchema!, columnName, out var schemaColumn))
            {
                resolvedTableKey = tableAlias;
                return schemaColumn;
            }
            // Not a schema table: the FROM source may be a CTE's virtual table.
            return ResolveThroughCtes(tableName, columnName, query.Ctes, schema, depth: 0);
        }

        // Unqualified — search all referenced tables
        foreach (var table in query.Tables)
        {
            if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema) &&
                SchemaLookup.TryGetColumn(tableSchema!, columnName, out var schemaColumn))
            {
                resolvedTableKey = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
                return schemaColumn;
            }
        }

        // Unqualified and not in any schema table: the query may read FROM a CTE.
        foreach (var table in query.Tables)
        {
            var viaCte = ResolveThroughCtes(table.TableName, columnName, query.Ctes, schema, depth: 0);
            if (viaCte != null)
                return viaCte;
        }

        return null;
    }

    /// <summary>
    /// Maps a resolved column's C# type for projection, forcing nullable when
    /// the column's table sits on the optional side of an outer join. The
    /// schema's own NOT NULL constraint doesn't survive a LEFT/RIGHT/FULL JOIN
    /// with no match, so a projected non-nullable read would throw at runtime
    /// on a NULL value the join legitimately produces.
    /// </summary>
    private static string MapProjectedColumnType(ColumnSchema column, string? dialect, bool forceNullable)
    {
        if (!forceNullable || column.IsRowVersion)
            return DialectMapper.MapColumnToCSharp(column, dialect);

        return DialectMapper.MapDbTypeToCSharp(column.DbType, isNullable: true,
            length: column.Precision ?? column.MaxLength, dialect: dialect);
    }

    /// <summary>
    /// Resolves a column selected FROM a CTE by tracing the CTE's output item
    /// back to a real schema column (or, for an expression item, the parser's
    /// shape inference). Without this, CTE-sourced plain columns silently
    /// projected as <c>object</c> with no diagnostic. Internal so the
    /// parameter-inference fallback (ResolveBoundColumnWide) can trace renamed
    /// CTE outputs the same way.
    /// </summary>
    internal static ColumnSchema? ResolveThroughCtes(string cteName, string columnName,
        List<CteRef> ctes, DatabaseSchema schema, int depth)
    {
        if (depth > 8)
            return null; // cycle guard (WITH RECURSIVE is rejected upstream)

        foreach (var cte in ctes)
        {
            if (!string.Equals(cte.Name, cteName, StringComparison.OrdinalIgnoreCase))
                continue;

            var body = cte.Body;
            var outputs = body.Columns.Count > 0 ? body.Columns : body.Returning;

            // Find the body output that produces this virtual column: a
            // declared column list (WITH t (a, b) AS ...) maps positionally
            // onto the body projection; otherwise the output name matches.
            ColumnRef? source = null;
            if (cte.DeclaredColumns.Count > 0)
            {
                for (int i = 0; i < cte.DeclaredColumns.Count && i < outputs.Count; i++)
                {
                    if (string.Equals(cte.DeclaredColumns[i], columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        source = outputs[i];
                        break;
                    }
                }
            }
            else
            {
                foreach (var item in outputs)
                {
                    string outputName = !string.IsNullOrEmpty(item.OutputAlias) ? item.OutputAlias : item.ColumnName;
                    if (string.Equals(outputName, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        source = item;
                        break;
                    }
                }
            }

            if (source == null || source.ColumnName == "*")
                return null;

            if (source.IsExpression)
            {
                // Expression item: the best available type is the parser's
                // shape inference (count(...) -> bigint, predicate -> boolean).
                // No inference -> unresolved, exactly as before.
                if (string.IsNullOrEmpty(source.InferredDbType))
                    return null;
                return new ColumnSchema
                {
                    Name = columnName,
                    DbType = source.InferredDbType!,
                    IsNullable = !source.InferredNotNull
                };
            }

            // Plain column: resolve against the body's own tables (respecting
            // an alias qualifier); when the body itself reads another CTE,
            // recurse through the same declaration list.
            foreach (var table in body.Tables)
            {
                if (!string.IsNullOrEmpty(source.TableAlias))
                {
                    string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
                    if (!string.Equals(key, source.TableAlias, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (SchemaLookup.TryGetTable(schema, table.TableName, out var ts) &&
                    SchemaLookup.TryGetColumn(ts!, source.ColumnName, out var schemaColumn))
                {
                    return schemaColumn;
                }
            }
            foreach (var table in body.Tables)
            {
                var nested = ResolveThroughCtes(table.TableName, source.ColumnName, ctes, schema, depth + 1);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        return null;
    }

    /// <summary>
    /// SUM/AVG over an exact numeric column (decimal/numeric/money) stays an
    /// exact decimal in Postgres, MySQL/MariaDB, and SQL Server alike — only
    /// approximate (float/real) or integer arguments have dialect-varying
    /// promotion rules, which are out of scope here (still require -- @type).
    /// Always nullable regardless of the source column's own nullability:
    /// unlike COUNT, SUM/AVG return SQL NULL when zero rows match, even over
    /// a NOT NULL column.
    /// </summary>
    private static readonly HashSet<string> ExactNumericDbTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "decimal", "numeric", "money", "smallmoney"
    };

    private static string? InferAggregateDbType(ColumnSchema argColumn)
    {
        string normalized = argColumn.DbType.IndexOf('(') is var paren && paren >= 0
            ? argColumn.DbType.Substring(0, paren).Trim()
            : argColumn.DbType.Trim();

        return ExactNumericDbTypes.Contains(normalized) ? "decimal" : null;
    }

    /// <summary>
    /// Types an expression projection item: a matching -- @type directive wins
    /// (its dbtype mapped through DialectMapper, nullable unless the parser also
    /// inferred NOT NULL), else the parser's built-in inference, else — for a
    /// bare sum(col)/avg(col) — the resolved argument column's own DB type.
    /// When nothing resolves, the column carries UnresolvedExpressionAlias so
    /// the generator raises JNT3005.
    /// </summary>
    private static ProjectionColumn BuildExpressionColumn(ColumnRef col, int ordinal,
        DatabaseSchema schema, Directives.DirectiveModel? directives, string? dialect,
        QueryModel query, Dictionary<string, string> aliasToTable)
    {
        string propName = DialectMapper.ToPascalCase(col.OutputAlias);

        string? dbType = null;
        bool notNull = col.InferredNotNull;
        bool fromTypeDirective = false;

        if (directives?.TypeDirectives != null)
        {
            foreach (var td in directives.TypeDirectives)
            {
                if (string.Equals(td.Alias, col.OutputAlias, StringComparison.OrdinalIgnoreCase))
                {
                    dbType = td.DbType;
                    fromTypeDirective = true;
                    // A declared type is nullable unless a NOT NULL shape inference also matched.
                    break;
                }
            }
        }

        if (dbType == null && !string.IsNullOrEmpty(col.InferredDbType))
            dbType = col.InferredDbType;

        if (dbType == null && !fromTypeDirective && !string.IsNullOrEmpty(col.AggregateFunction))
        {
            var argColumn = ResolveColumn(col.AggregateArgTableAlias, col.AggregateArgColumnName, query, schema, aliasToTable);
            if (argColumn != null)
                dbType = InferAggregateDbType(argColumn);
        }

        // The parser's built-in COUNT(...) inference always yields "bigint",
        // which is correct for Postgres/MySQL but wrong for SQL Server: a bare
        // COUNT/COUNT(*) there returns a 32-bit int on the wire (64-bit counts
        // need the separate COUNT_BIG), so the default int? cast throws
        // InvalidCastException at read time. Only the built-in default is
        // overridden — an explicit -- @type directive still wins as written.
        if (!fromTypeDirective && dbType == "bigint" &&
            string.Equals(dialect, "sqlserver", StringComparison.OrdinalIgnoreCase))
            dbType = "int";

        if (dbType == null)
        {
            // Unresolved: emit an object placeholder and flag for JNT3005.
            return new ProjectionColumn
            {
                Name = propName,
                Type = "object",
                Ordinal = ordinal,
                SourceName = col.OutputAlias,
                IsExpression = true,
                UnresolvedExpressionAlias = col.OutputAlias
            };
        }

        // NOT NULL only when the parser's shape inference proved it; an
        // @type-only expression stays nullable.
        string csharpType = DialectMapper.MapDbTypeToCSharp(dbType, isNullable: !notNull, dialect: dialect);

        return new ProjectionColumn
        {
            Name = propName,
            Type = csharpType,
            Ordinal = ordinal,
            SourceName = col.OutputAlias,
            IsExpression = true
        };
    }
}
