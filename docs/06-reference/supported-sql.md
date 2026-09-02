# Supported SQL surface

What JauntyQ's parser accepts, what it refuses, and which diagnostic you get
when you cross the line.

This page exists because the alternative is probing. A consumer report on
2026-08-17 described writing five variants of one query to discover by trial
which shapes the generator would take, three of them rejected with a message
that named the construct but not the boundary, and one (`LATERAL`) rejected
with a message that pointed at their schema for a table the parser had
invented. Reading a list is cheaper than that.

**Every claim here is measured, not remembered.** The source is
`tests/JauntyQ.SqlParser.Tests/SupportedSurfaceProbeTests.cs`, which parses each
shape below and asserts the outcome, plus `RegistryParityTests`, which asserts
that every `JNTxxxx` code named on this page resolves to a real descriptor. A
change that widens or narrows the surface turns those red.

## The shape of the rule

JauntyQ parses SQL to model the **result shape**, which columns come back, from
which tables, with what nullability, so it can emit a typed mapper. It is not a
SQL engine and does not need to be: the text you write is the text that runs,
verbatim. So the boundary is not "what is valid SQL", it is **"what can be
modeled without guessing"**. A construct is refused when the model JauntyQ would
build from it could disagree with what the database actually returns, because a
wrong model produces code that compiles and then throws at runtime.

That is why `UNION` is an error and `GROUP BY` is not.

## One statement per file

JauntyQ generates one method per `.sql` file from one statement. A file holding
two statements is **JNT1008**, an error: a second `SELECT`'s tables and columns
are merged into the first statement's model, and a second write statement is
dropped entirely, so the generated code matches neither. Split them.

## Supported

| Shape | Notes |
|---|---|
| `SELECT`, `INSERT`, `UPDATE`, `DELETE` | One per file. |
| `INSERT ... SELECT` | Parses as an `INSERT`. |
| `RETURNING` | On any write statement; makes it row-returning, and composes with `-- @first`. |
| `JOIN`, `INNER` / `LEFT` / `RIGHT` / `FULL` / `CROSS` | Outer joins widen the joined table's columns to nullable in the generated row type. |
| `ON` and `USING (...)` join conditions | `USING` is expanded to the equivalent same-named-column equalities. |
| Comma-separated implicit joins (`FROM a, b`) | Every table is registered, not just the first. |
| `WHERE` with `=` `!=` `<>` `<` `>` `<=` `>=` `LIKE` `BETWEEN` `IN (...)` `IS [NOT] NULL` `AND` `OR` | |
| `[NOT] IN (SELECT ...)` and `[NOT] EXISTS (SELECT ...)` **in `WHERE`** | Lifted and validated as their own scope. Correlated `EXISTS` included. |
| `GROUP BY` and `HAVING` | Including over a join. |
| Aggregates, `COUNT` `SUM` `AVG` `MIN` `MAX`, and dialect functions such as `bool_or` | `COUNT(*) FILTER (WHERE ...)` parses. |
| `ORDER BY` with `ASC` / `DESC`, by column, ordinal, or output alias | |
| `LIMIT` / `OFFSET`, T-SQL `OFFSET n ROWS FETCH NEXT m ROWS ONLY`, and `TOP n` (including `TOP n PERCENT` and `TOP n WITH TIES`) | See the pagination note below. A column genuinely named `percent` is still projected: `PERCENT` is read as a modifier only when another projection item follows it, since the tokenizer strips quoting and cannot tell `[percent]` from the keyword. |
| `DISTINCT` | |
| `CASE WHEN ... THEN ... ELSE ... END` | Needs `AS alias`, see expressions below. |
| Window functions (`... OVER (PARTITION BY ... ORDER BY ...)`) | Parse as aliased expressions; the type is not inferred from the schema, so declare it with [`-- @result`](directives.md). |
| `CAST`, `COALESCE`, `NULLIF` | |
| Non-recursive CTEs (`WITH x AS (...) SELECT ...`) | Each CTE is an in-scope virtual table for the final statement. |
| Schema-qualified names (`dbo.Products`, `public.users`) | The qualifier is stripped for schema resolution and preserved verbatim in the emitted SQL. |
| Parameters `@name` | Repeated use of one name binds one parameter. |
| Views | Readable like tables in every dialect. Writes to them are **JNT2021**, see below. The index and foreign-key analyses (**JNT8004**, **JNT8006**, **JNT8007**) stay silent on a view: it declares neither, so those checks would be reporting the absence of metadata rather than a real finding. |
| Materialized views | **PostgreSQL only.** They live in `pg_class`/`pg_attribute` and are invisible to `information_schema`, so a dialect whose extractor reads only the standard views cannot report them. SQL Server indexed views are deliberately not captured. Their columns carry the same type facets (length, precision, scale, Unicode) as a base table's, until 2026-08-18 they carried none, which silently disabled the JNT5001 literal-length check on them. |

### Expressions need an alias

Any projection item that is not a plain column reference must carry `AS alias`.
Without one there is no name to give the generated property, and inventing one
from the expression text produces a name that changes whenever the expression is
edited. Missing alias is **JNT3004**.

```sql
select u.id, count(*) as order_count          -- fine
select u.id, count(*)                         -- JNT3004
```

## Not supported

