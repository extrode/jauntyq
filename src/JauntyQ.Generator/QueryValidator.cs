using JauntyQ.Schema;
using JauntyQ.SqlParser.IR;

namespace JauntyQ.Generator;

public static partial class QueryValidator
{
    public static List<ValidationError> Validate(QueryModel query, DatabaseSchema? schema)
    {
        var errors = new List<ValidationError>();

        // JNT6001: Schema not provided
        if (schema == null)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT6001,
                "Schema snapshot not found. Run 'jaunty schema pull'"));
            return errors;
        }

        // WITH RECURSIVE is out of scope: reported as an unsupported construct
        // (the parser did not descend into the body).
        if (query.WithRecursive)
        {
            DetectUnsupportedConstructs(query, errors);
            return errors;
        }

        // Data-modifying CTE chain (Feature B): validate each CTE body as its
        // own statement — with earlier CTE names in scope as virtual tables —
        // then the final statement with the full CTE scope. Per-statement
        // validators (ambiguity, JNT8001) never run across the boundary.
        if (query.Ctes.Count > 0)
        {
            var scope = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var cte in query.Ctes)
            {
                ValidateStatement(cte.Body, schema, scope, errors);
                // Later CTEs + the final statement can reference this CTE name.
                scope[cte.Name] = cte.VirtualColumns;
            }
            ValidateStatement(query, schema, scope, errors);
            ValidateDialectConstructs(query, schema, errors);
            return errors;
        }

        ValidateStatement(query, schema, null, errors);
        ValidateDialectConstructs(query, schema, errors);
        return errors;
    }

    /// <summary>
    /// Validates one statement (SELECT/INSERT/UPDATE/DELETE) against the schema,
    /// treating any name in <paramref name="virtualTables"/> as an in-scope CTE
    /// whose columns are the mapped list. Errors are appended to <paramref name="errors"/>.
    /// </summary>
    private static void ValidateStatement(QueryModel query, DatabaseSchema schema,
        Dictionary<string, List<string>>? virtualTables, List<ValidationError> errors,
        bool isSubquery = false, Dictionary<string, string>? parentAliasToTable = null)
    {
        virtualTables ??= EmptyScope;

        // JNT3001: Empty query (SELECT only — CRUD has no columns). A predicate
        // subquery produces no generated result type, so its projection shape is
        // never surfaced to the caller — skip the projection-shape diagnostics
        // (empty/star/missing-alias) for it; tables/columns/params still check.
        if (!isSubquery && query.StatementType == StatementType.Select && query.Columns.Count == 0)
        {
            errors.Add(new ValidationError(JauntyDiagnostics.JNT3001,
                "SQL file is empty or contains no SELECT columns"));
            return;
        }

        // JNT3004: every expression projection item requires an explicit AS alias.
        if (!isSubquery)
        {
            foreach (var missing in query.ExpressionsMissingAlias)
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT3004,
                    $"Expression projection '{missing}' requires an explicit alias: add 'AS <name>' so the generated result property has a stable name."));
            }
        }

        // JNT4004: Duplicate parameter names
        var paramNames = new HashSet<string>();
        foreach (var param in query.Parameters)
        {
            if (!paramNames.Add(param.Name))
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT4004,
                    $"Duplicate parameter name '@{param.Name}'"));
            }
        }

        // Build alias-to-table map. A predicate subquery (EXISTS/IN) can
        // correlate back to the enclosing statement's own aliases in its WHERE
        // clause (e.g. `exists (select 1 from t where t.x = outer.y)`), so it
        // starts from the parent's map; its own tables take precedence on a
        // name collision. Top-level statements and CTE bodies (never
        // correlated) pass no parent map, so this is a no-op for them.
        var aliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parentAliasToTable != null)
        {
            foreach (var kv in parentAliasToTable)
                aliasToTable[kv.Key] = kv.Value;
        }
        foreach (var table in query.Tables)
        {
            string key = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            aliasToTable[key] = table.TableName;
        }

        // JNT2001: Table exists. A CTE name in scope is a virtual table and
        // never errors here; but a CTE that exposes NO columns (a body without
        // RETURNING / SELECT projection) cannot be a FROM source.
        foreach (var table in query.Tables)
        {
            if (virtualTables.TryGetValue(table.TableName, out var vcols))
            {
                if (vcols.Count == 0)
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2001,
                        $"CTE '{table.TableName}' returns no columns (its body has no RETURNING or SELECT projection); it cannot be used as a FROM source."));
                continue;
            }
            if (!schema.Tables.ContainsKey(table.TableName))
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT2001,
                    $"Table '{table.TableName}' does not exist in schema"));
            }
        }

        // JNT2002: Column exists + JNT2003: Ambiguous column
        foreach (var col in query.Columns)
        {
            // Expression projections are opaque — no column-existence checks
            // inside them (Feature A). Their type is resolved by the generator.
            if (col.IsExpression)
                continue;

            if (col.ColumnName == "*")
            {
                // JNT3002: SELECT * makes the generated result type depend on
                // live table definition order, and schema drift (a later ADD
                // COLUMN) surfaces at RUNTIME instead of failing this build.
                // The message hands the caller the explicit list to paste.
                // A predicate subquery has no generated result type; EXISTS
                // (SELECT * ...) is idiomatic, so the star check is skipped there
                // (the IN single-column rule runs separately on the caller side).
                if (!isSubquery)
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT3002,
                        BuildSelectStarMessage(query, schema)));
                continue;
            }

            if (!string.IsNullOrEmpty(col.TableAlias))
            {
                // Qualified column — resolve alias to table name (real or CTE)
                if (aliasToTable.TryGetValue(col.TableAlias, out string? tableName))
                {
                    if (virtualTables.TryGetValue(tableName, out var vcols))
                    {
                        if (!vcols.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase))
                            errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                                $"Column '{col.ColumnName}' does not exist in CTE '{tableName}'"));
                    }
                    else if (schema.Tables.TryGetValue(tableName, out var tableSchema))
                    {
                        if (!tableSchema.Columns.ContainsKey(col.ColumnName))
                        {
                            errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                                $"Column '{col.ColumnName}' does not exist in table '{tableName}'"));
                        }
                    }
                }
                else
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Unknown table alias '{col.TableAlias}'"));
                }
            }
            else
            {
                // Unqualified column — check ambiguity across all referenced
                // tables (real schema tables and in-scope CTE virtual tables).
                var matchingTables = new List<string>();
                foreach (var table in query.Tables)
                {
                    if (virtualTables.TryGetValue(table.TableName, out var vcols))
                    {
                        if (vcols.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase))
                            matchingTables.Add(table.TableName);
                    }
                    else if (schema.Tables.TryGetValue(table.TableName, out var tableSchema) &&
                             tableSchema.Columns.ContainsKey(col.ColumnName))
                    {
                        matchingTables.Add(table.TableName);
                    }
                }

                if (matchingTables.Count == 0)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Column '{col.ColumnName}' does not exist in any referenced table"));
                }
                else if (matchingTables.Count > 1)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2003,
                        $"Ambiguous column reference '{col.ColumnName}' found in tables: {string.Join(", ", matchingTables)}"));
                }
            }
        }

        // Validate join columns exist (join sides against real tables only;
        // CTE-qualified joins are opaque and skipped by ValidateJoinSide).
        foreach (var join in query.Joins)
        {
            ValidateJoinSide(join.LeftTable, join.LeftColumn, aliasToTable, schema, errors, virtualTables);
            ValidateJoinSide(join.RightTable, join.RightColumn, aliasToTable, schema, errors, virtualTables);
        }

        // JNT5001/JNT5002: literals must fit their target columns
        ValidateLiterals(query, aliasToTable, schema, errors);

        // JNT8xxx: performance analysis (warnings only, never block)
        ValidatePerformance(query, aliasToTable, schema, errors);

        // WHERE-clause predicate subqueries: validate each inner SELECT as its
        // own statement scope (same CTE virtual tables in scope), then enforce
        // the IN single-column rule. The subquery contributes nothing to this
        // statement's result shape, so this runs after the outer checks.
        foreach (var sub in query.Subqueries)
        {
            ValidateStatement(sub.Body, schema, virtualTables, errors, isSubquery: true, parentAliasToTable: aliasToTable);

            if (sub.Kind == SubqueryKind.In)
            {
                int projected = sub.Body.Columns.Count(c => c.ColumnName != "*");
                bool hasStar = sub.Body.Columns.Any(c => c.ColumnName == "*");
                if (!hasStar && projected != 1)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT3007,
                        $"An IN-subquery must project exactly one column, but it projects {projected}. Reduce the subquery's SELECT list to a single column."));
                }
            }
        }

        // JNT1001: Unsupported SQL constructs
        DetectUnsupportedConstructs(query, errors);
    }

    private static readonly Dictionary<string, List<string>> EmptyScope =
        new(StringComparer.OrdinalIgnoreCase);

}
