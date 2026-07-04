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

        // Auto-CRUD is on unless the consumer sets <JauntyQAutoCrud>false</JauntyQAutoCrud>
        var autoCrudEnabled = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            !(provider.GlobalOptions.TryGetValue("build_property.JauntyQAutoCrud", out var value)
              && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)));

        // Collect ALL SQL files so we can compute entity names and emit aggregated files
        var allSqlFiles = sqlFiles.Collect();
        var allSqlWithSchema = allSqlFiles.Combine(schemaText).Combine(autoCrudEnabled);

        context.RegisterSourceOutput(allSqlWithSchema, static (ctx, pair) =>
        {
            var ((sqlFileArray, schemaJson), autoCrud) = pair;
            ExecuteAll(ctx, sqlFileArray, schemaJson, autoCrud);
        });
    }

    private static void ExecuteAll(
        SourceProductionContext context,
        ImmutableArray<AdditionalText> sqlFiles,
        string? schemaJson,
        bool autoCrud)
    {
        // A schema snapshot alone is enough: auto-CRUD generates without any .sql files
        if (sqlFiles.IsEmpty && string.IsNullOrWhiteSpace(schemaJson))
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

        // entity.method slots claimed by user SQL files: a user file always
        // overrides the auto-CRUD synthetic of the same name
        var claimedMethods = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // tables whose canonical row POCO (e.g. Shipper) is actually used
        var neededRowTables = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // per-table synthetic write methods (POCO overloads forward to these;
        // user-overridden methods own their signature and get no overload)
        var syntheticWrites = new System.Collections.Generic.Dictionary<string, (string Entity, bool Insert, bool Update, bool Delete, bool Upsert)>(StringComparer.OrdinalIgnoreCase);

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

            claimedMethods.Add($"{entityName}.{methodName}");

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

            // -- @identity preconditions (JNT7001). Synthetic auto-CRUD SQL
            // only carries the directive when resolvable; this gate catches
            // user files.
            if (directives.ReturnsIdentity)
            {
                string? problem = null;
                if (directives.IsProc)
                    problem = "-- @identity cannot be combined with -- @proc";
                else if (queryModel.StatementType != StatementType.Insert)
                    problem = "-- @identity is only valid on INSERT statements";
                else if (string.IsNullOrEmpty(schema.Dialect))
                    problem = "-- @identity requires a dialect in the schema snapshot; re-run 'jaunty schema pull' with the current CLI";
                else if (CodeEmitter.ResolveIdentityInfo(queryModel, schema, directives) == null)
                    problem = $"-- @identity requires exactly one identity column on the target table '{queryModel.TargetTable}'";

                if (problem != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        JauntyDiagnostics.JNT7001, Location.None, problem));
                    continue;
                }
            }

            string source;

            if (queryModel.StatementType != StatementType.Select)
            {
                // CRUD (INSERT, UPDATE, DELETE) — no projection, returns int

                // JNT4003: Check for unresolved parameter types
                bool hasUnresolvedCrudParam = false;
                foreach (var param in queryModel.Parameters)
                {
                    string inferredType = CodeEmitter.InferCrudParameterType(param, queryModel, schema, directives);
                    if (inferredType == "object")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            JauntyDiagnostics.JNT4003, Location.None, param.Name));
                        hasUnresolvedCrudParam = true;
                    }
                }

                if (hasUnresolvedCrudParam)
                    continue;

                source = CodeEmitter.EmitCrud(queryModel, cleanedSql, entityName, schema, directives);
            }
            else
            {
                // SELECT — build projection and emit reader code
                var projection = ProjectionBuilder.Build(queryModel, schema);

                // JNT4003: Check for unresolved parameter types
                bool hasUnresolvedParam = false;
                foreach (var param in queryModel.Parameters)
                {
                    string inferredType = CodeEmitter.InferParameterType(param.Name, queryModel, projection, schema, directives);
                    if (inferredType == "object")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            JauntyDiagnostics.JNT4003, Location.None, param.Name));
                        hasUnresolvedParam = true;
                    }
                }

                if (hasUnresolvedParam)
                    continue;

                // Full-row single-table projections return the canonical
                // per-table POCO instead of a query-specific Result type.
                string? canonicalRowType = ResolveCanonicalRowType(queryModel, projection, schema, directives);
                if (canonicalRowType != null)
                    neededRowTables.Add(queryModel.Tables[0].TableName);

                source = CodeEmitter.Emit(queryModel, projection, cleanedSql, entityName, schema, directives, canonicalRowType);
            }

            // Emit per-query source file
            context.AddSource($"{entityName}.{methodName}.g.cs", SourceText.From(source, Encoding.UTF8));

            entityNames.Add(entityName);
        }

        // Auto-CRUD: synthesize per-table CRUD for everything the user didn't write
        if (autoCrud && schema != null)
        {
            foreach (var synth in AutoCrud.Synthesize(schema))
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (!claimedMethods.Add($"{synth.EntityName}.{synth.MethodName}"))
                    continue; // user SQL file wins

                if (synth.IsUpsert)
                {
                    // Dialect-native upsert bypasses the minimal SQL parser;
                    // correctness comes from the schema snapshot itself.
                    string upsertSource = CodeEmitter.EmitUpsert(synth.EntityName, schema.Tables[synth.TableName], schema.Dialect);
                    context.AddSource($"{synth.EntityName}.Upsert.auto.g.cs", SourceText.From(upsertSource, Encoding.UTF8));
                    entityNames.Add(synth.EntityName);
                    RecordSyntheticWrite(syntheticWrites, synth.TableName, synth.EntityName, "Upsert");
                    continue;
                }

                var (directives, cleanedSql) = Directives.DirectiveParser.Parse(synth.Sql);
                var tokens = SqlTokenizer.Tokenize(cleanedSql);
                var queryModel = SqlParser.SqlParser.Parse(tokens, synth.MethodName);

                // Synthesized SQL is derived from the schema itself; validation
                // failures here indicate a synthesis bug, not a user error.
                var errors = QueryValidator.Validate(queryModel, schema);
                if (errors.Exists(e => e.Severity == ValidationSeverity.Error))
                    continue;

                string source;
                if (queryModel.StatementType != StatementType.Select)
                {
                    source = CodeEmitter.EmitCrud(queryModel, cleanedSql, synth.EntityName, schema, directives);
                }
                else
                {
                    var projection = ProjectionBuilder.Build(queryModel, schema);
                    string? canonicalRowType = ResolveCanonicalRowType(queryModel, projection, schema, directives);
                    if (canonicalRowType != null)
                        neededRowTables.Add(queryModel.Tables[0].TableName);
                    source = CodeEmitter.Emit(queryModel, projection, cleanedSql, synth.EntityName, schema, directives, canonicalRowType);
                }

                context.AddSource($"{synth.EntityName}.{synth.MethodName}.auto.g.cs", SourceText.From(source, Encoding.UTF8));
                entityNames.Add(synth.EntityName);
                if (synth.MethodName is "Insert" or "Update" or "Delete")
                    RecordSyntheticWrite(syntheticWrites, synth.TableName, synth.EntityName, synth.MethodName);
            }
        }

        // POCO-taking overloads for synthetic write methods
        if (schema != null)
        {
            foreach (var kvp in syntheticWrites)
            {
                if (!schema.Tables.TryGetValue(kvp.Key, out var tableSchema))
                    continue;
                var info = kvp.Value;
                string rowType = Inflector.RowTypeName(DialectMapper.ToPascalCase(tableSchema.Name));
                string overloadSource = CodeEmitter.EmitPocoOverloads(
                    info.Entity, rowType, tableSchema, schema.Dialect,
                    info.Insert, info.Update, info.Delete, info.Upsert);
                context.AddSource($"{info.Entity}.Poco.auto.g.cs", SourceText.From(overloadSource, Encoding.UTF8));
                neededRowTables.Add(tableSchema.Name);
            }
        }

        // Emit canonical row POCOs (one per table actually used full-row)
        if (schema != null)
        {
            foreach (var tableName in neededRowTables)
            {
                if (!schema.Tables.TryGetValue(tableName, out var tableSchema))
                    continue;
                string entityPascal = DialectMapper.ToPascalCase(tableSchema.Name);
                string rowType = Inflector.RowTypeName(entityPascal);
                context.AddSource($"{entityPascal}.Row.g.cs",
                    SourceText.From(CodeEmitter.EmitRowPoco(rowType, tableSchema), Encoding.UTF8));
            }
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