| Shape | Diagnostic | Severity | What to write instead |
|---|---|---|---|
| `UNION` / `UNION ALL` / `INTERSECT` / `EXCEPT` | **JNT1006** | Error | Separate queries merged in application code. Only the first branch's column shape would be modeled, so a second branch with different nullability generates code that reads `NULL` as non-nullable. |
| Scalar subquery in the projection list | **JNT1007** | Error | A `JOIN` with `GROUP BY`, or a CTE. |
| Derived table in `FROM` (`from (select ...) x`) | **JNT1007** | Error | A CTE. |
| `LATERAL` joins, in `FROM` or in a join | **JNT1009** | Error | A plain `JOIN`, a `WHERE`-clause `IN`/`EXISTS`, or a `GROUP BY` with aggregates. |
| `CROSS APPLY` / `OUTER APPLY` (SQL Server) | **JNT1009** | Error | The same rewrites as `LATERAL`, this is T-SQL's spelling of it. |
| `DISTINCT ON (...)` (PostgreSQL) | **JNT1009** | Error | `row_number() over (partition by <the DISTINCT ON columns> order by <your ORDER BY>)` in a CTE, filtered to `1` outside it; or two queries. Plain `DISTINCT` is unaffected and still supported. |
| `SELECT ... INTO <table>` (T-SQL) | **JNT1009** | Error | Move the table creation into a migration and keep the `SELECT` here as a plain query. It creates a table rather than returning rows, so there is no result shape to map. PL/pgSQL's `SELECT ... INTO <variable>` belongs in a stored procedure. `INSERT INTO` is unaffected and still supported. Every target spelling reaches the same refusal on its own: T-SQL `#temp` and `##global`, and PostgreSQL's `INTO TEMP` / `TEMPORARY` / `UNLOGGED [TABLE] name`. |
| `WITH RECURSIVE` | **JNT1001** | Warning | Iterate in application code. |
| More than one statement in a file | **JNT1008** | Error | One statement per file. |

**`JNT1009` is new in v0.3.0 and is why this page exists.** `LATERAL` is not a
tokenizer keyword, so it used to arrive in the table-name position as an
ordinary identifier and the parser recorded a relation literally named
`lateral`. What the consumer then saw was `JNT2001: Table 'lateral' does not
exist in schema`, true of a table the parser had invented, and advice pointing
at a schema that was perfectly correct. Missing grammar and a missing relation
are different failures needing opposite responses, so they no longer share a
code: **JNT2001 now means only "this relation is not in your snapshot"**, and
JNT1009 means "JauntyQ has no grammar for this".

The guarantee is about the construct, not about where you wrote it. The first
cut of this fix guarded only the join position, so `FROM LATERAL (...)`, the
comma form `FROM a, LATERAL (...)`, and `CROSS APPLY` all still invented a
relation, the fix held for the one shape that had been reported and nowhere
else. Every relation position is covered now, and
`UnmodeledRelationTests` has a case per position rather than trusting one to
stand for the rest.

### A measured gap

`UPDATE ... FROM other_table` parses without complaint but models only the
target table, so the second relation is invisible to validation. It is neither
supported nor refused, which is the worst of the three; it is recorded in
`the todo list` and pinned by
`SupportedSurfaceProbeTests.UpdateFrom_ParsesButDoesNotModelTheSecondRelation`.

`DISTINCT ON` (PostgreSQL) **was** in the same state and is no longer: since
2026-08-18 it is refused as **JNT1009** and appears in the table above. Why it
was worth fixing rather than documenting: the `ON` and its parenthesized list
glued onto the first projected column as one opaque expression, so the query
modeled one fewer real column than it returns. Unaliased that reached JNT3004
("this expression needs an alias"), loud, but blaming a column the consumer
did not write. Aliased, it passed validation outright and the generated mapper
typed its first property from expression-shape inference over the text
`ON ( ... ) <column>`.

### Escaping a quote inside a string literal

Double it. `'it''s'` works on every dialect JauntyQ supports.

MySQL's backslash form, `'it\'s'`, does **not**: the tokenizer implements only
the ANSI `''` escape, so the backslash leaves the quotes unbalanced and the file
is refused with JNT1002. That refusal is deliberate and the message names the
backslash when it finds one, but it is a refusal, not a silent mis-parse, and
there is no build in which a backslash-escaped file produces wrong SQL. Pinned
by `TokenizerTests`'s three backslash cases, which also pin why the fix is not
simply "honor the backslash": `'C:\'` is a complete literal under ANSI rules
and tokenizes as one today.

## Things that are not the parser

Three refusals get mistaken for grammar gaps.

**A missing relation is JNT2001**, and it means what it says: the table or view
is not in your schema snapshot. Re-run `jauntyq schema pull`. Since v0.3.0 this
code is never raised for a construct the parser failed to understand.

**An unindexed filter is JNT8004**, a performance warning, not a rejection. The
query is fine; the column you filter on has no index. Add the index, or accept
the scan for this one query with
[`-- @allow-unindexed <reason>`](directives.md). If your project sets
`TreatWarningsAsErrors`, every JauntyQ warning arrives as a build error, that
escalation is your setting, not JauntyQ's severity.

**Unstable pagination is JNT8009 / JNT8010**, also warnings. A `LIMIT`/`OFFSET`
page whose `ORDER BY` does not resolve to a unique key can return the same row
on two pages and skip another, because the engine is free to break the tie
differently per query. Add the primary key as the last `ORDER BY` term.

## See also

- [Diagnostics](diagnostics.md), every `JNTxxxx` code.
- [Directives](directives.md), `-- @result`, `-- @params`, `-- @allow-unindexed`.
- [Dialect differences](dialects.md), what the emitted SQL looks like per engine.
- [Troubleshooting](troubleshooting.md), symptoms mapped to causes.
