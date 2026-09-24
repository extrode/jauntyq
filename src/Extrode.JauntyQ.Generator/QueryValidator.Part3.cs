using Extrode.JauntyQ.Schema;
using Extrode.JauntyQ.SqlParser.IR;

namespace Extrode.JauntyQ.Generator;

public static partial class QueryValidator
{
    /// <summary>
    /// Resolves an (alias, column) reference to its schema column and canonical
    /// table. Shared by the per-query analyzers here and the cross-query
    /// NPlusOneAnalyzer (JNT8008): alias-qualified references resolve through
    /// the statement's alias map; unqualified ones resolve only when exactly one
    /// referenced table has the column (ambiguity is never guessed).
    /// </summary>
    internal static ColumnSchema? ResolveColumn(
        QueryModel query, string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable, DatabaseSchema schema, out string? tableName)
    {
        tableName = null;
        if (string.IsNullOrEmpty(columnName))
            return null;

        if (!string.IsNullOrEmpty(tableAlias))
        {
            if (aliasToTable.TryGetValue(tableAlias, out string? resolved) &&
                SchemaLookup.TryGetTable(schema, resolved, out var aliasedTable) &&
                SchemaLookup.TryGetColumn(aliasedTable!, columnName, out var aliasedColumn))
            {
                tableName = resolved;
                return aliasedColumn;
            }
            return null;
        }

        ColumnSchema? match = null;
        foreach (var table in query.Tables)
        {
            if (SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema) &&
                SchemaLookup.TryGetColumn(tableSchema!, columnName, out var column))
            {
                if (match != null)
                    return null; // ambiguous — JNT2003 territory, skip the value check
                match = column;
                tableName = table.TableName;
            }
        }
        return match;
    }

    private static void DetectUnsupportedConstructs(QueryModel query, List<ValidationError> errors)
    {
        foreach (var construct in query.UnsupportedConstructs)
        {
            // UNION gets its own Error-severity diagnostic (JNT1006) instead
            // of this generic Warning: the generator would otherwise proceed
            // to emit code modeled on only the first branch. See JNT1006's
            // doc comment for why that's a correctness bug, not a stylistic
            // nicety.
            if (construct == "UNION")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1006,
                    "Set operations (UNION, UNION ALL, INTERSECT, EXCEPT) are not supported: the " +
                    "generator only models the first branch's " +
                    "column shape, so a second branch with different nullability would silently generate " +
                    "code that reads NULL as non-nullable and throws at runtime. Split into separate " +
                    "queries, or model the combined result as an application-level merge."));
                continue;
            }

            // SUBQUERY gets its own Error-severity diagnostic (JNT1007) for
            // the same reason as UNION above: a nested SELECT that isn't a
            // WHERE-clause IN/EXISTS predicate falls through to the enclosing
            // statement's ordinary parsing instead of being lifted as its own
            // scope, so the outer query's shape may already be misparsed.
            if (construct == "SUBQUERY")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1007,
                    "This subquery form is not supported: only WHERE-clause '[NOT] IN (SELECT ...)' and " +
                    "'[NOT] EXISTS (SELECT ...)' predicates are modeled as their own scope. A scalar " +
                    "subquery in the projection list or a derived table in FROM falls through to the " +
                    "enclosing statement's ordinary parsing, which can misread the outer query's shape. " +
                    "Rewrite using a JOIN, a WHERE-clause IN/EXISTS predicate, or a CTE."));
                continue;
            }

            // LATERAL (spec 015): missing GRAMMAR, not a missing relation.
            // Before this, the keyword was parsed as the joined table's name
            // and the consumer met JNT2001 pointing at their schema -- for a
            // table the parser had invented. Its own code so the two failures
            // stay distinguishable without reading the message.
            if (construct == "LATERAL")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1009,
                    "LATERAL joins are not supported: the parser has no grammar for them, and nothing " +
                    "about your schema is wrong. Rewrite as a plain JOIN, a WHERE-clause IN/EXISTS " +
                    "predicate, or a GROUP BY with aggregates (which is supported, including over a " +
                    "join). See docs/06-reference/supported-sql.md for the full accepted surface."));
                continue;
            }

            // DISTINCT ON is PostgreSQL-only and also missing grammar, so it
            // shares JNT1009 -- with its own message, since it has a rewrite
            // that LATERAL does not. Before it was refused, the ON and its
            // parenthesized list glued onto the first projected column as one
            // opaque expression: loud but misdirected when that column had no
            // alias (JNT3004), and SILENT when it had one.
            if (construct == "DISTINCT ON")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1009,
                    "PostgreSQL's 'DISTINCT ON (...)' is not supported: the parser has no grammar for " +
                    "it, and nothing about your schema is wrong. Its expression list would otherwise be " +
                    "read as part of your first selected column. Rewrite as a window function -- " +
                    "'row_number() over (partition by <the DISTINCT ON columns> order by <your ORDER BY>)' " +
                    "in a CTE, filtered to 1 in the outer query -- or select the distinct keys and their " +
                    "rows in two queries. See docs/06-reference/supported-sql.md for the accepted surface."));
                continue;
            }

            // APPLY is LATERAL under T-SQL's spelling, and shares JNT1009 --
            // but not the message. Telling someone who wrote CROSS APPLY that
            // "LATERAL joins are not supported" makes them hunt for a keyword
            // they never used.
            if (construct == "APPLY")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1009,
                    "CROSS APPLY / OUTER APPLY is not supported: the parser has no grammar for it, and " +
                    "nothing about your schema is wrong. It is SQL Server's spelling of a LATERAL join. " +
                    "Rewrite as a plain JOIN, a WHERE-clause IN/EXISTS predicate, or a GROUP BY with " +
                    "aggregates (which is supported, including over a join). See " +
                    "docs/06-reference/supported-sql.md for the full accepted surface."));
                continue;
            }

            // SELECT INTO shares JNT1009 with LATERAL / DISTINCT ON / APPLY,
            // but it is a different kind of thing and the message says so: the
            // others are query grammar JauntyQ has not implemented, while this
            // one is a statement that CREATES A TABLE. There is no rewrite that
            // makes it a query, so the message offers the two real options
            // (move it to a migration, or split it) rather than a rephrasing.
            if (construct == "SELECT INTO")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1009,
                    "'SELECT ... INTO <table>' is not supported: it creates a table rather than returning " +
                    "rows, so there is no result shape for JauntyQ to generate a mapper from, and nothing " +
                    "about your schema is wrong. Move the table creation into a migration under your " +
                    "migrations directory, and keep the SELECT here as a plain query -- or, if you meant " +
                    "PL/pgSQL's 'SELECT ... INTO <variable>', that belongs in a stored procedure rather " +
                    "than a query file. See docs/06-reference/supported-sql.md for the accepted surface."));
                continue;
            }

            // MULTI_STATEMENT joins UNION and SUBQUERY as an Error rather than
            // a JNT1001 Warning: the merged (or truncated) model means the
            // generated mapper matches neither statement. See JNT1008.
            if (construct == "MULTI_STATEMENT")
            {
                errors.Add(new ValidationError(JauntyDiagnostics.JNT1008,
                    "This file contains more than one SQL statement. JauntyQ generates one method per file " +
                    "from one statement: a second statement's tables and columns are merged into the first " +
                    "statement's model (SELECT) or dropped entirely (INSERT/UPDATE/DELETE), so the generated " +
                    "code would not match either statement. Split each statement into its own .sql file."));
                continue;
            }

            errors.Add(new ValidationError(JauntyDiagnostics.JNT1001,
                $"Unsupported SQL construct: {construct}"));
        }
    }

    private static void ValidateJoinSide(
        string tableAlias, string columnName,
        Dictionary<string, string> aliasToTable,
        DatabaseSchema schema,
        List<ValidationError> errors,
        Dictionary<string, List<string>>? virtualTables = null)
    {
        if (string.IsNullOrEmpty(tableAlias))
            // Stryker disable once Statement : falling through looks up "" in the alias map, which only a table ref with an empty name and alias puts there, and then needs the schema to hold a table named "" before any error can differ
            return;

        if (aliasToTable.TryGetValue(tableAlias, out string? tableName))
        {
            // CTE-qualified join sides are opaque — the virtual columns are
            // already scoped for projection resolution; skip existence here.
            if (virtualTables != null && virtualTables.ContainsKey(tableName))
                return;
            if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
            {
                if (!SchemaLookup.ContainsColumn(tableSchema!, columnName))
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT2002,
                        $"Column '{columnName}' does not exist in table '{tableName}'"));
                }
            }
        }
    }

    /// <summary>
    /// JNT7002: verbatim SQL emission has no dialect rewrite, so a RETURNING
    /// clause or a data-modifying CTE fails under dialects that don't support
    /// them as written. The two constructs are gated independently -- they
    /// do NOT share the same dialect set:
    ///   - Writable (data-modifying) CTEs, i.e. a CTE whose own body is
    ///     INSERT/UPDATE/DELETE, are a postgres-only extension among the
    ///     four dialects here. sqlserver, mysql, AND sqlite all require
    ///     every CTE body to be a SELECT: sqlite's own WITH-clause grammar
    ///     (https://www.sqlite.org/lang_with.html, "common-table-expression")
    ///     defines the CTE body as a select-stmt, full stop -- sqlite has
    ///     never supported writable CTEs despite otherwise being close to
    ///     postgres in feature coverage (AUD-R11: this dialect used to be
    ///     lumped in with postgres and fell through this check entirely).
    ///   - RETURNING itself: sqlserver has no rewrite (must use OUTPUT).
    ///     Stock MySQL has no RETURNING clause on any DML statement; MariaDB
    ///     (which shares the "mysql" dialect string — see DialectMapper)
    ///     added RETURNING for INSERT/DELETE in 10.5 and UPDATE in 13.0, so
    ///     this intentionally over-flags valid MariaDB RETURNING usage in
    ///     exchange for catching it on stock MySQL, where it always fails.
    ///     postgres and sqlite (3.35.0+) both support RETURNING and are
    ///     never gated on this construct.
    /// </summary>
    private static void ValidateDialectConstructs(QueryModel query, DatabaseSchema schema, List<ValidationError> errors)
    {
        bool isSqlServer = string.Equals(schema.Dialect, "sqlserver", StringComparison.OrdinalIgnoreCase);
        bool isMySql = string.Equals(schema.Dialect, "mysql", StringComparison.OrdinalIgnoreCase);
        bool isSqlite = string.Equals(schema.Dialect, "sqlite", StringComparison.OrdinalIgnoreCase);

        // Only a CTE whose own body is INSERT/UPDATE/DELETE is the
        // unsupported "writable CTE" construct the message describes -- a
        // plain read-only "WITH cte AS (SELECT ...) SELECT ..." is ordinary,
        // universally-supported SQL under every dialect here. Gating on
        // query.Ctes.Count > 0 alone (any CTE at all) used to flag that
        // ordinary, extremely common form as unsupported under sqlserver/
        // mysql/sqlite, even though nothing about it is dialect-specific.
        if (isSqlServer || isMySql || isSqlite)
        {
            foreach (var cte in query.Ctes)
            {
                if (cte.Body.StatementType != StatementType.Select)
                {
                    errors.Add(new ValidationError(JauntyDiagnostics.JNT7002,
                        $"Data-modifying CTEs (WITH ... INSERT/UPDATE/DELETE) are not supported under the {schema.Dialect} dialect; the SQL is emitted verbatim with no rewrite."));
                    break;
                }
            }
        }

        // RETURNING is a separate construct from writable CTEs above: sqlite
        // supports RETURNING (3.35.0+) even though it doesn't support
        // writable CTEs, so it must NOT be folded into the same dialect
        // check as the loop above.
        if (isSqlServer || isMySql)
        {
            if (query.HasReturning || query.Returning.Count > 0)
            {
                string advice = isSqlServer
                    ? "use OUTPUT"
                    : "use LAST_INSERT_ID()/a follow-up SELECT, or MariaDB 10.5+/13.0 if RETURNING is actually available on your server";
                errors.Add(new ValidationError(JauntyDiagnostics.JNT7002,
                    $"RETURNING is not supported under the {schema.Dialect} dialect ({advice}); the SQL is emitted verbatim so it cannot be rewritten."));
            }
        }
    }

    /// <summary>
    /// Builds the JNT3002 message with the paste-ready explicit column list
    /// (alias-qualified when the query joins multiple tables).
    /// </summary>
    private static string BuildSelectStarMessage(QueryModel query, DatabaseSchema schema)
    {
        var cols = new List<string>();
        bool qualify = query.Tables.Count > 1;
        foreach (var table in query.Tables)
        {
            if (!SchemaLookup.TryGetTable(schema, table.TableName, out var tableSchema))
                continue; // JNT2001 already reported
            string prefix = !string.IsNullOrEmpty(table.Alias) ? table.Alias : table.TableName;
            foreach (var col in tableSchema!.Columns.Values)
            {
                cols.Add(qualify ? $"{prefix}.{col.Name}" : col.Name);
            }
        }

        string list = cols.Count > 0 ? string.Join(", ", cols) : "<no columns resolved>";
        return "SELECT * is not allowed: the projection must be explicit so the generated " +
               "result type is deterministic and schema drift fails this build instead of " +
               $"failing at runtime. Replace * with: {list}";
    }

    /// <summary>
    /// Resolves how many columns a `*` (optionally table-qualified, e.g.
    /// <c>t.*</c>) projection item in <paramref name="body"/> actually expands
    /// to, against real schema tables and in-scope CTE virtual tables --
    /// mirroring <see cref="BuildSelectStarMessage"/>'s resolution but
    /// returning a count instead of a message. Used by the JNT3007
    /// IN-subquery single-column check so a real multi-column
    /// <c>SELECT * FROM t</c> subquery isn't silently exempted just because
    /// its shape wasn't spelled out as an explicit column list. Returns null
    /// when a referenced table/alias can't be resolved (already reported
    /// separately via JNT2001/JNT2002) rather than guess a count.
    /// </summary>
    private static int? CountStarColumns(QueryModel body, string tableAliasQualifier,
        DatabaseSchema schema, Dictionary<string, List<string>> virtualTables)
    {
        var localAliasToTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in body.Tables)
        {
            string key = !string.IsNullOrEmpty(t.Alias) ? t.Alias : t.TableName;
            localAliasToTable[key] = t.TableName;
        }

        var tableNames = new List<string>();
        if (!string.IsNullOrEmpty(tableAliasQualifier))
        {
            if (!localAliasToTable.TryGetValue(tableAliasQualifier, out var resolved))
                return null;
            tableNames.Add(resolved);
        }
        else
        {
            foreach (var t in body.Tables)
                tableNames.Add(t.TableName);
        }

        int total = 0;
        foreach (var tableName in tableNames)
        {
            if (virtualTables.TryGetValue(tableName, out var vcols))
            {
                total += vcols.Count;
            }
            else if (SchemaLookup.TryGetTable(schema, tableName, out var tableSchema))
            {
                total += tableSchema!.Columns.Count;
            }
            else
            {
                return null;
            }
        }
        return total;
    }
}
