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

        // JNT2004 (H1, keyword case): a bare identifier can still be a reserved
        // C# keyword (a file literally named 'class.sql' or 'int.sql'). Unlike
        // parameters and column aliases — which flow through IdentifierGuard.Escape
        // — the entity/method names are emitted verbatim as a class and method,
        // so a keyword yields uncompilable code ('public class class'). Reject it
        // with actionable guidance rather than emit broken source.
        if (IdentifierGuard.IsReservedKeyword(entityName) || IdentifierGuard.IsReservedKeyword(methodName))
        {
            string offender = IdentifierGuard.IsReservedKeyword(methodName) ? methodName : entityName;
            var keywordNameDiag = ImmutableArray.Create(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                $"SQL file path yields the C# reserved keyword '{offender}' as an entity/method name, which cannot be emitted as-is. Rename the file/folder (e.g. '{offender}Query')."));
            return FileResult.WithDiagnostics(entityName, methodName, keywordNameDiag);
        }

        // From here on the file claims its entity.method slot: a user file
        // always overrides the auto-CRUD synthetic of the same name, even
        // when it currently fails validation.
        if (schemaState.ParseFailed)
            return FileResult.None(entityName, methodName, claims: true); // JNT6001 comes from the aggregate step

        var schema = schemaState.Schema;

        // Parse directives (before tokenization, since tokenizer strips comments)
        var (directives, cleanedSql) = Directives.DirectiveParser.Parse(sqlText!);

        // JNT3008 (warning, non-fatal): directive-lookalike comments that
        // parsed as nothing — a bare value-taking directive or a one-edit
        // typo of a known name. The line stayed a plain comment; tell the
        // author their intent was dropped instead of silently ignoring it.
        var directiveWarnings = ImmutableArray<DiagnosticInfo>.Empty;
        if (directives.SuspiciousDirectives is { Count: > 0 } suspicious)
        {
            var warnBuilder = ImmutableArray.CreateBuilder<DiagnosticInfo>(suspicious.Count);
            foreach (var message in suspicious)
                warnBuilder.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3008, message));
            directiveWarnings = warnBuilder.ToImmutable();
        }

        // -- @call binds to an existing stored procedure. The file has no SQL
        // body of its own; the callable contract (params + result columns)
        // comes from the schema snapshot, so this short-circuits the SQL
        // parse/validate pipeline entirely.
        if (directives.CallProcName != null)
        {
            // AUD-R8 (JNT2005 sibling-sweep): every other directive
            // presupposes the file has its own parsed query (a result shape,
            // parameters bound from the SQL text, a statement @identity can
            // check, etc.) -- none of that exists for @call. Before this
            // check, e.g. "-- @call X" + "-- @proc Y" in the same file
            // emitted ZERO diagnostics and silently discarded @proc (and
            // every other directive here behaves the same way, since the
            // @call branch below never consults them). Reject the
            // combination explicitly instead of silently picking @call.
            string? conflictingDirective =
                directives.IsProc ? "@proc" :
                directives.IsFirst ? "@first" :
                directives.ReturnsIdentity ? "@identity" :
                directives.IsStream ? "@stream" :
                directives.EachParams != null ? "@each" :
                directives.TypeDirectives != null ? "@type" :
                directives.ExplicitParams != null ? "@params" :
                (directives.ResultTypeName != null || directives.ResultIsVoid || directives.InlineColumns != null) ? "@result" :
                null;

            if (conflictingDirective != null)
            {
                var conflictDiagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
                conflictDiagnostics.AddRange(directiveWarnings);
                conflictDiagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3003,
                    $"-- @call cannot be combined with -- {conflictingDirective}: @call has no SQL body of " +
                    "its own -- its parameter and result shape come entirely from the schema snapshot's " +
                    $"procedure signature, so -- {conflictingDirective} would be silently ignored. Remove one directive."));
                return FileResult.WithDiagnostics(entityName, methodName, conflictDiagnostics.ToImmutable());
            }

            var callDiagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            callDiagnostics.AddRange(directiveWarnings);

            if (schema == null)
                return FileResult.WithDiagnostics(entityName, methodName, callDiagnostics.ToImmutable());

            if (!TryResolveProcedure(schema, directives.CallProcName, out var procedure))
            {
                callDiagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2005,
                    $"Stored procedure '{directives.CallProcName}' was not found in the schema snapshot. Re-run 'jaunty schema pull' after the procedure exists, or check the name."));
                return FileResult.WithDiagnostics(entityName, methodName, callDiagnostics.ToImmutable());
            }

            // Reject illegal C# identifiers among the emitted result columns
            // (JNT2004), consistent with the SELECT projection guard.
            foreach (var rc in procedure!.Results)
            {
                if (!IdentifierGuard.IsValidIdentifier(DialectMapper.ToPascalCase(rc.Name)))
                {
                    callDiagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                        $"Stored procedure '{procedure.Name}' returns a column '{rc.Name}' that maps to an illegal C# identifier."));
                    return FileResult.WithDiagnostics(entityName, methodName, callDiagnostics.ToImmutable());
                }
            }

            // JNT2011: two distinct result columns folding to the same
            // PascalCased property name would emit a duplicate member into
            // EmitProcCall's Result DTO (CodeEmitter.Part10.cs) -- the exact
            // sibling of the canonical row-POCO check in
            // JauntyQGenerator.Part4.cs, here for a stored procedure's own
            // result-set columns instead of a table's columns.
            string? dupProcCol = FindDuplicateColumnPropertyName(procedure.Results);
            if (dupProcCol != null)
            {
                callDiagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2011,
                    $"Stored procedure '{procedure.Name}' has two result columns that map to the same generated property '{dupProcCol}'. " +
                    $"The Result DTO cannot declare '{dupProcCol}' twice; alias one column distinctly in the procedure's own SELECT."));
                return FileResult.WithDiagnostics(entityName, methodName, callDiagnostics.ToImmutable());
            }

            // JNT2013: two distinct parameters folding to the same C# formal
            // would emit a duplicate parameter into every EmitProcCall
            // overload (CodeEmitter.Part11.cs) -- the parameter-list sibling
            // of the result-column check immediately above. No rename by the
            // generator can resolve this: both names are real and legitimate,
            // so it has to be rejected and aliased in the procedure itself.
            string? dupProcParam = FindDuplicateParameterName(procedure.Params, out string? firstRaw, out string? secondRaw);
            if (dupProcParam != null)
            {
                callDiagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2013,
                    $"Stored procedure '{procedure.Name}' has two parameters, '{firstRaw}' and '{secondRaw}', that map to the same generated parameter '{dupProcParam}'. " +
                    $"The generated method cannot declare '{dupProcParam}' twice; rename one of them in the procedure definition."));
                return FileResult.WithDiagnostics(entityName, methodName, callDiagnostics.ToImmutable());
            }

            string callSource = CodeEmitter.EmitProcCall(entityName, methodName, procedure, schema.Dialect, schema);
            return new FileResult(
                $"{entityName}.{methodName}.g.cs",
                callSource,
                callDiagnostics.ToImmutable(),
                new FileSummary(entityName, methodName, claims: true, emitted: true, canonicalTable: null),
                fingerprint: $"@call:{procedure.Name}",
                path: sqlFile.Path);
        }

        // Tokenize (use cleaned SQL with directive lines removed)
        var tokens = SqlTokenizer.Tokenize(cleanedSql);

        // JNT1003: input exceeded the tokenizer's size cap and was refused
        // before any tokenizing happened. Bail before parsing.
        int tooLargeIndex = tokens.FindIndex(t => t.Type == JauntyQ.SqlParser.Tokens.TokenType.TooLarge);
        if (tooLargeIndex >= 0)
        {
            var tooLargeDiag = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            tooLargeDiag.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT1003,
                $"SQL text is {tokens[tooLargeIndex].Value} characters, exceeding the {SqlTokenizer.MaxInputLength}-character limit; refusing to tokenize."));
            return FileResult.WithDiagnostics(entityName, methodName, tooLargeDiag.ToImmutable());
        }

        // JNT1005: parenthesis nesting exceeded the tokenizer's depth cap and
        // was refused. The recursive-descent parser is O(n²) in depth, so a
        // pathologically deep file would otherwise hang the build. Bail before
        // parsing.
        int tooDeepIndex = tokens.FindIndex(t => t.Type == JauntyQ.SqlParser.Tokens.TokenType.TooDeep);
        if (tooDeepIndex >= 0)
        {
            var tooDeepDiag = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            tooDeepDiag.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT1005,
                $"SQL nests parentheses more than {SqlTokenizer.MaxNestingDepth} levels deep; refusing to parse. Simplify the query — nesting this deep is almost always a generated-SQL or copy-paste error."));
            return FileResult.WithDiagnostics(entityName, methodName, tooDeepDiag.ToImmutable());
        }

        // JNT1002: an unterminated block comment, quoted identifier
        // ([Name/"Name/`Name), or string literal ran to end-of-input. Bail
        // before parsing rather than let the parser work off a
        // truncated/corrupted token stream.
        int unterminatedIndex = tokens.FindIndex(t => t.Type == JauntyQ.SqlParser.Tokens.TokenType.Unterminated);
        if (unterminatedIndex >= 0)
        {
            var unterminatedDiag = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            unterminatedDiag.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT1002,
                $"Unterminated {tokens[unterminatedIndex].Value}: reached end of file before finding its closing delimiter."));
            return FileResult.WithDiagnostics(entityName, methodName, unterminatedDiag.ToImmutable());
        }

        // JNT1004: a character the tokenizer does not recognize. Bail before
        // parsing — skipping the character (the old behavior) silently corrupts
        // the token stream and the parsed model no longer describes the query
        // that will actually run.
        int unknownIndex = tokens.FindIndex(t => t.Type == JauntyQ.SqlParser.Tokens.TokenType.Unknown);
        if (unknownIndex >= 0)
        {
            var unknownDiag = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            unknownDiag.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT1004,
                $"Unsupported character '{tokens[unknownIndex].Value}' in SQL; JauntyQ cannot safely parse this file. Remove the character or rewrite the construct."));
            return FileResult.WithDiagnostics(entityName, methodName, unknownDiag.ToImmutable());
        }

        // Parse
        var queryModel = SqlParser.SqlParser.Parse(tokens, methodName);

        // Validate
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        diagnostics.AddRange(directiveWarnings);
        var errors = QueryValidator.Validate(queryModel, schema);

        // Spec 015: -- @allow-unindexed <reason> accepts this query's unindexed
        // filters deliberately. Applied HERE rather than inside QueryValidator
        // because the validator takes no directives and threading them through
        // every caller would spread a narrow policy across a wide surface --
        // the suppression is a reporting decision, not an analysis one.
        //
        // Only JNT8004. Every other diagnostic this query raises still fires,
        // which is what keeps the directive an accepted-scan marker rather than
        // a general silencer.
        bool allowUnindexed = directives.AllowUnindexedReason != null;
        bool suppressedAnything = false;

        bool hasErrors = false;
        foreach (var error in errors)
        {
            if (allowUnindexed && error.Code == "JNT8004")
            {
                suppressedAnything = true;
                continue;
            }

            diagnostics.Add(DiagnosticInfo.ForValidation(error));
            if (error.Severity == ValidationSeverity.Error)
                hasErrors = true;
        }

        // JNT8012: the directive is present but nothing needed suppressing. An
        // exemption that outlives the condition that justified it is how an
        // escape hatch quietly becomes the default, so a dead one is reported
        // rather than tolerated.
        //
        // "Suppressed nothing" is only evidence of a dead directive when the
        // JNT8004 analysis actually reached a verdict. Three cases where it
        // does not, and where the old placement -- before the gate below --
        // reported the directive as dead anyway:
        //
        //   * schema == null: no snapshot, so no index analysis ran at all.
        //   * hasErrors: the query failed earlier validation, so its filter
        //     columns were never resolved against any index.
        //   * the snapshot declares no index metadata anywhere, which makes
        //     QueryValidator's index checks return early (Part2.cs:88) --
        //     a schema that simply never recorded indexes cannot show that
        //     this query's filters are covered.
        //
        // In all three the consumer was told "the index it was waiting for
        // probably exists now: remove the directive" on the strength of an
        // analysis that never happened. Deleting the directive on that advice
        // would then produce the JNT8004 it was suppressing all along.
        if (hasErrors || schema == null)
            return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());

        if (allowUnindexed && !suppressedAnything && SchemaDeclaresAnyIndex(schema))
        {
            diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT8012,
                "-- @allow-unindexed is declared but no filter column on this query is unindexed, so it " +
                "suppresses nothing. The index it was waiting for probably exists now: remove the directive. " +
                $"(Stated reason: {directives.AllowUnindexedReason})"));
        }

        // -- @identity preconditions (JNT7001). Synthetic auto-CRUD SQL
        // only carries the directive when resolvable; this gate catches
        // user files.
        if (directives.ReturnsIdentity)
        {
            string? problem = null;
            if (queryModel.HasReturning)
                problem = "-- @identity cannot be combined with a user-written RETURNING clause; use one or the other";
            else if (directives.IsProc)
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

        // -- @first preconditions (JNT3003). @first reduces a result set to its
        // first row, so it needs a result set: a SELECT, or a CRUD statement
        // carrying RETURNING (which routes through Emit and does have rows).
        // On a plain INSERT/UPDATE/DELETE it was SILENTLY dropped -- the same
        // silent-ignore the @stream and @each gates above and below exist to
        // prevent, left ungated because nothing observable goes wrong: a plain
        // INSERT has no result shape for @first to change. Nothing observable
        // going wrong is exactly why the author never finds out, which is the
        // JNT2015/JNT2019/JNT2020 policy applied here -- a directive is
        // applied or it is reported.
        if (directives.IsFirst
            && queryModel.StatementType != StatementType.Select
            && !queryModel.HasReturning)
        {
            diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3003,
                "-- @first is only valid on a SELECT or on a statement with a RETURNING clause: " +
                $"a plain {queryModel.StatementType.ToString().ToUpperInvariant()} produces no rows for it to " +
                "reduce, so the directive would be silently ignored. Remove it, or add RETURNING."));
            return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
        }

        // -- @each preconditions (JNT3003). Runtime IN-list expansion is
        // implemented for SELECT queries only; left ungated on a CRUD
        // statement, the directive would be SILENTLY ignored (the parameter
        // stays scalar and the SQL is never expanded) — fail the file with a
        // clear message instead.
        if (directives.EachParams is { Count: > 0 })
        {
            string? eachProblem = null;
            if (queryModel.StatementType != StatementType.Select)
                eachProblem = "-- @each is only supported on SELECT queries; runtime IN-list expansion is not implemented for INSERT/UPDATE/DELETE";
            else if (directives.IsProc)
                eachProblem = "-- @each cannot be combined with -- @proc (a stored procedure call has a fixed parameter list; IN-list expansion rewrites the SQL text)";
            else
            {
                // A name matching no parameter would otherwise be a silent
                // no-op: the method takes a scalar instead of a list and the
                // SQL is never expanded. Same never-silently-ignored rule as
                // the gates above.
                foreach (var eachName in directives.EachParams)
                {
                    if (!queryModel.Parameters.Exists(p => string.Equals(p.Name, eachName, StringComparison.OrdinalIgnoreCase)))
                    {
                        eachProblem = $"-- @each names unknown parameter '{eachName}': the query has no @{eachName} parameter. Check the spelling; left as-is the directive would be silently ignored.";
                        break;
                    }
                }
            }

            if (eachProblem != null)
            {
                diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT3003, eachProblem));
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }
        }

        // JNT7002: -- @proc emits a "CREATE OR ALTER PROCEDURE ... AS BEGIN
        // ... END" DDL script (EmitProcScript) with T-SQL-only parameter
        // types from CSharpToSqlTypeMapper (nvarchar(MAX), uniqueidentifier,
        // datetime2, varbinary(MAX), bit). Postgres and MySQL have no
        // "CREATE OR ALTER" syntax and no "uniqueidentifier" type, and
        // SQLite has no stored procedures at all -- without this gate, a
        // non-SQL-Server project's -- @proc file silently generated a
        // non-functional SQL string constant with zero diagnostic anywhere
        // in the pipeline.
        if (directives.IsProc && !string.Equals(schema.Dialect, "sqlserver", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT7002,
                $"-- @proc generates SQL Server-only T-SQL (CREATE OR ALTER PROCEDURE, T-SQL parameter types) and is not supported under the '{schema.Dialect}' dialect."));
            return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
        }

        // JNT2004: an explicit -- @proc <name> becomes the CommandText string
        // literal emitted for the StoredProcedure call. Reject anything that is
        // not a bare identifier so a hostile name cannot break out of the C#
        // literal and inject build-time code. A synthesized name (entity_method)
        // is already identifier-validated upstream, so only the explicit form
        // needs guarding here.
        if (directives.IsProc
            && directives.ProcName != null
            && !IdentifierGuard.IsValidIdentifier(directives.ProcName))
        {
            diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                $"-- @proc name '{directives.ProcName}' is not a valid C# identifier. Use letters, digits, and underscores, not starting with a digit."));
            return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
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

            if (queryModel.HasReturning)
            {
                // RETURNING makes a CRUD statement row-returning: build a
                // projection from the RETURNING list (plain columns resolve
                // against the target table; expressions follow Feature A) and
                // emit reader code instead of a rows-affected int.
                var returningProjection = ProjectionBuilder.Build(queryModel, queryModel.Returning, schema, directives);

                var retExprDiag = ValidateExpressionTypes(queryModel.Returning, returningProjection, directives);
                if (retExprDiag.Count > 0)
                {
                    foreach (var d in retExprDiag)
                        diagnostics.Add(d);
                    return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
                }

                foreach (var pcol in returningProjection.Columns)
                {
                    if (!IdentifierGuard.IsValidIdentifier(pcol.Name))
                    {
                        diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT2004,
                            $"RETURNING column or alias maps to an illegal C# identifier '{pcol.Name}'. Use a valid identifier."));
                        return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
                    }
                }

                // JNT3009: duplicate emitted property name in the RETURNING row
                // type (same rule as the SELECT projection below).
                var dupReturningCol = FindDuplicateResultColumn(returningProjection);
                if (dupReturningCol != null)
                {
                    diagnostics.Add(dupReturningCol);
                    return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
                }

                source = CodeEmitter.EmitCrudReturning(queryModel, returningProjection, cleanedSql, entityName, schema, directives);
            }
            else
            {
                source = CodeEmitter.EmitCrud(queryModel, cleanedSql, entityName, schema, directives);
            }
        }
        else
        {
            // SELECT — build projection and emit reader code
            var projection = ProjectionBuilder.Build(queryModel, schema, directives);

            // JNT3005/JNT3006: expression projection type resolution.
            var exprDiag = ValidateExpressionTypes(queryModel.Columns, projection, directives);
            if (exprDiag.Count > 0)
            {
                foreach (var d in exprDiag)
                    diagnostics.Add(d);
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
            }

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

            // JNT3009: two SELECT items that map to the same generated property
            // name (e.g. `id as X, other as X`, or `x`/`X`, or `my_col`/`myCol`
            // after PascalCasing) would emit a row type with a duplicate member
            // — a confusing CS0102 inside generated code. Catch it here with a
            // clear diagnostic naming the property.
            var dupResultCol = FindDuplicateResultColumn(projection);
            if (dupResultCol != null)
            {
                diagnostics.Add(dupResultCol);
                return FileResult.WithDiagnostics(entityName, methodName, diagnostics.ToImmutable());
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

        // Build-time RISKY impact (JNT9004): this file passed validation against
        // the effective (post-migration) schema, so it is SAFE or RISKY — a
        // breaking migration already failed above via the JNT2xxx validation
        // errors. Flag the RISKY tier as a non-fatal warning without changing
        // what fails the build.
        if (schemaState.MigrationDelta != null)
        {
            var impactInput = new QueryImpactInput(sqlFile.Path, $"{entityName}.{methodName}",
                ReferencedObjects.Resolve(queryModel));
            var impact = ImpactClassifier.ClassifySingle(schemaState.MigrationDelta, impactInput);
            if (impact.Classification == Classification.Risky)
            {
                var reasonTexts = new System.Collections.Generic.List<string>(impact.Reasons.Count);
                foreach (var r in impact.Reasons)
                    reasonTexts.Add($"{r.SchemaObject} {r.Effect}");
                var reasons = string.Join("; ", reasonTexts);
                diagnostics.Add(DiagnosticInfo.From(JauntyDiagnostics.JNT9004,
                    $"{entityName}.{methodName}: {reasons}"));
            }
        }

        return new FileResult(
            $"{entityName}.{methodName}.g.cs",
            source,
            diagnostics.ToImmutable(),
            new FileSummary(entityName, methodName, claims: true, emitted: true, canonicalTable),
            ComputeFingerprint(tokens),
            queryModel,
            sqlFile.Path,
            directives.MirrorsTarget);
    }

    /// <summary>
    /// Returns a JNT3009 diagnostic when two projected columns collapse to the
    /// same emitted member name, else null. The comparison is on the emitted
    /// form — <c>DialectMapper.ToPascalCase(name)</c>, exactly what the row POCO
    /// and mapper use — so it catches case-only clashes (<c>x</c>/<c>X</c>) and
    /// separator clashes (<c>my_col</c>/<c>myCol</c>) that both fold to one C#
    /// property, which the compiler would otherwise reject with an opaque CS0102
    /// inside generated code.
    /// </summary>
    private static DiagnosticInfo? FindDuplicateResultColumn(ProjectionModel projection)
    {
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var pcol in projection.Columns)
        {
            string emitted = DialectMapper.ToPascalCase(pcol.Name);
            if (!seen.Add(emitted))
                return DiagnosticInfo.From(JauntyDiagnostics.JNT3009,
                    $"Two result columns map to the same generated property '{emitted}'. Give each SELECT/RETURNING item a distinct alias (AS) — the generated row type cannot declare '{emitted}' twice.");
        }
        return null;
    }

    /// <summary>
    /// Mirrors QueryValidator.Part2's own precondition for running the index
    /// checks at all: a snapshot with no index metadata anywhere makes JNT8004
    /// return early. Kept as its own predicate so JNT8012 asks the same
    /// question the analysis asked, rather than assuming the analysis ran.
    /// </summary>
    private static bool SchemaDeclaresAnyIndex(DatabaseSchema schema)
    {
        foreach (var table in schema.Tables.Values)
        {
            if (table.Indexes.Count > 0)
                return true;
        }
        return false;
    }
}
