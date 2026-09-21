# Directives reference

Directives are `--` comment lines placed at the top of a `.sql` file that
control the shape of the code generated for that query. They are parsed out and
stripped from the emitted `CommandText`; the SQL that runs never contains them.

## Syntax rules

- A directive is a line comment beginning `-- @` followed by the keyword.
- Keywords are **case-insensitive** (`-- @First` == `-- @first`); type names
  and parameter names inside a directive are **case-sensitive** (they are C#).
- `-- @type` and `-- @each` may appear multiple times; the others appear at
  most once per file. Writing one of the others twice is reported as
  **`JNT3011`** and is not an error: the later line wins, as it always has, and
  the warning names the value that was overwritten. Before JNT3011 this was
  silent, so a file could carry two `-- @result` lines, or two `-- @allow-sort`
  reasons, with nothing to show which one the build acted on.
- Plain `--` comments without `@` are left untouched.

## Directive summary

| Directive | Applies to | Effect |
|---|---|---|
| [`-- @first`](#-first) | SELECT | Return `Row?` instead of `List<Row>`. |
| [`-- @stream`](#-stream) | SELECT | Return `IEnumerable<Row>` / `IAsyncEnumerable<Row>`, yielding lazily. |
| [`-- @identity`](#-identity) | INSERT | Return the database-assigned id, typed. |
| [`-- @result`](#-result) | any | Declare the result type (void / named type / inline columns). |
| [`-- @params`](#-params) | any | Declare parameter types the parser cannot infer. |
| [`-- @each`](#-each) | any | Expand a parameter into a runtime IN-list. |
| [`-- @type`](#-type) | SELECT | Declare the DB type of a projected expression. |
| [`-- @proc`](#-proc) | SQL Server only | Emit a stored-procedure definition and call it. |
| [`-- @call`](#-call) | any | Bind to a procedure that already exists in the database. |
| [`-- @mirrors`](#-mirrors) | any with a WHERE | Declare that this query filters identically to another, and have the build check it. |
| [`-- @allow-unindexed`](#-allow-unindexed) | any with a WHERE | Accept this query's unindexed filter columns deliberately. Reason mandatory. |
| [`-- @allow-sort`](#-allow-sort) | any with an ORDER BY | Accept this query's runtime sort deliberately. Reason mandatory. |
| [`-- @allow-n-plus-one`](#-allow-n-plus-one) | any child point lookup by a foreign key | Accept this query's N+1 shape deliberately. Reason mandatory. |

---

### `-- @first`

Marks a SELECT as returning a single row. The return type becomes `Row?`
(async `Task<Row?>`) instead of `List<Row>`. Cannot combine with `-- @stream`
(`JNT3003`).

```sql
-- @first
SELECT id, username, email FROM users WHERE username = @Username
```

### `-- @stream`

Yields rows lazily straight off the reader: `IEnumerable<Row>` (sync) or
`IAsyncEnumerable<Row>` (async), never buffering into a list. Keeps memory
constant over large result sets. SELECT-only; cannot combine with `-- @first`
or `-- @proc` (`JNT3003`).

```sql
-- @stream
SELECT ProductId, ProductName FROM Products ORDER BY ProductId
```

### `-- @identity`

On an INSERT, returns the database-assigned identity value (typed to the
column) instead of the affected-row count. The emitted SQL is dialect-native:
`OUTPUT INSERTED.<col>` (SQL Server), `RETURNING <col>` (PostgreSQL / SQLite),
or `SELECT LAST_INSERT_ID()` narrowed to the column's type (MySQL).

Requires an INSERT statement, exactly one identity column on the target table,
and a dialect in the snapshot; violations are `JNT7001`. Cannot combine with a
hand-written `RETURNING` clause or `-- @proc`. Auto-CRUD `Insert` applies this
automatically on tables with a single identity column.

```sql
-- @identity
INSERT INTO articles (slug, title, body, author_id)
VALUES (@Slug, @Title, @Body, @AuthorId)
```

### `-- @result`

Declares the result shape explicitly. Three forms:

- `-- @result void`, no result set; the method returns `int` (row count). Use
  for INSERT/UPDATE/DELETE.
- `-- @result TypeName`, materialize rows into an existing type instead of a
  generated POCO. May be fully qualified.
- `-- @result (int Id, string Name)`, inline column list; generates a
  query-specific DTO. Names must match the SELECT aliases.

```sql
-- @result void
DELETE FROM products WHERE product_id = @ProductId
```

### `-- @params`

Declares parameter types the parser cannot infer from context (for example a
parameter used only inside a subquery or expression). Comma-separated
`Name:Type` pairs; types may be nullable.

An uninferrable parameter is a build error (`JNT4003`), not a silent `object`
fallback, the message tells you exactly which `-- @params` line to add.

```sql
-- @params CategoryId:int, MinPrice:decimal, Name:string?
SELECT p.product_id, p.product_name FROM products p
WHERE p.category_id = @CategoryId AND p.unit_price >= @MinPrice
```

### `-- @each`

Marks a parameter for runtime IN-list expansion. The C# parameter becomes
`IReadOnlyList<T>`, and each `@Name` occurrence in the SQL is expanded to
`@Name0, @Name1, ...` at call time. Repeatable. SELECT-only, and cannot
combine with `-- @proc` (`JNT3003` otherwise).

```sql
-- @each Ids
SELECT ProductId, ProductName FROM Products WHERE ProductId IN (@Ids)
```

An empty list short-circuits to an empty result before any connection is
opened (`IN ()` is invalid SQL everywhere). An oversize list fails fast with
`ArgumentException` at the dialect's per-command parameter budget, 2,000
elements on SQL Server (server limit ~2,100 parameters per request), 32,000
on SQLite, 65,000 on PostgreSQL/MySQL, instead of an obscure provider error;
batch larger workloads into chunks. Note that on SQL Server every distinct
list length compiles a distinct query plan, so very high-cardinality IN-lists
also pollute the plan cache, a table-valued parameter or staging table is
the better tool at that scale.

### `-- @type`

Declares the database type of a projected expression the generator cannot infer
from the schema (aggregates, function results, computed columns). Syntax is
`-- @type <alias> <dbtype>`, where `<alias>` matches the SELECT alias and
`<dbtype>` (which may contain spaces) is resolved per dialect. Repeatable.

**`-- @type` is an assertion, not a coercion.** It only tells the generator
what C# type to bind the reader call to, it does not cast or convert the
value the database actually returns. If the declared type doesn't match what
the expression evaluates to at runtime (e.g. asserting `double precision` on
an integer division that the database evaluates as an integer), you get an
`InvalidCastException` at read time, not a build error. Where the mismatch is
avoidable, fix the SQL itself (an explicit `CAST`) rather than relying on
`-- @type` to paper over it.

```sql
-- @type payment_count int
-- @type total_amount numeric
SELECT st.staff_id,
       COUNT(p.payment_id) AS payment_count,
       SUM(p.amount)       AS total_amount
FROM staff st JOIN payment p ON p.staff_id = st.staff_id
GROUP BY st.staff_id
```

### `-- @proc`

Emits a stored-procedure definition (a `CREATE OR ALTER PROCEDURE` constant on a
nested `Proc` class) and generates the call with `CommandType.StoredProcedure`.
With no argument the procedure name is inferred from entity + method; an
argument overrides it and must be a valid C# identifier (`JNT2004` otherwise).
Cannot combine with `-- @stream` or `-- @identity`. Restricted to the
`sqlserver` dialect (`JNT7002` on any other dialect): `CREATE OR ALTER
PROCEDURE` and T-SQL parameter types have no equivalent on Postgres/MySQL,
and SQLite has no stored procedures at all.

```sql
-- @proc usp_GetProducts
SELECT product_id, product_name FROM products
```

### `-- @call`

Binds the file to a stored procedure that **already exists** in the database.
The file carries no SQL body; parameters and result columns come from the
`procedures` section of the snapshot (captured by `jauntyq schema pull`). Emits a
`CommandType.StoredProcedure` call with typed IN parameters (OUT/INOUT become
C# `out`/`ref`), returning `List<Result>` for row-returning procedures or `int`
otherwise. An unknown procedure name is `JNT2005`.

**PostgreSQL:** the `int` returned for a non-row-returning procedure is
always `-1`, unconditionally. Postgres's `CALL` protocol reports only a
bare completion tag for a procedure invocation, never an affected-row
count, regardless of how the procedure is authored, a structural
PostgreSQL/Npgsql limitation with no ADO.NET-level workaround. Do not
rely on this value to detect whether a Postgres procedure's side effects
occurred; the generated method's own doc comment repeats this caveat at
the call site. (MySQL reports a real count for the identical call shape.
SQL Server usually does too, but not unconditionally, a bound procedure
using `SET NOCOUNT ON`, a common T-SQL pattern, also reports `-1`; this
is pre-existing general ADO.NET/T-SQL behavior, not something JauntyQ
can control for a procedure it doesn't author, so it isn't called out
per-call the way Postgres's unconditional case is.)

```sql
-- @call GetProductsByCategory
```

### `-- @mirrors`

Declares that this query's WHERE clause must match another query's, and makes
the build check it (`JNT8011`). The case it exists for is a paginated list and
the count that sizes its pager: when one gains a filter the other lacks, the
page contents stay correct while the total is wrong, and nothing surfaces it.

```sql
-- @mirrors ListPage
SELECT count(*) AS total
FROM bookmarks
WHERE bookmarks.user_id = @userId AND bookmarks.deleted_at IS NULL
```

The value is a method name resolved across the whole query corpus, or a
qualified `Entity.Method` when two entities have a method of the same name.
Put it on either side of the pair; only the file that carries it is checked, so
one directive per pair is enough.

**What is compared.** The WHERE clause of the statement **and of every CTE
body**, unioned, split at top-level `AND` into atoms and compared as a set.
Column references are resolved against the schema first, so `b.user_id` and a
bare `user_id` match; keyword casing and atom order do not matter. The CTE union
is what makes the idiomatic paginate-inside-a-CTE list comparable with a flat
count. `ORDER BY`, projection and `LIMIT`/`OFFSET` are **not** compared, a
count query has none of them.

**Deliberate conservatisms.** A top-level `OR` makes the whole clause one atom
rather than being split into conjuncts; `BETWEEN`'s own `AND` is not a splitter;
and the comparison is textual after resolution, so two predicates that mean the
same thing written differently (`a >= @x` vs `@x <= a`) read as drift. Write the
mirrored clause the same way on both sides, or drop the directive.

**It reports rather than guesses.** A pairing it cannot compare, an unknown or
ambiguous target, a target that failed to compile, or a WHERE column that does
not resolve to a schema column (a CTE virtual column, say), is `JNT3010`, not
silence. Falling back to raw text would invent drift out of a spelling
difference, and one false positive kills an opt-in check.

### `-- @allow-unindexed`

Accepts this query's unindexed filter columns deliberately, suppressing
`JNT8004` for this query and nothing else. A reason is required.

```sql
-- @allow-unindexed status index ships in migration 0042
SELECT messages.id, messages.subject
FROM messages
WHERE messages.status = @status
```

**Why it exists.** `JNT8004` reads the schema snapshot, and the snapshot is
advanced by running a migration against a live database and re-pulling. Without
this directive the only way past the rule is to change the world: an ordinary
`WHERE status = @status` stops the build until someone applies a migration, and
a fresh clone cannot build a branch whose migration has not been applied
anywhere. The rule is right; needing a database to satisfy it is not.

**The reason is mandatory.** A bare `-- @allow-unindexed` suppresses nothing and
is reported as `JNT3008`, like every other value-taking directive written bare.
The reason is the whole point: it puts the decision in the query file, where it
shows up in the diff and can be argued with at review time. A suppression whose
justification is not written down is a `NoWarn` entry with extra steps.

**It suppresses only `JNT8004`.** Every other diagnostic the query would raise
still fires. This is an accepted-scan marker, not a silencer.

**A suppression that suppresses nothing is reported** (`JNT8012`). Once the
index lands, the directive stops hiding an accepted scan and starts hiding a
future regression, so the build asks for it to be removed. Any single suppressed
column keeps the directive live: a query filtering on one indexed and one
unindexed column is correct use and stays quiet.

**Accepting a scan in an auto-CRUD query.** Auto-CRUD synthesizes a
`GetBy<Fk>` loader for every foreign-key column, and on engines that do not
index foreign keys automatically (SQLite, unlike InnoDB) those loaders scan. The
directive needs a file, and a synthesized query has none, so `JNT8004` on a
generated method names the file that would claim its slot:

```
warning JNT8004: No index covers address.city_id used as a filter/join key: this
query scans. Add an index or filter on an indexed column. 'Address.GetByCityId'
is generated by auto-CRUD, so there is no file carrying it to annotate. To accept
this scan deliberately, create '<your query root>/Address/GetByCityId.sql'
containing '-- @allow-unindexed <reason>' followed by: SELECT address_id, ...
FROM address WHERE address.city_id = @city_id -- a hand-written file always
overrides the generated method of the same name. That file is then yours: it no
longer tracks schema changes, ...
```

(The real message never elides the SQL, the full column list is what makes it
paste-able. It is condensed here only for the page.)

Creating that file overrides the generated method: a hand-written `.sql` always
wins the `entity.method` slot, and the directive then applies normally, `JNT3008`
and `JNT8012` included.

**The copy stops tracking the snapshot.** Once the file exists it is yours: a
column added upstream will not appear in its `SELECT`, and no diagnostic says
so, `JNT8012` wakes when the *scan* stops being real (someone indexes the
column), not when the projection drifts. For the audience this route is aimed
at, an upstream-owned schema nobody in the building controls, drift is the
normal condition rather than the edge case, so this is the cost of the
acceptance and not a footnote to it.
`<JauntyQAutoCrud>false</JauntyQAutoCrud>` is the blunt alternative and removes
auto-CRUD for every table.

**Or accept it without taking ownership of the SQL.** The override file is the
right answer when you wanted to hand-write that query anyway; when you only
wanted the warning to stop, paying for it with a copy that no longer tracks the
snapshot is a poor trade. A `*.accept.json` sidecar accepts a generated scan per
column and keeps the generated loader, [the acceptance sidecar](configuration.md#the-acceptance-sidecar-acceptjson) has
the shape and the rules. The message above names it too. It covers **generated
queries only**: a hand-written query filtering an accepted column still raises
`JNT8004`, because that query has a file and the file can carry the directive.

### `-- @allow-sort`

Accepts this query's runtime sort deliberately, suppressing `JNT8007` for this
query and nothing else. A reason is required.

```sql
-- @allow-sort 20 rows a page, the sort never sees more than a screenful
SELECT articles.id, articles.title
FROM articles
ORDER BY articles.created_at DESC, articles.id
```

**Why it is a separate directive.** `JNT8007` is the sort half of the same
question `-- @allow-unindexed` answers for filters, and the obvious move is to
let one directive cover both. It is the wrong move, and the reason is the
`JNT8012` remedy: *remove the directive*. Under a single shared directive that
advice goes wrong the moment a query has both an unindexed filter and an
unindexed sort and only one of them gets an index, the directive still
suppresses the other, so `JNT8012` never fires and the stale half lives on
forever. Two directives each expire on their own evidence.

Everything else matches its sibling exactly. A bare `-- @allow-sort` suppresses
nothing and is reported as `JNT3008`. Every other diagnostic the query raises
still fires, `JNT8009` and `JNT8010` included, accepting a sort is not
accepting an unstable one. Once an index can serve the sort, the directive is
reported as unnecessary (`JNT8012`), naming itself so a query carrying both
directives says which one to remove.

**What "an index can serve the sort" means** is the same predicate `JNT8004`
uses (`IsColumnIndexSupported`): the column is the primary key, or leads some
index, or sits in a composite index every more-leading column of which this
query constrains with a bound parameter. Note *constrains*, not *filters by
equality*, the set that predicate consults is every bound filter/join column
regardless of comparison operator, so `WHERE author_id > @since ORDER BY
created_at` against an index on `(author_id, created_at)` is treated as served.
A range on the leading column does not in fact deliver the trailing column in
order, so that case is a known false negative rather than a guarantee.

**Each `ORDER BY` item is tested on its own.** There is no multi-column
prefix logic: `ORDER BY a, b` against an index on `(a, b)` clears `a`, and
clears `b` only if `a` is also constrained by a parameter, or `b` is the
primary key. A two-column sort whose tiebreak is the primary key, the common
`ORDER BY created_at DESC, id` shape, therefore passes on the tiebreak's own
merit, not because the index covers the pair.

**Sort direction is invisible to this check.** `IndexSchema.Columns` is a list
of names with no ASC/DESC, and the parser discards the direction of each
`ORDER BY` item, so `JNT8007` cannot tell `(a, b)` from `(a DESC, b)`. The
engine can: PostgreSQL serves `ORDER BY a DESC, b` from `(a, b)` with a
backward scan plus an incremental sort for the tiebreak, and from `(a DESC, b)`
with no sort at all. When ties on `a` are common that difference is real and no
JauntyQ diagnostic will point at it.

### `-- @allow-n-plus-one`

Accepts this query's N+1 access pattern deliberately, suppressing `JNT8008` for
this query and nothing else. A reason is required.

```sql
-- @allow-n-plus-one one customer at a time on the service desk screen, never called per row of a list
SELECT rental.rental_id, rental.rental_date
FROM rental
WHERE rental.customer_id = @CustomerId
```

**Why it exists.** Unlike its siblings, `JNT8008` already has a suppression
route: it is an ordinary Roslyn diagnostic, so `<NoWarn>$(NoWarn);JNT8008</NoWarn>`
or `dotnet_diagnostic.JNT8008.severity = none` turns it off. That route is
project- or file-wide and carries no reason in the diff, which is the wrong
shape for this rule: JNT8008 is a corpus-level heuristic, so the query it
misjudges is usually one query among many the rule gets right, and switching the
whole code off to accept that one takes the rest of the corpus down with it.
This directive is the per-query alternative, with the justification recorded
where the decision was made.

**The reason is mandatory.** A bare `-- @allow-n-plus-one` suppresses nothing and
is reported as `JNT3008`, like every other value-taking directive written bare.
The reason is the whole point: it puts the decision in the query file, where it
shows up in the diff and can be argued with at review time. A suppression whose
justification is not written down is a `NoWarn` entry with extra steps.

**It suppresses only `JNT8008`.** Every other diagnostic the query would raise
still fires, `JNT8004` included, accepting a per-row lookup is not accepting a
scan on top of it.

**A suppression that suppresses nothing is reported** (`JNT8013`, not `JNT8012`,
which is its siblings' code). Once the pairing that justified the directive is
gone, because the parent-collection query was deleted or the child lookup was
made set-based, the directive stops hiding an accepted pattern and starts hiding
a future one, so the build asks for it to be removed. The message repeats the
stated reason.

## Related diagnostics

`JNT3003` (invalid directive combination), `JNT7001` (identity unavailable),
`JNT7002` (construct unavailable on dialect, `-- @proc` outside `sqlserver`),
`JNT4003` (parameter type unresolved), `JNT2004` (illegal identifier),
`JNT2005` (procedure not found), `JNT3006` (unknown `@type` alias),
`JNT8004` (unindexed filter column), `JNT8012` (unnecessary unindexed
acceptance), `JNT8013` (unnecessary N+1 acceptance). Full list in
[diagnostics](diagnostics.md).
