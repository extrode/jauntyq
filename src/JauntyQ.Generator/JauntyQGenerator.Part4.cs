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
                // errors here indicate a synthesis bug, not a user error, so
                // emission is skipped rather than surfaced as a build error.
                // Warnings (JNT8xxx performance advice) are real findings
                // about the user's own schema/indexes, so they ARE reported —
                // unlike errors, they were previously computed and discarded.
                var errors = QueryValidator.Validate(queryModel, schema);
                bool hasSynthError = false;
                foreach (var error in errors)
                {
                    if (error.Severity == ValidationSeverity.Error)
                        hasSynthError = true;
                    else
                        context.ReportDiagnostic(DiagnosticInfo.ForValidation(error).ToDiagnostic());
                }
                if (hasSynthError)
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

                // BulkInsert(IEnumerable<Row>): a dialect-native set-based insert
                // for tables that have a synthetic Insert (and thus a row POCO).
                if (info.Insert && !string.IsNullOrEmpty(schema.Dialect))
                {
                    string bulkSource = CodeEmitter.EmitBulkInsert(info.Entity, rowType, tableSchema, schema.Dialect);
                    context.AddSource($"{info.Entity}.BulkInsert.auto.g.cs", SourceText.From(bulkSource, Encoding.UTF8));
                }
            }
        }

        // Emit canonical row POCOs (one per table actually used full-row)
        if (schema != null)
        {
            foreach (var tableName in neededRowTables)
            {
                if (!schema.Tables.TryGetValue(tableName, out var tableSchema))
                    continue;

                // JNT2004 (C2): row-POCO member names come from schema-JSON
                // column names via ToPascalCase. That transform sanitizes
                // hostile characters today, but the trust boundary must not
                // rely on it: gate every emitted member on IsValidIdentifier so
                // a malicious snapshot cannot inject code if the transform ever
                // changes. Skip the table and report rather than emit.
                bool rowNameOk = true;
                foreach (var rcol in tableSchema.Columns.Values)
                {
                    if (!IdentifierGuard.IsValidIdentifier(DialectMapper.ToPascalCase(rcol.Name)))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2004, Location.None,
                            $"Table '{tableSchema.Name}' has a column '{rcol.Name}' that maps to an illegal C# identifier; fix the schema snapshot."));
                        rowNameOk = false;
                        break;
                    }
                }
                if (!rowNameOk)
                    continue;

                // JNT2007: a column whose db type has no case in
                // DialectMapper.MapDbTypeToCSharp silently degrades to
                // `object`, which is unusable without a manual cast at every
                // call site. Surface it here — the one place every table's
                // columns are visited regardless of which query touches
                // them — instead of leaving it to be discovered only by
                // noticing the generated property type.
                foreach (var ucol in tableSchema.Columns.Values)
                {
                    // Rowversion concurrency tokens bypass the normal dbType
                    // switch entirely (MapColumnToCSharp special-cases them to
                    // byte[]?); checking the raw dbType here would otherwise
                    // false-positive on a column that's already handled.
                    if (!ucol.IsRowVersion && DialectMapper.IsUnmappedDbType(ucol.DbType, ucol.IsNullable, ucol.Precision ?? ucol.MaxLength))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2007, Location.None,
                            $"Column '{tableSchema.Name}.{ucol.Name}' has db type '{ucol.DbType}', which has no mapping in DialectMapper and degrades to 'object'. " +
                            $"Add a case for it or accept the untyped column and cast at the call site."));
                    }
                }

                string entityPascal = DialectMapper.ToPascalCase(tableSchema.Name);
                string rowType = Inflector.RowTypeName(entityPascal);

                // JNT2006: the row POCO is always a plain (non-partial) class.
                // Inflector's own "Row" suffix fallback only guards against the
                // table's own accessor name; it cannot see a differently-named
                // accessor elsewhere (almost always a db/tables/<Folder> whose
                // name doesn't match this table's own PascalCase form) that
                // happens to already claim this exact name. One partial + one
                // non-partial declaration of the same type is CS0260 at compile
                // time — report it here with the fix, instead of leaving the
                // user to decode the raw compiler error from generated code.
                if (entityNames.Contains(rowType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2006, Location.None,
                        $"Generated row type '{rowType}' for table '{tableSchema.Name}' has the same name as an entity accessor ('db.{rowType}'). " +
                        $"Rename the db/tables/{rowType}/ folder to the table's own PascalCase form ('{entityPascal}') so the two names no longer collide."));
                }

                context.AddSource($"{entityPascal}.Row.g.cs",
                    SourceText.From(CodeEmitter.EmitRowPoco(rowType, tableSchema, schema.Dialect), Encoding.UTF8));
            }
        }

        // Emit entity core files (constructor + _conn field per entity)
        foreach (var entity in entityNames)
        {
            var coreSource = CodeEmitter.EmitEntityCore(entity);
            context.AddSource($"{entity}.Core.g.cs", SourceText.From(coreSource, Encoding.UTF8));
        }

        // Emit JauntyDb class (also when the snapshot has sequences but no
        // emitted entities, so db.Sequences is still generated)
        if (entityNames.Count > 0 || (schema != null && schema.Sequences.Count > 0))
        {
            var sortedEntities = new System.Collections.Generic.List<string>(entityNames);
            sortedEntities.Sort(StringComparer.Ordinal);
            var dbSource = CodeEmitter.EmitJauntyDb(sortedEntities, schema);
            context.AddSource("JauntyDb.g.cs", SourceText.From(dbSource, Encoding.UTF8));
        }
    }
}
