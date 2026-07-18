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
        if (!SchemaLookup.TryGetTable(schema, queryModel.Tables[0].TableName, out var resolvedTableSchema))
            return null;
        var tableSchema = resolvedTableSchema!;
        if (projection.Columns.Count != tableSchema.Columns.Count)
            return null;

        // AUD-R64-01 (fix 2): a table whose columns collide under
        // DialectMapper.ToPascalCase (JNT2011) has no safely-emittable
        // shared row type at all -- routing a full-row query to it here
        // would leave that query's own generated source referencing a type
        // JauntyQGenerator's row-POCO emission loop (Part4.cs) will not
        // emit. Fall through to this query's own per-projection type
        // instead, which the pre-existing JNT3009 duplicate-result-column
        // check (Part2.cs) already guards independently.
        if (FindDuplicateColumnPropertyName(tableSchema.Columns.Values) != null)
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
    /// Files under a "ddl" directory segment are CREATE TABLE / ALTER TABLE
    /// statements that DEFINE the base schema when no JSON snapshot has been
    /// pulled. Convention: db/ddl/*.sql, applied in filename order to build an
    /// initial schema from an empty database. This is deliberately a distinct
    /// folder from "migrations": db/ddl/*.sql is the baseline schema source,
    /// while db/migrations/*.sql is incremental change ON TOP of a baseline
    /// (a JSON snapshot today, or a ddl-built schema now). Keep query entity
    /// folders away from both names.
    /// </summary>
    internal static bool IsDdlPath(string path)
    {
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (string.Equals(segment, "ddl", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts the entity name from a SQL file path by comparing to the common directory prefix.
    /// Files directly in the root SQL folder get entity name "Queries" (catch-all).
    /// Files in a subfolder get the subfolder name as entity name.
    ///
    /// Thin forwarding wrapper (audit round 61) over the canonical algorithm in
    /// <see cref="JauntyQ.Analysis.EntityNameResolver.ExtractEntityName"/>, which
    /// JauntyQ.Cli's ImpactCommand/UsageManifestBuilder now call directly instead
    /// of maintaining their own copy. Kept here, rather than deleted, so
    /// InternalCachingTypeTests.cs's AUD-R31-01 regression test (which calls this
    /// method by this exact name) stays unedited.
    /// </summary>
    internal static string ExtractEntityName(string filePath, string commonPrefix)
        => JauntyQ.Analysis.EntityNameResolver.ExtractEntityName(filePath, commonPrefix);

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
