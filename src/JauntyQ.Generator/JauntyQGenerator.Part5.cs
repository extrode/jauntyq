using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;
public partial class JauntyQGenerator : IIncrementalGenerator
{

    private static void RecordSyntheticWrite(
        System.Collections.Generic.Dictionary<string, (string Entity, bool Insert, bool Update, bool Delete, bool Upsert)> map,
        string tableName, string entityName, string method)
    {
        map.TryGetValue(tableName, out var info);
        info.Entity = entityName;
        switch (method)
        {
            case "Insert": info.Insert = true; break;
            case "Update": info.Update = true; break;
            case "Delete": info.Delete = true; break;
            case "Upsert": info.Upsert = true; break;
        }
        map[tableName] = info;
    }

    /// <summary>
    /// A query returns the canonical per-table POCO when it selects exactly
    /// the table's full column set, in table order, from a single table,
    /// with no custom result shaping directives.
    /// </summary>
    private static string? ResolveCanonicalRowType(
        QueryModel queryModel,
        ProjectionModel projection,
        DatabaseSchema schema,
        Directives.DirectiveModel directives)
    {
        if (directives.ResultTypeName != null || directives.InlineColumns != null || directives.ResultIsVoid)
            return null;
        if (queryModel.Tables.Count != 1)
            return null;
        if (!schema.Tables.TryGetValue(queryModel.Tables[0].TableName, out var tableSchema))
            return null;
        if (projection.Columns.Count != tableSchema.Columns.Count)
            return null;

        int i = 0;
        foreach (var col in tableSchema.Columns.Values)
        {
            var proj = projection.Columns[i++];
            string sourceName = string.IsNullOrEmpty(proj.SourceName) ? proj.Name : proj.SourceName;
            if (!string.Equals(sourceName, col.Name, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return Inflector.RowTypeName(DialectMapper.ToPascalCase(tableSchema.Name));
    }

    /// <summary>
    /// Files under a "migrations" directory segment are DDL migrations, not
    /// query files. Convention: db/migrations/NNNN_name.sql, applied in
    /// filename order. Keep query entity folders away from that name.
    /// </summary>
    internal static bool IsMigrationPath(string path)
    {
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (string.Equals(segment, "migrations", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts the entity name from a SQL file path by comparing to the common directory prefix.
    /// Files directly in the root SQL folder get entity name "Queries" (catch-all).
    /// Files in a subfolder get the subfolder name as entity name.
    /// </summary>
    internal static string ExtractEntityName(string filePath, string commonPrefix)
    {
        // Normalize separators
        string normalized = filePath.Replace('\\', '/');
        string normalizedPrefix = commonPrefix.Replace('\\', '/');

        // Get the relative path after the common prefix
        string relative;
        if (normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = normalized.Substring(normalizedPrefix.Length);
        }
        else
        {
            // Fallback: just use the filename
            relative = System.IO.Path.GetFileName(filePath);
        }

        // Trim leading separators
        relative = relative.TrimStart('/');

        // Split into segments
        var segments = relative.Split('/');

        // Skip structural prefixes (tables/, views/)
        int entityIndex = 0;
        if (segments.Length >= 3 &&
            (string.Equals(segments[0], "tables", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(segments[0], "views", StringComparison.OrdinalIgnoreCase)))
        {
            entityIndex = 1;
        }

        if (segments.Length >= entityIndex + 2)
        {
            // Has an entity subfolder
            return segments[entityIndex];
        }

        // No subfolder — catch-all
        return "Queries";
    }

    internal static string ComputeCommonDirectoryPrefix(ImmutableArray<AdditionalText> files)
    {
        if (files.IsEmpty)
            return "";
        var paths = ImmutableArray.CreateBuilder<string>(files.Length);
        foreach (var file in files)
            paths.Add(file.Path);
        return ComputeCommonDirectoryPrefix(paths.MoveToImmutable());
    }

}
