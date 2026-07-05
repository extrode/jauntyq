using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using JauntyQ.Schema;
using JauntyQ.SqlParser;
using JauntyQ.SqlParser.IR;
using JauntyQ.SqlParser.Tokens;

namespace JauntyQ.Generator;

[Generator(LanguageNames.CSharp)]
public class JauntyQGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Shared one-time shape guard (exists exactly once per compilation)
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("JauntyQShapeGuard.g.cs", SourceText.From(CodeEmitter.EmitShapeGuardSource(), Encoding.UTF8)));

        // Collect SQL query files (migrations are a separate pipeline)
        var sqlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && !IsMigrationPath(f.Path));

        // Migration files: db/migrations/NNNN_name.sql, applied to the
        // snapshot in filename order to form the effective schema.
        var migrationFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && IsMigrationPath(f.Path))
            .Select(static (f, ct) => (Name: System.IO.Path.GetFileName(f.Path), Text: f.GetText(ct)?.ToString() ?? ""))
            .Collect();

        // Collect schema files
        var schemaFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase));

        // Get the first schema file's text
        var schemaText = schemaFiles.Collect().Select(static (files, _) =>
            files.IsEmpty ? null : files[0].GetText()?.ToString());

        // Parse the snapshot once and apply pending migrations to produce
        // the EFFECTIVE schema that validation and emission run against.
        // Cached until the snapshot json or any migration text changes.
        var schemaState = schemaText
            .Combine(migrationFiles)
            .Select(static (pair, _) => SchemaState.Load(pair.Left, pair.Right));

        // Auto-CRUD is on unless the consumer sets <JauntyQAutoCrud>false</JauntyQAutoCrud>
        var autoCrudEnabled = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            !(provider.GlobalOptions.TryGetValue("build_property.JauntyQAutoCrud", out var value)
              && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)));

        // Path-set projection: content edits leave every path string equal, so
        // the common prefix (and everything keyed on it) stays cached.
        var commonPrefix = sqlFiles
            .Select(static (f, _) => f.Path)
            .Collect()
            .Select(static (paths, _) => ComputeCommonDirectoryPrefix(paths))
            .WithTrackingName("JauntyQ_CommonPrefix");

        // Per-file pipeline: editing one .sql re-parses and re-emits only that file.
        var perFile = sqlFiles
            .Combine(commonPrefix)
            .Combine(schemaState)
            .Select(static (pair, ct) =>
            {
                var ((sqlFile, prefix), schema) = pair;
                return ProcessFile(sqlFile, prefix, schema, ct);
            })
            .WithTrackingName("JauntyQ_PerFile");

        context.RegisterSourceOutput(perFile, static (ctx, result) =>
        {
            foreach (var diag in result.Diagnostics)
                ctx.ReportDiagnostic(diag.ToDiagnostic());
            if (result.HintName != null && result.Source != null)
                ctx.AddSource(result.HintName, SourceText.From(result.Source, Encoding.UTF8));
        });

        // Duplicate-query detection (JNT8005): plain string comparison in its
        // own node, so body edits never invalidate the expensive aggregate.
        var fingerprints = perFile
            .Select(static (r, _) => (Entity: r.Summary.EntityName, Method: r.Summary.MethodName, Fingerprint: r.Fingerprint))
            .Collect()
            .WithTrackingName("JauntyQ_Fingerprints");

        context.RegisterSourceOutput(fingerprints, static (ctx, entries) =>
        {
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Fingerprint))
                    continue;
                if (!groups.TryGetValue(entry.Fingerprint!, out var members))
                {
                    members = new System.Collections.Generic.List<string>();
                    groups[entry.Fingerprint!] = members;
                }
                members.Add($"{entry.Entity}.{entry.Method}");
            }
            foreach (var group in groups)
            {
                if (group.Value.Count < 2)
                    continue;
                group.Value.Sort(StringComparer.Ordinal);
                ctx.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT8005, Location.None,
                    $"Queries {string.Join(", ", group.Value)} compile to identical SQL; consolidate them to keep one plan and one maintenance point."));
            }
        });

        // Aggregated outputs (synthetics, POCO overloads, row POCOs, entity
        // cores, JauntyDb) depend only on each file's value-equatable shape
        // summary — body edits that keep the shape leave all of it cached.
        var summaries = perFile
            .Select(static (r, _) => r.Summary)
            .Collect()
            .WithComparer(FileSummaryArrayComparer.Instance)
            .WithTrackingName("JauntyQ_Summaries");

        var aggregateInput = summaries
            .Combine(schemaState)
            .Combine(autoCrudEnabled)
            .WithTrackingName("JauntyQ_AggregateInput");

        context.RegisterSourceOutput(aggregateInput, static (ctx, pair) =>
        {
            var ((fileSummaries, schema), autoCrud) = pair;
            EmitAggregates(ctx, fileSummaries, schema, autoCrud);
        });
    }

    /// <summary>
    /// Parses, validates and emits a single user .sql file. Pure function of
    /// (file, common prefix, schema state) so the incremental pipeline can
    /// cache it per file.
    /// </summary>
    private static FileResult ProcessFile(
        AdditionalText sqlFile,
        string commonPrefix,
        SchemaState schemaState,
        CancellationToken cancellationToken)
    {
        string entityName = ExtractEntityName(sqlFile.Path, commonPrefix);
        string methodName = System.IO.Path.GetFileNameWithoutExtension(sqlFile.Path);

        var sqlText = sqlFile.GetText(cancellationToken)?.ToString();
        if (string.IsNullOrWhiteSpace(sqlText))
            return FileResult.None(entityName, methodName, claims: false);

        // JNT2004 (H1): the folder/file name becomes a C# class/method name.
        // Reject anything that is not a bare identifier so a hostile path can't
        // inject code into the generated entity or method signature. "Queries"
        // is the safe catch-all entity and is always valid.
        if (!IdentifierGuard.IsValidIdentifier(entityName) || !IdentifierGuard.IsValidIdentifier(methodName))
        {
            var badNameDiag = ImmutableArray.Create(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                $"SQL file path yields an illegal C# name (entity '{entityName}', method '{methodName}'). Rename the file/folder to a valid identifier (letters, digits, underscore; not starting with a digit)."));
            return FileResult.WithDiagnostics(entityName, methodName, badNameDiag);
        }

        // From here on the file claims its entity.method slot: a user file
        // always overrides the auto-CRUD synthetic of the same name, even
        // when it currently fails validation.
        if (schemaState.ParseFailed)
            return FileResult.None(entityName, methodName, claims: true); // JNT6001 comes from the aggregate step

        var schema = schemaState.Schema;

        // Parse directives (before tokenization, since tokenizer strips comments)
        var (directives, cleanedSql) = Directives.DirectiveParser.Parse(sqlText!);

        // Tokenize (use cleaned SQL with directive lines removed)
        var tokens = SqlTokenizer.Tokenize(cleanedSql);

        // Parse
        var queryModel = SqlParser.SqlParser.Parse(tokens, methodName);

        // Validate
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var errors = QueryValidator.Validate(queryModel, schema);
        bool hasErrors = false;
        foreach (var error in errors)
        {
            diagnostics.Add(DiagnosticInfo.ForValidation(error));
            if (error.Severity == ValidationSeverity.Error)
                hasErrors = true;
        }

        if (hasErrors || schema == null)
            return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());

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
                diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT7001, problem));
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }
        }

        // -- @stream preconditions (JNT3003). Streaming yields rows lazily off
        // the reader, so it is only meaningful for multi-row SELECTs and cannot
        // combine with @first (single row) or @proc.
        if (directives.IsStream)
        {
            string? problem = null;
            if (queryModel.StatementType != StatementType.Select)
                problem = "-- @stream is only valid on SELECT queries";
            else if (directives.IsFirst)
                problem = "-- @stream cannot be combined with -- @first (streaming is for multi-row results)";
            else if (directives.IsProc)
                problem = "-- @stream cannot be combined with -- @proc";

            if (problem != null)
            {
                diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3003, problem));
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }
        }

        string source;
        string? canonicalTable = null;

        if (queryModel.StatementType != StatementType.Select)
        {
            // CRUD (INSERT, UPDATE, DELETE) — no projection, returns int

            // JNT2004 (H2): parameter names are emitted as C# parameter
            // identifiers; reject illegal ones before they reach emission.
            var crudParamDiag = ValidateParameterNames(queryModel, entityName, methodName);
            if (crudParamDiag != null)
            {
                diagnostics.Add(crudParamDiag);
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }

            // JNT4003: Check for unresolved parameter types
            bool hasUnresolvedCrudParam = false;
            foreach (var param in queryModel.Parameters)
            {
                string inferredType = CodeEmitter.InferCrudParameterType(param, queryModel, schema, directives);
                if (inferredType == "object")
                {
                    diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT4003, param.Name));
                    hasUnresolvedCrudParam = true;
                }
            }

            if (hasUnresolvedCrudParam)
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());

            source = CodeEmitter.EmitCrud(queryModel, cleanedSql, entityName, schema, directives);
        }
        else
        {
            // SELECT — build projection and emit reader code
            var projection = ProjectionBuilder.Build(queryModel, schema);

            // JNT2004 (C1): every projected name is emitted as a C# member;
            // reject any that is not a bare identifier so a hostile column
            // alias cannot inject code into the generated projection type.
            foreach (var pcol in projection.Columns)
            {
                if (!IdentifierGuard.IsValidIdentifier(pcol.Name))
                {
                    diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                        $"Column or alias maps to an illegal C# identifier '{pcol.Name}'. Use a SQL alias that is a valid identifier (letters, digits, underscore; not starting with a digit)."));
                    return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
                }
            }

            // JNT2004 (H2): parameter names are emitted as C# parameter identifiers.
            var selectParamDiag = ValidateParameterNames(queryModel, entityName, methodName);
            if (selectParamDiag != null)
            {
                diagnostics.Add(selectParamDiag);
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }

            // JNT4003: Check for unresolved parameter types
            bool hasUnresolvedParam = false;
            foreach (var param in queryModel.Parameters)
            {
                string inferredType = CodeEmitter.InferParameterType(param.Name, queryModel, projection, schema, directives);
                if (inferredType == "object")
                {
                    diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT4003, param.Name));
                    hasUnresolvedParam = true;
                }
            }

            if (hasUnresolvedParam)
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());

            // Full-row single-table projections return the canonical
            // per-table POCO instead of a query-specific Result type.
            string? canonicalRowType = ResolveCanonicalRowType(queryModel, projection, schema, directives);
            if (canonicalRowType != null)
                canonicalTable = queryModel.Tables[0].TableName;

            source = CodeEmitter.Emit(queryModel, projection, cleanedSql, entityName, schema, directives, canonicalRowType);
        }

        return new FileResult(
            $"{entityName}.{methodName}.g.cs",
            source,
            diagnostics.ToImmutable(),
            new FileSummary(entityName, methodName, claims: true, emitted: true, canonicalTable),
            ComputeFingerprint(tokens));
    }

    /// <summary>
    /// JNT2004 (H2): SQL @parameter names are emitted verbatim as C# parameter
    /// identifiers. The tokenizer already restricts them to [A-Za-z0-9_], but a
    /// leading digit (e.g. @1x) or a C# keyword still produces broken/ambiguous
    /// code. Returns a diagnostic for the first illegal name, else null.
    /// (Keywords are safe because emission '@'-escapes them, so only the
    /// bare-identifier shape is enforced here.)
    /// </summary>
    private static DiagnosticInfo? ValidateParameterNames(QueryModel queryModel, string entityName, string methodName)
    {
        foreach (var param in queryModel.Parameters)
        {
            if (!IdentifierGuard.IsValidIdentifier(param.Name))
            {
                return DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                    $"Parameter '@{param.Name}' in {entityName}.{methodName} is not a valid C# identifier. Rename it to letters, digits, and underscores, not starting with a digit.");
            }
        }
        return null;
    }

    /// <summary>
    /// Normalized token stream: keywords/identifiers case-folded, whitespace
    /// and comments gone. Two queries with equal fingerprints do identical
    /// work regardless of formatting (JNT8005).
    /// </summary>
    private static string ComputeFingerprint(System.Collections.Generic.List<Token> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.End)
                break;
            if (sb.Length > 0)
                sb.Append(' ');
            switch (token.Type)
            {
                case TokenType.Keyword:
                    sb.Append(token.Value.ToUpperInvariant());
                    break;
                case TokenType.Identifier:
                    sb.Append(token.Value.ToLowerInvariant());
                    break;
                case TokenType.Parameter:
                    sb.Append('@').Append(token.Value.ToLowerInvariant());
                    break;
                case TokenType.Literal:
                    sb.Append('\'').Append(token.Value).Append('\'');
                    break;
                default:
                    sb.Append(token.Value);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Emits everything that spans files: auto-CRUD synthetics, POCO write
    /// overloads, canonical row POCOs, entity cores and the JauntyDb facade.
    /// Driven by the file summaries only, so it re-runs when a file is
    /// added/removed/renamed or changes shape — not on every body edit.
    /// </summary>
    private static void EmitAggregates(
        SourceProductionContext context,
        ImmutableArray<FileSummary> files,
        SchemaState schemaState,
        bool autoCrud)
    {
        // A schema snapshot alone is enough: auto-CRUD generates without any .sql files
        if (files.IsEmpty && !schemaState.HasJson)
            return;

        foreach (var diag in schemaState.MigrationDiagnostics)
            context.ReportDiagnostic(diag.ToDiagnostic());

        if (schemaState.ParseFailed)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                JauntyDiagnostics.JNT6001, Location.None));
            return;
        }

        var schema = schemaState.Schema;

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

        foreach (var file in files)
        {
            if (file.Claims)
                claimedMethods.Add($"{file.EntityName}.{file.MethodName}");
            if (file.Emitted)
                entityNames.Add(file.EntityName);
            if (file.CanonicalTable != null)
                neededRowTables.Add(file.CanonicalTable);
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

    /// <summary>
    /// Computes the common directory prefix across all SQL file paths.
    /// This identifies the root SQL folder so we can extract entity subfolder names.
    /// Uses the grandparent directory of each file (parent-of-parent) to avoid
    /// swallowing the entity subfolder when all files share the same entity.
    /// </summary>
    internal static string ComputeCommonDirectoryPrefix(ImmutableArray<string> paths)
    {
        if (paths.IsEmpty)
            return "";

        // Collect grandparent directories (go up 2 levels from the file).
        // For files like "db/Products/GetAll.sql", grandparent = "db".
        // For root-level files like "db/GetOrphaned.sql", grandparent = "" (empty).
        // We use grandparents so the common prefix doesn't include the entity folder.
        var dirs = new System.Collections.Generic.List<string>();
        foreach (var path in paths)
        {
            string dir = (System.IO.Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
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
            string firstDir = (System.IO.Path.GetDirectoryName(paths[0]) ?? "").Replace('\\', '/');
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

/// <summary>
/// Parsed schema snapshot state. Rebuilt only when the snapshot json changes;
/// otherwise every pipeline node shares the same cached instance, so the
/// default reference equality is exactly right for incremental caching.
/// </summary>
internal sealed class SchemaState
{
    public DatabaseSchema? Schema { get; }
    public bool ParseFailed { get; }
    public bool HasJson { get; }

    /// <summary>JNT9001/JNT9002 from parsing and simulating pending migrations.</summary>
    public ImmutableArray<DiagnosticInfo> MigrationDiagnostics { get; }

    private SchemaState(DatabaseSchema? schema, bool parseFailed, bool hasJson,
        ImmutableArray<DiagnosticInfo> migrationDiagnostics = default)
    {
        Schema = schema;
        ParseFailed = parseFailed;
        HasJson = hasJson;
        MigrationDiagnostics = migrationDiagnostics.IsDefault
            ? ImmutableArray<DiagnosticInfo>.Empty
            : migrationDiagnostics;
    }

    public static SchemaState Load(string? json, ImmutableArray<(string Name, string Text)> migrations)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SchemaState(null, parseFailed: false, hasJson: false);

        DatabaseSchema snapshot;
        try
        {
            snapshot = SchemaLoader.Load(json!);
        }
        catch
        {
            return new SchemaState(null, parseFailed: true, hasJson: true);
        }

        if (migrations.IsDefaultOrEmpty)
            return new SchemaState(snapshot, parseFailed: false, hasJson: true);

        // Pending migrations are applied to a clone of the snapshot in
        // filename order; the generator validates and emits against the
        // post-migration world, so a breaking migration fails this build
        // instead of the deploy.
        var ordered = new System.Collections.Generic.List<(string Name, string Text)>(migrations);
        ordered.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        var parsed = new System.Collections.Generic.List<(string FileName, System.Collections.Generic.List<Migrations.MigrationStatement> Statements)>();
        foreach (var migration in ordered)
            parsed.Add((migration.Name, Migrations.MigrationParser.Parse(migration.Text)));

        var errors = new System.Collections.Generic.List<ValidationError>();
        var effective = Migrations.SchemaSimulator.Apply(snapshot, parsed, errors);

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>(errors.Count);
        foreach (var error in errors)
            diagnostics.Add(DiagnosticInfo.From(error.Descriptor!, error.Message));

        return new SchemaState(effective, parseFailed: false, hasJson: true,
            diagnostics.MoveToImmutable());
    }
}

/// <summary>
/// Value-equatable shape of one .sql file: everything the aggregate pass
/// (synthetics, POCO overloads, facade) needs to know about it. A body edit
/// that keeps this equal leaves the aggregate output cached.
/// </summary>
internal sealed class FileSummary : IEquatable<FileSummary>
{
    public string EntityName { get; }
    public string MethodName { get; }

    /// <summary>True when the file suppresses the same-named auto-CRUD synthetic.</summary>
    public bool Claims { get; }

    /// <summary>True when the file produced a source file (parsed and validated clean).</summary>
    public bool Emitted { get; }

    /// <summary>Table whose canonical row POCO this query returns, if any.</summary>
    public string? CanonicalTable { get; }

    public FileSummary(string entityName, string methodName, bool claims, bool emitted, string? canonicalTable)
    {
        EntityName = entityName;
        MethodName = methodName;
        Claims = claims;
        Emitted = emitted;
        CanonicalTable = canonicalTable;
    }

    public bool Equals(FileSummary? other) =>
        other != null
        && Claims == other.Claims
        && Emitted == other.Emitted
        && string.Equals(EntityName, other.EntityName, StringComparison.Ordinal)
        && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal)
        && string.Equals(CanonicalTable, other.CanonicalTable, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as FileSummary);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = EntityName.GetHashCode();
            hash = hash * 31 + MethodName.GetHashCode();
            hash = hash * 31 + (CanonicalTable?.GetHashCode() ?? 0);
            hash = hash * 31 + (Claims ? 2 : 0) + (Emitted ? 1 : 0);
            return hash;
        }
    }
}

