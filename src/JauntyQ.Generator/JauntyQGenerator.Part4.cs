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

        // AUD-R52-01: the two top-level types the generator always emits
        // itself, regardless of schema content (JauntyDb.g.cs via this file's
        // own EmitJauntyDb call below; JauntyQShapeGuard.g.cs via
        // JauntyQGenerator.cs's RegisterPostInitializationOutput). Neither is
        // user-derived, so an entity or row-POCO name equal to either is a
        // reserved-name collision distinct from (and not covered by) the
        // entity-vs-entity/entity-vs-rowtype checks below.
        var reservedGeneratedTypeNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "JauntyDb", "JauntyQShapeGuard"
        };

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

        // JNT2010: DatabaseSchema.Tables is a plain Dictionary<string, TableSchema>
        // (the default, ordinal, case-SENSITIVE comparer), so two distinct raw
        // table names differing only by case -- reachable on any
        // case-sensitive-identifier engine (Postgres with quoted mixed-case
        // names, or MySQL/MariaDB on a case-sensitive-filesystem Linux host,
        // the common lower_case_table_names=0 default) -- can coexist as two
        // separate schema entries. AutoCrud.Synthesize computes each table's
        // entity name via DialectMapper.ToPascalCase(table.Name), which is
        // itself dialect-agnostic and collapses "widgets"/"Widgets" to the
        // same "Widgets" -- so both tables' synthetics claim the identical
        // "Widgets.GetAll" etc. slot, and claimedMethods below silently drops
        // every one of the second table's methods with no diagnostic at all,
        // making that entire table invisible in the generated API. Report it
        // here so the collision is loud instead of a silent missing table.
        if (autoCrud && schema != null)
        {
            var byEntityName = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.Ordinal);
            foreach (var table in schema.Tables.Values)
            {
                string candidateEntityName = DialectMapper.ToPascalCase(table.Name);
                if (!byEntityName.TryGetValue(candidateEntityName, out var names))
                    byEntityName[candidateEntityName] = names = new System.Collections.Generic.List<string>();
                names.Add(table.Name);
            }
            foreach (var pair in byEntityName)
            {
                if (pair.Value.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2010, Location.None,
                        $"Tables {string.Join(", ", pair.Value)} all generate the same entity accessor 'db.{pair.Key}'. " +
                        "Only the first table's auto-CRUD is emitted; the others are silently invisible in the generated API. " +
                        "Rename the tables so their PascalCased names no longer collide, or write their queries by hand."));
                }
            }
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
                    string upsertSource = CodeEmitter.EmitUpsert(synth.EntityName, schema.Tables[synth.TableName], schema.Dialect, schema);
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
                if (!SchemaLookup.TryGetTable(schema, kvp.Key, out var tableSchema))
                    continue;
                var table = tableSchema!;
                var info = kvp.Value;
                string rowType = Inflector.RowTypeName(DialectMapper.ToPascalCase(table.Name));
                string overloadSource = CodeEmitter.EmitPocoOverloads(
                    info.Entity, rowType, table, schema.Dialect,
                    info.Insert, info.Update, info.Delete, info.Upsert, schema);
                context.AddSource($"{info.Entity}.Poco.auto.g.cs", SourceText.From(overloadSource, Encoding.UTF8));
                neededRowTables.Add(table.Name);

                // BulkInsert(IEnumerable<Row>): a dialect-native set-based insert
                // for tables that have a synthetic Insert (and thus a row POCO).
                if (info.Insert && !string.IsNullOrEmpty(schema.Dialect))
                {
                    string bulkSource = CodeEmitter.EmitBulkInsert(info.Entity, rowType, table, schema.Dialect, schema);
                    context.AddSource($"{info.Entity}.BulkInsert.auto.g.cs", SourceText.From(bulkSource, Encoding.UTF8));
                }
            }
        }

        // Emit canonical row POCOs (one per table actually used full-row)
        if (schema != null)
        {
            foreach (var tableName in neededRowTables)
            {
                if (!SchemaLookup.TryGetTable(schema, tableName, out var resolvedTableSchema))
                    continue;
                var tableSchema = resolvedTableSchema!;

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
                    if (!ucol.IsRowVersion && DialectMapper.IsUnmappedDbType(ucol.DbType, ucol.IsNullable, ucol.Precision ?? ucol.MaxLength, schema.Dialect))
                    {
                        // Round 14 audit: SQL Server CLR UDT columns
                        // (hierarchyid, geography, geometry) get a sharper
                        // message than the generic "degrades to object" —
                        // confirmed live this round that reading one through
                        // the fallback's reader.GetValue(i) call THROWS
                        // (System.IO.FileNotFoundException on
                        // Microsoft.SqlServer.Types, which JauntyQ never
                        // references), it doesn't just lose type fidelity.
                        // See DialectMapper.IsKnownSqlServerClrUdtType.
                        bool isKnownSqlServerClrUdt =
                            string.Equals(schema.Dialect, "sqlserver", StringComparison.OrdinalIgnoreCase)
                            && DialectMapper.IsKnownSqlServerClrUdtType(ucol.DbType);

                        string message = isKnownSqlServerClrUdt
                            ? $"Column '{tableSchema.Name}.{ucol.Name}' has db type '{ucol.DbType}', a SQL Server CLR user-defined type with no mapping in DialectMapper. " +
                              $"It degrades to 'object', but reading it at RUNTIME will THROW System.IO.FileNotFoundException for 'Microsoft.SqlServer.Types' " +
                              $"(intentionally not referenced, to keep JauntyQ's core zero-dependency/NativeAOT) — this is not merely a lost-type-fidelity warning, the query will crash. " +
                              $"Cast the column to a mapped type in the SQL text instead (e.g. CAST({ucol.Name} AS varbinary(892)) or CAST({ucol.Name} AS nvarchar(4000))) before selecting it."
                            : $"Column '{tableSchema.Name}.{ucol.Name}' has db type '{ucol.DbType}', which has no mapping in DialectMapper and degrades to 'object'. " +
                              $"Add a case for it or accept the untyped column and cast at the call site.";

                        context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2007, Location.None, message));
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

                // AUD-R52-01: the row POCO can equally collide with one of the
                // two always-emitted, fixed-name generator types (JauntyDb,
                // JauntyQShapeGuard) — verified live: a table whose singularized
                // PascalCase name is exactly "JauntyDb" produces a raw CS0260/
                // CS0542/CS0102/CS0111/CS0229 cascade with zero JauntyQ
                // diagnostic, since neither name is user-derived and so was
                // never in scope for the entity-vs-entity check above.
                if (reservedGeneratedTypeNames.Contains(rowType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2006, Location.None,
                        $"Generated row type '{rowType}' for table '{tableSchema.Name}' has the same name as a JauntyQ-reserved generated type ('{rowType}'). " +
                        $"Rename the table (or its db/tables/ folder) so its PascalCase form no longer collides with a reserved name."));
                }

                context.AddSource($"{entityPascal}.Row.g.cs",
                    SourceText.From(CodeEmitter.EmitRowPoco(rowType, tableSchema, schema.Dialect, schema), Encoding.UTF8));
            }
        }

        // AUD-R52-01: an entity accessor name colliding with one of the two
        // always-emitted, fixed-name generator types (JauntyDb,
        // JauntyQShapeGuard) is a distinct case from the entity-vs-entity/
        // entity-vs-rowtype checks above — neither reserved name is
        // user-derived, so nothing else in this method ever compares against
        // them. Verified live: a table named "jaunty_db" (PascalCase
        // "JauntyDb") makes EmitEntityCore below declare a second, directly
        // conflicting "JauntyDb" type (duplicate _conn field, duplicate
        // constructor, a self-referential "JauntyDb JauntyDb { get; }"
        // property whose name equals its enclosing type) — a raw CS0260/
        // CS0542/CS0102/CS0111/CS0229 cascade with zero JauntyQ diagnostic
        // pointing at the real cause. Reported (not skipped) to match the
        // existing JNT2006 row-POCO check's own report-but-still-emit
        // convention above.
        foreach (var entity in entityNames)
        {
            if (reservedGeneratedTypeNames.Contains(entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2006, Location.None,
                    $"Generated entity accessor '{entity}' (db.{entity}) has the same name as a JauntyQ-reserved generated type ('{entity}'). " +
                    $"Rename the table (or its db/tables/ folder) so its PascalCase form no longer collides with a reserved name."));
            }
        }

        // Emit entity core files (constructor + _conn field per entity)
        foreach (var entity in entityNames)
        {
            var coreSource = CodeEmitter.EmitEntityCore(entity);
            context.AddSource($"{entity}.Core.g.cs", SourceText.From(coreSource, Encoding.UTF8));
        }

        // JNT2009: two distinct sequence names that fold to the same PascalCase
        // accessor method name (e.g. "order_number" and "OrderNumber" both ->
        // "NextOrderNumber") would otherwise silently collapse to a single
        // db.Sequences method with no diagnostic -- the same "colliding
        // PascalCase names must not silently double-emit a C# member" invariant
        // JNT2006/JNT3009 already enforce for row POCOs and result columns.
        // CodeEmitter.EmitSequenceAccessor keeps only the first on collision
        // regardless; this reports it so the drop isn't silent.
        if (schema != null && schema.Sequences.Count > 0)
        {
            var byMethodName = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.Ordinal);
            foreach (var seq in schema.Sequences.Values)
            {
                if (!IdentifierGuard.IsValidIdentifier(seq.Name))
                    continue;
                string method = "Next" + DialectMapper.ToPascalCase(seq.Name);
                if (!byMethodName.TryGetValue(method, out var names))
                    byMethodName[method] = names = new System.Collections.Generic.List<string>();
                names.Add(seq.Name);
            }
            foreach (var pair in byMethodName)
            {
                if (pair.Value.Count > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2009, Location.None,
                        $"Sequences {string.Join(", ", pair.Value)} all generate the same accessor method '{pair.Key}()'. " +
                        "Only the first is emitted; rename the others so their PascalCased names no longer collide."));
                }
            }
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
