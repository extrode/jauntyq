using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

[Generator(LanguageNames.CSharp)]
public class JauntyQGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Shared one-time shape guard (exists exactly once per compilation)
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("JauntyQShapeGuard.g.cs", SourceText.From(CodeEmitter.EmitShapeGuardSource(), Encoding.UTF8)));

        // Collect SQL files
        var sqlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

        // Collect schema files
        var schemaFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase));

        // Get the first schema file's text
        var schemaText = schemaFiles.Collect().Select(static (files, _) =>
            files.IsEmpty ? null : files[0].GetText()?.ToString());

        // Collect ALL SQL files so we can compute entity names and emit aggregated files
        var allSqlFiles = sqlFiles.Collect();
        var allSqlWithSchema = allSqlFiles.Combine(schemaText);

        context.RegisterSourceOutput(allSqlWithSchema, static (ctx, pair) =>
        {
            var (sqlFileArray, schemaJson) = pair;
            ExecuteAll(ctx, sqlFileArray, schemaJson);
        });
    }

    private static void ExecuteAll(
        SourceProductionContext context,
        ImmutableArray<AdditionalText> sqlFiles,
        string? schemaJson)
    {
        if (sqlFiles.IsEmpty)
            return;

        // Load schema once
        DatabaseSchema? schema = null;
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                schema = SchemaLoader.Load(schemaJson!);
            }
            catch
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    JauntyDiagnostics.JNT6001, Location.None));
                return;
            }
        }

        // Compute the common directory prefix across all SQL file paths
        string commonPrefix = ComputeCommonDirectoryPrefix(sqlFiles);

        // Track unique entity names for JauntyDb generation
        var entityNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        // Process each SQL file
        foreach (var sqlFile in sqlFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var sqlText = sqlFile.GetText(context.CancellationToken)?.ToString();
            if (string.IsNullOrWhiteSpace(sqlText))
                continue;

            // Extract entity and method names from path
            string entityName = ExtractEntityName(sqlFile.Path, commonPrefix);
            string methodName = System.IO.Path.GetFileNameWithoutExtension(sqlFile.Path);

            // Parse directives (before tokenization, since tokenizer strips comments)
            var (directives, cleanedSql) = Directives.DirectiveParser.Parse(sqlText!);

            // Tokenize (use cleaned SQL with directive lines removed)
            var tokens = SqlTokenizer.Tokenize(cleanedSql);

            // Parse
            var queryModel = SqlParser.SqlParser.Parse(tokens, methodName);

            // Validate
            var errors = QueryValidator.Validate(queryModel, schema);
            bool hasErrors = false;
            foreach (var error in errors)
            {
                var severity = error.Severity == ValidationSeverity.Warning
                    ? DiagnosticSeverity.Warning
                    : DiagnosticSeverity.Error;
                // Use a simple descriptor with the pre-formatted message
                var descriptor = new DiagnosticDescriptor(
                    error.Code, error.Code, error.Message, "JauntyQ", severity, true);
                context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));

                if (error.Severity == ValidationSeverity.Error)
                    hasErrors = true;
            }

            if (hasErrors)
                continue;

            if (schema == null)
                continue;

            string source;

            if (queryModel.StatementType != StatementType.Select)
            {
                // CRUD (INSERT, UPDATE, DELETE) — no projection, returns int
                source = CodeEmitter.EmitCrud(queryModel, cleanedSql, entityName, schema, directives);
            }
            else
            {
                // SELECT — build projection and emit reader code
                var projection = ProjectionBuilder.Build(queryModel, schema);

                // JNT4003: Check for unresolved parameter types
                foreach (var param in queryModel.Parameters)
                {
                    string inferredType = CodeEmitter.InferParameterType(param.Name, queryModel, projection, schema, directives);
                    if (inferredType == "object")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            JauntyDiagnostics.JNT4003, Location.None, param.Name));
                    }
                }

                source = CodeEmitter.Emit(queryModel, projection, cleanedSql, entityName, schema, directives);
            }

            // Emit per-query source file
            context.AddSource($"{entityName}.{methodName}.g.cs", SourceText.From(source, Encoding.UTF8));

            entityNames.Add(entityName);
        }

        // Emit entity core files (constructor + _conn field per entity)
        foreach (var entity in entityNames)
        {
            var coreSource = CodeEmitter.EmitEntityCore(entity);
            context.AddSource($"{entity}.Core.g.cs", SourceText.From(coreSource, Encoding.UTF8));
        }

        // Emit JauntyDb class
        if (entityNames.Count > 0)
        {
            var sortedEntities = new System.Collections.Generic.List<string>(entityNames);
            sortedEntities.Sort(StringComparer.Ordinal);
            var dbSource = CodeEmitter.EmitJauntyDb(sortedEntities);
            context.AddSource("JauntyDb.g.cs", SourceText.From(dbSource, Encoding.UTF8));
        }
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

    /// <summary>
    /// Computes the common directory prefix across all SQL file paths.
    /// This identifies the root SQL folder so we can extract entity subfolder names.
    /// Uses the grandparent directory of each file (parent-of-parent) to avoid
    /// swallowing the entity subfolder when all files share the same entity.
    /// </summary>
    internal static string ComputeCommonDirectoryPrefix(ImmutableArray<AdditionalText> files)
    {
        if (files.IsEmpty)
            return "";

        // Collect grandparent directories (go up 2 levels from the file).
        // For files like "db/Products/GetAll.sql", grandparent = "db".
        // For root-level files like "db/GetOrphaned.sql", grandparent = "" (empty).
        // We use grandparents so the common prefix doesn't include the entity folder.
        var dirs = new System.Collections.Generic.List<string>();
        foreach (var file in files)
        {
            string dir = (System.IO.Path.GetDirectoryName(file.Path) ?? "").Replace('\\', '/');
            string grandparent = (System.IO.Path.GetDirectoryName(dir) ?? "").Replace('\\', '/');
            dirs.Add(grandparent);
        }

        // Filter to non-empty grandparents (files with entity subfolders).
        // Root-level files (grandparent empty) shouldn't affect the common prefix.
        var nonEmpty = new System.Collections.Generic.List<string>();
        foreach (var d in dirs)
        {
            if (d.Length > 0)
                nonEmpty.Add(d);
        }

        if (nonEmpty.Count == 0)
        {
            // ALL files are at most 1 folder deep — use the direct parent as root
            string firstDir = (System.IO.Path.GetDirectoryName(files[0].Path) ?? "").Replace('\\', '/');
            if (firstDir.Length > 0 && !firstDir.EndsWith("/"))
                firstDir += "/";
            return firstDir;
        }

        // Compute common prefix across non-empty grandparent directories
        string prefix = nonEmpty[0];
        for (int i = 1; i < nonEmpty.Count; i++)
        {
            string dir = nonEmpty[i];
            int len = System.Math.Min(prefix.Length, dir.Length);
            int matchEnd = 0;
            for (int j = 0; j < len; j++)
            {
                if (char.ToLowerInvariant(prefix[j]) != char.ToLowerInvariant(dir[j]))
                    break;
                if (prefix[j] == '/')
                    matchEnd = j + 1;
                if (j == len - 1)
                    matchEnd = len;
            }
            prefix = prefix.Substring(0, matchEnd);
        }

        // Ensure prefix ends with separator
        if (prefix.Length > 0 && !prefix.EndsWith("/"))
            prefix += "/";

        return prefix;
    }

}
