using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static class ProjectionBuilder
{
    public static ProjectionModel Build(QueryModel query, DatabaseSchema schema)
    {
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

        foreach (var col in query.Columns)
        {
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
                                Type = DialectMapper.MapDbTypeToCSharp(schemaCol.DbType, schemaCol.IsNullable),
                                Ordinal = ordinal++,
                                SourceName = schemaCol.Name
                            });
                        }
                    }
                }
                continue;
            }

            // Resolve column to schema
            string? resolvedTable = null;
            ColumnSchema? schemaColumn = null;

            if (!string.IsNullOrEmpty(col.TableAlias))
            {
                if (aliasToTable.TryGetValue(col.TableAlias, out var tableName))
                {
                    if (schema.Tables.TryGetValue(tableName, out var tableSchema))
                    {
                        if (tableSchema.Columns.TryGetValue(col.ColumnName, out schemaColumn))
                        {
                            resolvedTable = tableName;
                        }
                    }
                }
            }
            else
            {
                // Unqualified — search all referenced tables
                foreach (var table in query.Tables)
                {
                    if (schema.Tables.TryGetValue(table.TableName, out var tableSchema))
                    {
                        if (tableSchema.Columns.TryGetValue(col.ColumnName, out schemaColumn))
                        {
                            resolvedTable = table.TableName;
                            break;
                        }
                    }
                }
            }

            // Determine property name
            string propName = !string.IsNullOrEmpty(col.OutputAlias)
                ? DialectMapper.ToPascalCase(col.OutputAlias)
                : DialectMapper.ToPascalCase(col.ColumnName);

            // Determine C# type
            string csharpType = schemaColumn != null
                ? DialectMapper.MapDbTypeToCSharp(schemaColumn.DbType, schemaColumn.IsNullable)
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
}
