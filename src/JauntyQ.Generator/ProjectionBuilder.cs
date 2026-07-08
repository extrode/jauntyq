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

        // Build alias-to-table map
        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            aliasToTable[key] = table.TableName;
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
                    if (schema.Tables.TryGetValue(table.TableName, out var tableSchema))
                    {
                        foreach (var schemaCol in tableSchema.Columns.Values)
                        {
                            projection.Columns.Add(new ProjectionColumn
                            {
                                Name = DialectMapper.ToPascalCase(schemaCol.Name),
                                Type = DialectMapper.MapColumnToCSharp(schemaCol),
                                Ordinal = ordinal++,
                                SourceName = schemaCol.Name
                            });
                        }
                    }
                }
                continue;
            }

            // Resolve column to schema
            ColumnSchema? schemaColumn = ResolveColumn(col.TableAlias, col.ColumnName, query, schema, aliasToTable);

            // Determine property name
            string propName = !string.IsNullOrEmpty(col.OutputAlias)
                ? DialectMapper.ToPascalCase(col.OutputAlias)
                : DialectMapper.ToPascalCase(col.ColumnName);

            // Determine C# type
            string csharpType = schemaColumn != null
                ? DialectMapper.MapColumnToCSharp(schemaColumn)
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
    {
        if (!string.IsNullOrEmpty(tableAlias))
        {
            if (aliasToTable.TryGetValue(tableAlias, out var tableName) &&
                schema.Tables.TryGetValue(tableName, out var tableSchema) &&
                tableSchema.Columns.TryGetValue(columnName, out var schemaColumn))
            {
                return schemaColumn;
            }
            return null;
        }

        // Unqualified — search all referenced tables
        foreach (var table in query.Tables)
        {
            if (schema.Tables.TryGetValue(table.TableName, out var tableSchema) &&
                tableSchema.Columns.TryGetValue(columnName, out var schemaColumn))
            {
                return schemaColumn;
            }
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
        string csharpType = DialectMapper.MapDbTypeToCSharp(dbType, isNullable: !notNull);

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
