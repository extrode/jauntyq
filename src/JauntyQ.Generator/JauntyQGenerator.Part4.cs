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
        //
        // AUD-R50-03 (partial fix, this set): a second, distinct mechanism
        // reaches the same "zero JauntyQ diagnostic" outcome. CodeEmitter's
        // TypeRef collision guard qualifies every *reference* to a table-
        // derived entity/row-POCO name and to a handful of value types
        // (DateTime/Guid/TimeSpan/DateTimeOffset/provider types), but the
        // ADO.NET plumbing types below are emitted as bare literals at every
        // call site and never routed through TypeRef at all -- verified live
        // (table "db_commands" -> entity "DbCommands", still collides
        // because every *reference* to the real System.Data.Common.DbCommand
        // inside that entity's own emitted methods is now shadowed by the
        // entity's own class). Deliberately scoped to the ADO.NET-specific
        // names round 50 named explicitly; broader generic BCL words
        // (Convert/Math/Array/Type/StringComparison/exception type names)
        // are NOT included here -- left as residual, still-deferred scope
        // (AUD-R50-03) since
        // they are both far more generic (higher false-positive-on-a-
        // legitimate-table-name risk to even enumerate correctly) and even
        // less plausible as a real table name than the names below.
        //
        // AUD-R60-01: "System" is reserved for a third, again-distinct reason
        // -- namespace-ROOT shadowing, not type-name shadowing. C# namespace-
        // member lookup resolves the first segment of a dotted name against
        // in-namespace types before falling back to the global namespace, so
        // an entity accessor or row POCO literally named "System" (table
        // "system", or "systems" via singularization) shadows the BCL System
        // namespace root for EVERY dotted "System.Xxx" reference in every
        // file compiled into JauntyQ.Generated -- and the always-emitted
        // JauntyQShapeGuard (schema-independent post-init output, so it can
        // never route through TypeRef) contains four such references
        // (System.Data.Common.DbDataReader / System.InvalidOperationException
        // x2 / System.StringComparison). Verified by real Roslyn compilation:
        // a "system" table produces CS0426/CS0117 in the shape guard plus a
        // CS1503 cascade at every entity's Validate call site, on every
        // dialect, with zero JauntyQ diagnostic -- and, unlike the round-59
        // residual names, there is NO dialect or feature combination under
        // which it compiles (the shape guard is unconditional), so an
        // unconditional JNT2006 Error here has zero false-positive risk.
        // The same reservation also covers the conditionally-emitted dotted
        // System.* references (System.Net.IPAddress for postgres inet/cidr
        // columns in CodeEmitter.Part9.cs and in property/parameter type
        // positions, System.Collections.IEnumerator in Part13's bulk reader
        // adapter, System.Collections.Generic.IReadOnlyList<T> for @each
        // parameters): each is a fully-qualified TYPE-position reference, so
        // the only schema-reachable way to break any of them is an emitted
        // top-level type named exactly "System" -- exactly what this entry
        // now reports.
        var reservedGeneratedTypeNames = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "JauntyDb", "JauntyQShapeGuard",
            "System",
            "DbCommand", "DbParameter", "DbDataReader", "DbConnection", "DbTransaction",
            "CancellationToken", "StringBuilder", "DBNull",
            "ConnectionState", "CommandType", "CommandBehavior", "ParameterDirection"
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

            // AUD-R75-03 (JNT2014): the column-level sibling of the JNT2010
            // check above, and for the same reason -- a silently missing
            // table.
            //
            // Three sites already refuse a table whose columns fold to one C#
            // name, each correctly declining to emit broken code and each
            // assuming another one reports it: AutoCrud.cs:98 skips
            // synthesizing CRUD, Part5.cs:59's ResolveCanonicalRowType
            // refuses to route any full-row query to it, and the JNT2011 arm
            // below (this file, in the row-POCO loop) is meant to be the one
            // that explains it. But that loop iterates neededRowTables, and
            // Part5.cs:59 is precisely what keeps such a table OUT of
            // neededRowTables -- so the JNT2011 row-POCO arm cannot fire for
            // the case it was written for, and the table vanished from the
            // generated API with no diagnostic at all.
            //
            // Reported here, where every table is visited regardless of
            // whether any query reached it. WARNING, not Error, deliberately:
            // round 64 chose this skip to be silent rather than breaking, and
            // an Error here would fail the build of any existing consumer
            // whose schema holds such a pair even in a table they never query.
            // The consequence is now visible without that cost; JNT2010's
            // Error severity for the table-name case is left as-is rather
            // than harmonized, which is a known inconsistency recorded with
            // this finding.
            foreach (var table in schema.Tables.Values)
            {
                string? dupCol = FindDuplicateColumnPropertyName(
                    table.Columns.Values, out string? firstRawCol, out string? secondRawCol);
                if (dupCol != null)
                {
                    // AUD-R77-02: same absence claim, same correction as the
                    // JNT2015 loop below. A projection query that does not
                    // select both colliding columns still emits, and with it
                    // the entity this sentence calls absent.
                    string entity = DialectMapper.ToPascalCase(table.Name);
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2014, Location.None,
                        $"Table '{table.Name}' has two columns, '{firstRawCol}' and '{secondRawCol}', that both map to the generated property '{dupCol}'. " +
                        (entityNames.Contains(entity)
                            ? $"No row type or auto-CRUD can be emitted for it, so 'db.{entity}' carries only the queries you wrote by hand. "
                            : $"No row type or auto-CRUD can be emitted for it, so 'db.{entity}' is absent from the generated API. ") +
                        "Rename one of the columns, or alias it distinctly in a hand-written query."));
                }
            }

            // AUD-R64-01 (T8 residual, JNT2015): the sibling of the loop above
            // for the other silent skip. AutoCrud.Synthesize refuses any table
            // whose name, or any of whose column names, cannot be written
            // unquoted -- a JauntyQ keyword, a word the target engine reserves,
            // an illegal character, or (PostgreSQL) a mixed-case name an
            // unquoted reference would fold away. Every one of those was a bare
            // `continue`, so the table left no trace anywhere.
            //
            // The reason string comes from AutoCrud itself rather than being
            // re-derived here: DescribeUnusableTable and the gate are the same
            // code, so this cannot report a skip that did not happen or miss
            // one that did.
            foreach (var table in schema.Tables.Values)
            {
                string? why = AutoCrud.DescribeUnusableTable(table, schema.Dialect);
                if (why != null)
                {
                    // AUD-R77-02: the absence claim holds only when nothing
                    // else emitted the entity. A hand-written query file
                    // creates 'db.X' whether or not auto-CRUD would have, and
                    // telling a consumer a member they can see in IntelliSense
                    // does not exist is worse than saying nothing about it.
                    string entity = DialectMapper.ToPascalCase(table.Name);
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2015, Location.None,
                        $"No auto-CRUD is generated for table '{table.Name}' because {why}. " +
                        (entityNames.Contains(entity)
                            ? $"'db.{entity}' carries only the queries you wrote by hand. " +
                              "Rename the table or column to get auto-CRUD as well."
                            : $"'db.{entity}' is absent from the generated API. " +
                              "Rename the table or column, or write its queries by hand with the identifier quoted.")));
                }
            }

            // JNT2019/JNT2020: the Upsert-shaped siblings of the JNT2015 loop
            // above. UpsertKeyResolver.Resolve refuses a key enforced only by
            // a prefix UNIQUE (MySQL SUB_PART) and skips one whose only
            // candidates are expression UNIQUEs, and either refusal is
            // otherwise indistinguishable from "no key at all" -- a silent
            // skip. The refusing index comes from the resolver itself, so this
            // cannot report a refusal that did not happen or miss one that
            // did; the resolver sets at most one of the two out parameters.
            foreach (var table in schema.Tables.Values)
            {
                // AUD-R77-01: the same boundary JNT2018 respects one loop
                // below. A hand-written Upsert.sql claims the slot, so an
                // Upsert IS generated for this table and "No Upsert is
                // generated" describes nothing that happened -- while the
                // remedy the message offers, "write the upsert by hand", is
                // what the consumer already did. JNT2015/JNT2014 above need no
                // such guard: they report auto-CRUD being skipped for the whole
                // table, which one hand-written query does not undo.
                if (claimedMethods.Contains($"{DialectMapper.ToPascalCase(table.Name)}.Upsert"))
                    continue;

                UpsertKeyResolver.Resolve(table, out var prefixOnlyKey, out var expressionOnlyKey);
                if (prefixOnlyKey != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2019, Location.None,
                        $"No Upsert is generated for table '{table.Name}': the only constraint that could serve as its " +
                        $"upsert key is '{prefixOnlyKey.Name}' ({string.Join(", ", prefixOnlyKey.Columns)}), a UNIQUE over a " +
                        "column PREFIX, which the engine matches on rows sharing only the prefix -- not the full value the " +
                        "method's signature would imply. Add a full-column UNIQUE constraint (or a bindable primary key), " +
                        "or write the upsert by hand."));
                }
                else if (expressionOnlyKey != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2020, Location.None,
                        $"No Upsert is generated for table '{table.Name}': the only constraint that could serve as its " +
                        $"upsert key is '{expressionOnlyKey.Name}', a UNIQUE with an EXPRESSION key part -- the schema " +
                        "snapshot carries only its real columns, and no generated method can bind a parameter to the " +
                        "expression's value the engine actually matches on. Add a full-column UNIQUE constraint (or a " +
                        "bindable primary key), or write the upsert by hand."));
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
                    // AUD-R4-16 (JNT2018): reported here rather than in a table
                    // loop of its own, because here is the one place that knows
                    // an Upsert is actually being emitted for this table -- a
                    // hand-written .sql file claiming the method above wins, and
                    // then no key-targeted SQL exists to warn about. Same
                    // predicate the emitter branches on, so the warning cannot
                    // describe a form that was not emitted.
                    var upsertTable = schema.Tables[synth.TableName];
                    var upsertKey = UpsertKeyResolver.Resolve(upsertTable);
                    if (upsertKey != null
                        && string.Equals(schema.Dialect, "mysql", StringComparison.OrdinalIgnoreCase)
                        && UpsertKeyResolver.HasCompetingUniqueConstraint(upsertTable, upsertKey))
                    {
                        var keyNames = new List<string>(upsertKey.Count);
                        foreach (var c in upsertKey)
                            keyNames.Add(c.Name);
                        context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2018, Location.None,
                            $"'{synth.EntityName}.Upsert' targets table '{upsertTable.Name}' on ({string.Join(", ", keyNames)}), " +
                            "but the table carries another UNIQUE constraint. MySQL's ON DUPLICATE KEY UPDATE names no conflict " +
                            "target and would match whichever UNIQUE the insert violates, so JauntyQ emits a key-targeted " +
                            "UPDATE-then-INSERT instead, matching what postgres, sqlite and sqlserver already do. Those two " +
                            "statements are not atomic, and wrapping them in a transaction does not make them so. Concurrent " +
                            "upserts of the same new key can fail with duplicate-key error 1062 (both pass the existence " +
                            "check), deadlock with error 1213 (one session's insert-intention lock meets the other's gap " +
                            "lock from the UPDATE), or silently write nothing (another session commits the row between this " +
                            "call's UPDATE and its INSERT, so neither statement applies the caller's values). Retry the call " +
                            "on 1062 and 1213 — it is idempotent — or drop the competing UNIQUE constraint so the atomic " +
                            "ON DUPLICATE KEY UPDATE form is emitted instead. A 0 return is not by itself evidence of the " +
                            "lost write: under UseAffectedRows=true an unchanged re-run also returns 0."));
                    }

                    // Dialect-native upsert bypasses the minimal SQL parser;
                    // correctness comes from the schema snapshot itself.
                    string upsertSource = CodeEmitter.EmitUpsert(synth.EntityName, upsertTable, schema.Dialect, schema);
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

                // JNT2011: two distinct, individually-legal column names that
                // fold to the same PascalCased property name (e.g.
                // "order_number"/"OrderNumber", or a case-only pair) would
                // make CodeEmitter.EmitRowPoco emit two identically-named
                // properties in the same class (CS0102), plus a duplicate
                // member-initializer entry in Read() -- the same "sibling
                // switch/guard divergence" shape JNT3009 already guards for a
                // query's own SELECT projection, and JNT2009/JNT2010 guard
                // for sequence/entity accessor names, but nothing previously
                // covered the table's own full column set feeding the
                // canonical row POCO. Skip the table and report, matching the
                // JNT2004 (C2) precedent immediately above.
                string? dupRowCol = FindDuplicateColumnPropertyName(tableSchema.Columns.Values);
                if (dupRowCol != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2011, Location.None,
                        $"Table '{tableSchema.Name}' has two columns that map to the same generated property '{dupRowCol}'. " +
                        $"The row type cannot declare '{dupRowCol}' twice; rename one column or exclude it from full-row queries."));
                    continue;
                }

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
                    // Spec 013: a column whose EnumName resolves against the
                    // snapshot's captured enums maps to its generated C# enum
                    // in MapColumnToCSharp, so its raw dbType (the Postgres
                    // type name / MySQL's bare "enum") being absent from the
                    // dbType switch is expected, not an unmapped-type gap.
                    if (!ucol.IsRowVersion
                        && DialectMapper.ResolveEnumTypeName(ucol, schema) == null
                        && DialectMapper.IsUnmappedDbType(ucol.DbType, ucol.IsNullable, ucol.Precision ?? ucol.MaxLength, schema.Dialect))
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

        // Spec 013, JNT2016/JNT2017: validate captured enums. Deliberately
        // outside the autoCrud gate above -- an enum reaches the generated API
        // through any query that selects an enum column, not just through
        // auto-CRUD, so gating this on autoCrud would let the collision
        // through on exactly the hand-written-queries path.
        if (schema != null && schema.Enums.Count > 0)
        {
            ReportEnumDiagnostics(context, schema);

            // Spec 013 T10: the enums, their {EnumName}Values companions and
            // the shared JauntyQEnumValueException, in one file. Null when no
            // column references a captured type, so a snapshot that merely has
            // enums declared adds no source.
            string? enumSource = CodeEmitter.EmitEnums(schema);
            if (enumSource != null)
                context.AddSource("Enums.g.cs", SourceText.From(enumSource, Encoding.UTF8));
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

    /// <summary>
    /// The name of the exception type emitted once per assembly and thrown by
    /// every generated <c>{EnumName}Values.Parse</c> when the database hands
    /// back a value the snapshot never captured (spec 013).
    /// </summary>
    internal const string EnumValueExceptionTypeName = "JauntyQEnumValueException";

    /// <summary>
    /// Spec 013 JNT2016/JNT2017. Runs over the enums that are actually
    /// emitted — a captured type no column references contributes no C# name
    /// and therefore cannot collide with anything.
    /// </summary>
    private static void ReportEnumDiagnostics(SourceProductionContext context, DatabaseSchema schema)
    {
        // Which captured types are actually emitted. Taken from the emitter
        // itself, not re-derived: a diagnostic that disagrees with what gets
        // emitted is worse than no diagnostic at all.
        var referenced = new System.Collections.Generic.HashSet<string>(
            CodeEmitter.ReferencedEnumNames(schema), StringComparer.Ordinal);

        // Every C# name already claimed in the generated namespace, mapped to
        // a description of what claimed it, so the message can say what the
        // enum is colliding with rather than just that it collides.
        var claimed = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in schema.Tables.Values)
        {
            string entity = DialectMapper.ToPascalCase(table.Name);
            claimed[entity] = $"the entity accessor for table '{table.Name}'";
            claimed[Inflector.RowTypeName(entity)] = $"the row type for table '{table.Name}'";
        }

        // Iterate the snapshot's own order, not the HashSet's: a source
        // generator must report the same diagnostics in the same order on
        // every run or incremental builds churn.
        foreach (var pair in schema.Enums)
        {
            if (!referenced.Contains(pair.Key))
                continue;
            string enumName = pair.Key;
            var enumSchema = pair.Value;

            // JNT2016: two members folding to one C# identifier. Checked
            // before the name collisions below because a duplicate member is a
            // property of the type in isolation and does not depend on what
            // else the snapshot holds.
            var seenMembers = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in enumSchema.Members)
            {
                string folded = string.IsNullOrEmpty(member.CSharpName)
                    ? EnumMemberNaming.Fold(member.Value)
                    : member.CSharpName;
                if (seenMembers.TryGetValue(folded, out string? firstValue))
                {
                    context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2016, Location.None,
                        $"Enum '{enumName}' has two members, '{firstValue}' and '{member.Value}', that both map to the generated member '{folded}'. " +
                        $"The generated enum cannot declare '{folded}' twice; rename one of the database values."));
                    break;
                }
                seenMembers[folded] = member.Value;
            }

            // JNT2017: the enum contributes two names of its own, plus the
            // shared exception type. Report the first collision per enum --
            // the fix is the same rename either way.
            string typeName = DialectMapper.EnumTypeName(enumName);
            string companion = typeName + "Values";
            string? conflict = null;
            if (claimed.TryGetValue(typeName, out string? owner))
                conflict = $"the generated enum type '{typeName}' collides with {owner}";
            else if (claimed.TryGetValue(companion, out string? companionOwner))
                conflict = $"its generated companion class '{companion}' collides with {companionOwner}";
            else if (string.Equals(typeName, EnumValueExceptionTypeName, StringComparison.Ordinal) ||
                     string.Equals(companion, EnumValueExceptionTypeName, StringComparison.Ordinal))
                conflict = $"it collides with the generated exception type '{EnumValueExceptionTypeName}'";

            if (conflict != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2017, Location.None,
                    $"Enum '{enumName}' cannot be generated: {conflict}. " +
                    "Rename the database type, the table, or the column so the generated names no longer collide."));
                continue;
            }

            claimed[typeName] = $"the generated enum type for '{enumName}'";
            claimed[companion] = $"the generated companion class for enum '{enumName}'";
        }

        // JauntyQEnumValueException itself is emitted once whenever any enum
        // is, so a table claiming that name breaks the assembly no matter
        // which enum is at fault. Reported separately, keyed off the table.
        if (referenced.Count > 0 && claimed.TryGetValue(EnumValueExceptionTypeName, out string? exOwner))
        {
            context.ReportDiagnostic(Diagnostic.Create(JauntyDiagnostics.JNT2017, Location.None,
                $"The generated exception type '{EnumValueExceptionTypeName}' collides with {exOwner}. " +
                "Rename the table so the generated names no longer collide."));
        }
    }

    /// <summary>
    /// True (returning the folded name) when two columns in <paramref
    /// name="columns"/> map to the same emitted <c>DialectMapper.ToPascalCase</c>
    /// property name, else null. Shared by the canonical row-POCO check
    /// (JNT2011, this file) and the stored-procedure Result DTO check
    /// (JNT2011, JauntyQGenerator.Part2.cs) — both emit one C# property per
    /// column from a raw schema-derived column list, the same shape
    /// <c>FindDuplicateResultColumn</c> already guards for a query's own
    /// SELECT projection.
    /// </summary>
    private static string? FindDuplicateColumnPropertyName(System.Collections.Generic.IEnumerable<ColumnSchema> columns)
        => FindDuplicateColumnPropertyName(columns, out _, out _);

    /// <summary>
    /// As above, but also yields the two RAW column names that collided.
    /// JNT2014 reports the pair: naming only the folded property leaves the
    /// reader hunting through a wide table for which two columns produced it,
    /// which is the same reason JNT2010 lists its colliding table names.
    /// </summary>
    private static string? FindDuplicateColumnPropertyName(
        System.Collections.Generic.IEnumerable<ColumnSchema> columns,
        out string? firstRawName,
        out string? secondRawName)
    {
        var seen = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var col in columns)
        {
            string emitted = DialectMapper.ToPascalCase(col.Name);
            if (seen.TryGetValue(emitted, out string? firstRaw))
            {
                firstRawName = firstRaw;
                secondRawName = col.Name;
                return emitted;
            }
            seen[emitted] = col.Name;
        }
        firstRawName = null;
        secondRawName = null;
        return null;
    }
}