/// <summary>
/// Result of processing one user .sql file: the generated source (if any),
/// its diagnostics, and the shape summary the aggregate pass consumes.
/// </summary>
internal sealed class FileResult
{
    public string? HintName { get; }
    public string? Source { get; }
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
    public FileSummary Summary { get; }

    /// <summary>Normalized-SQL fingerprint; null when the file emitted nothing.</summary>
    public string? Fingerprint { get; }

    public FileResult(string? hintName, string? source, ImmutableArray<DiagnosticInfo> diagnostics, FileSummary summary, string? fingerprint = null)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
        Summary = summary;
        Fingerprint = fingerprint;
    }

    public static FileResult None(string entityName, string methodName, bool claims) =>
        new FileResult(null, null, ImmutableArray<DiagnosticInfo>.Empty,
            new FileSummary(entityName, methodName, claims, emitted: false, canonicalTable: null));

    public static FileResult WithDiagnostics(string entityName, string methodName, ImmutableArray<DiagnosticInfo> diagnostics) =>
        new FileResult(null, null, diagnostics,
            new FileSummary(entityName, methodName, claims: true, emitted: false, canonicalTable: null));
}

/// <summary>
/// Cache-friendly diagnostic carrier: plain strings compared by value, so a
/// re-run that produces the same diagnostics counts as unchanged. Rehydrated
/// into a Roslyn Diagnostic only at report time.
/// </summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    private readonly string _id;
    private readonly string _title;
    private readonly string _messageFormat;
    private readonly string _category;
    private readonly DiagnosticSeverity _severity;
    private readonly string? _arg;

    private DiagnosticInfo(string id, string title, string messageFormat, string category, DiagnosticSeverity severity, string? arg)
    {
        _id = id;
        _title = title;
        _messageFormat = messageFormat;
        _category = category;
        _severity = severity;
        _arg = arg;
    }

    public static DiagnosticInfo From(DiagnosticDescriptor descriptor, string arg) =>
        new DiagnosticInfo(descriptor.Id, descriptor.Title.ToString(), descriptor.MessageFormat.ToString(),
            descriptor.Category, descriptor.DefaultSeverity, arg);

    public static DiagnosticInfo ForValidation(ValidationError error) =>
        new DiagnosticInfo(error.Code, error.Code, error.Message, "JauntyQ",
            error.Severity == ValidationSeverity.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            arg: null);

    public Diagnostic ToDiagnostic()
    {
        var descriptor = new DiagnosticDescriptor(_id, _title, _messageFormat, _category, _severity, isEnabledByDefault: true);
        return _arg != null
            ? Diagnostic.Create(descriptor, Location.None, _arg)
            : Diagnostic.Create(descriptor, Location.None);
    }

    public bool Equals(DiagnosticInfo? other) =>
        other != null
        && _severity == other._severity
        && string.Equals(_id, other._id, StringComparison.Ordinal)
        && string.Equals(_title, other._title, StringComparison.Ordinal)
        && string.Equals(_messageFormat, other._messageFormat, StringComparison.Ordinal)
        && string.Equals(_category, other._category, StringComparison.Ordinal)
        && string.Equals(_arg, other._arg, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = _id.GetHashCode();
            hash = hash * 31 + _messageFormat.GetHashCode();
            hash = hash * 31 + (_arg?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// Sequence comparer for the collected file summaries: lets the aggregate
/// node treat "same files, same shapes" as an unchanged input even though
/// Collect() produces a fresh array instance on every upstream change.
/// </summary>
internal sealed class FileSummaryArrayComparer : System.Collections.Generic.IEqualityComparer<ImmutableArray<FileSummary>>
{
    public static readonly FileSummaryArrayComparer Instance = new FileSummaryArrayComparer();

    private FileSummaryArrayComparer() { }

    public bool Equals(ImmutableArray<FileSummary> x, ImmutableArray<FileSummary> y)
    {
        if (x.IsDefault || y.IsDefault)
            return x.IsDefault && y.IsDefault;
        if (x.Length != y.Length)
            return false;
        for (int i = 0; i < x.Length; i++)
        {
            if (!x[i].Equals(y[i]))
                return false;
        }
        return true;
    }

    public int GetHashCode(ImmutableArray<FileSummary> array)
    {
        if (array.IsDefault)
            return 0;
        unchecked
        {
            int hash = array.Length;
            foreach (var summary in array)
                hash = hash * 31 + summary.GetHashCode();
            return hash;
        }
    }
}
