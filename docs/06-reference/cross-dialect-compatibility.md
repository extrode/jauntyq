# Cross-dialect compatibility

JauntyQ generates for one dialect at a time, and the same `.sql` file is meant to mean the same
thing on every engine it supports. This page states where that holds and where it does not.

It is not a summary written by hand. Every entry below is produced by
`samples/JauntyQ.Conduit.Differential.Tests`, which runs one query corpus against SQLite,
PostgreSQL, MySQL, MariaDB and SQL Server and compares each engine's rows to the others'. Any
difference the suite finds that is not listed here fails the build.

## What the suite compares

The Conduit corpus: 28 hand-written queries plus the auto-CRUD surface generated over the same
seven tables, reads, an ordered script of inserts, updates and deletes, then every read again
against the changed data. Results are compared engine-to-engine, never against a hand-written
expected value, so no single engine's behavior is treated as the reference.

## Differences the suite erases

These are compared as equal. Each is a claim that the difference is representational and carries
no meaning; the suite's own tests pin that it does not erase anything beyond them.

| Erased | Why it carries no meaning |
|---|---|
| Integer width and signedness | `COUNT(*)` returns `ulong` on MySQL, `long` on PostgreSQL and SQLite. The number is the result; its CLR width is a provider choice. |
| Trailing zeros on a decimal | `1.10` from `decimal(10,2)` and `1.1` from `decimal(10,1)` are the same number. `1.10` and `1.2` stay different. |
| Boolean representation | SQLite and SQL Server return 0/1 where PostgreSQL returns `bool`. |
| `DateTime.Kind` | Npgsql returns `Utc`, SqlClient `Unspecified`, for the same instant. |
| Sub-second precision | Compared to whole seconds, the resolution the corpus schema stores. |
| Trailing spaces on a string | `CHAR(n)` pads to width on SQL Server and MySQL; `VARCHAR` does not. Leading spaces are data and are kept. |
| Row order without `ORDER BY` | No engine promises an order for an unordered `SELECT`, so those results compare as multisets. A query that does order compares in order. |

Anything else, a differing row count, a differing value, a null where another engine returned a
value, a differing column set, or an error one engine raises and another does not, is a
difference, and either appears in the table below or fails the suite.

## Known differences

| Query | Engines | Difference | Reason |
|---|---|---|---|
| `Articles/GetFiltered` | SQL Server vs SQLite, PostgreSQL, MySQL, MariaDB | A page size of zero raises an error instead of returning no rows | SQLite, PostgreSQL, MySQL and MariaDB accept `LIMIT 0`. SQL Server's `OFFSET`/`FETCH` grammar requires `FETCH NEXT` to be greater than zero and raises *"The number of rows provided for a FETCH clause must be greater then zero"*. JauntyQ emits the paging clause each dialect requires and cannot reconcile the two without injecting a runtime guard into every paged query. **Callers targeting SQL Server must not pass a page size of 0.** Paging past the end of the result set returns no rows on all five. |

## Where the engines differ by design

The table above answers "does JauntyQ generate code that behaves the same everywhere". This
section answers the other half: **which SQL constructs mean different things on different
engines**, whoever writes them. These are facts about the engines, measured on the versions the
suite runs, SQLite in-process, `postgres:16-alpine`, MySQL, MariaDB and SQL Server, not
statements about generated code.

Each row was produced by running the construct on all five engines and recording which of them
returned the same result. A row where every engine agreed is not listed, because it is not a
difference; two categories were probed and dropped for that reason, noted at the end.

### Ordering and comparison

| Construct | Agree | Differ | What happens | Write instead |
|---|---|---|---|---|
| `ORDER BY v ASC` with NULLs | SQLite, MySQL, MariaDB, SQL Server | PostgreSQL | PostgreSQL sorts NULL as the largest value, so it comes last ascending. The other four put it first. | `NULLS FIRST` / `NULLS LAST` where available, else `ORDER BY CASE WHEN v IS NULL THEN 1 ELSE 0 END, v`. |
| `ORDER BY v DESC` with NULLs | SQLite, MySQL, MariaDB, SQL Server | PostgreSQL | The same rule from the other end: NULL first on PostgreSQL, last elsewhere. | As above. A descending paged query returns different first-page rows across this split. |
| `'ABC' = 'abc'` | MySQL, MariaDB, SQL Server | PostgreSQL, SQLite | The first three use a case-insensitive collation by default and call these equal. | `LOWER(a) = LOWER(b)`, or declare the collation. SQL Server's default is an install-time choice, so two deployments can differ. |
| `'a' = 'a '` | MariaDB, SQL Server | SQLite, PostgreSQL, MySQL | PAD SPACE collations ignore trailing spaces. MariaDB's default is PAD SPACE; **MySQL 8's is not**, the one measured case where the two split. | `TRIM(a) = TRIM(b)`, or trim on write. A text uniqueness constraint means different things across this split. |
| `ORDER BY` on mixed case | MySQL, MariaDB, SQL Server | PostgreSQL, SQLite | Case-insensitive collations sort `a` before `B`; code-point ordering puts `B` first. PostgreSQL's side follows the database's `LC_COLLATE`, measured here against the image's C locale. | `ORDER BY LOWER(v)`. |

### Arithmetic and typing

| Construct | Agree | Differ | What happens | Write instead |
|---|---|---|---|---|
| `5 / 2` | SQLite, PostgreSQL, SQL Server | MySQL, MariaDB | MySQL and MariaDB always divide as decimals and return `2.5`; the rest return the integer `2`. The type differs as well as the value. | `DIV` on MySQL/MariaDB for integer division; `* 1.0` on the others for a fractional one. |
| `-5 / 2` | SQLite, PostgreSQL, SQL Server | MySQL, MariaDB | The same split. Every engine that *does* divide as integers truncates toward zero, so rounding direction is not a difference. | As above. |
| `1 / 0` | SQLite, MySQL, MariaDB | PostgreSQL, SQL Server | PostgreSQL raises SQLSTATE 22012 and SQL Server raises its divide-by-zero error; the other three **return NULL**, turning a broken calculation into a missing value. | `NULLIF(d, 0)`, NULL on all five, and says so on purpose. |
| `AVG(v)` over integers | SQLite, PostgreSQL, MySQL, MariaDB | SQL Server | SQL Server's `AVG` returns the argument's type, so an integer average truncates: `1.5` comes back as `1`. | `AVG(CAST(v AS float))` or `AVG(v * 1.0)`. |

### Constructs with no portable spelling

These have no single form that runs everywhere, so each engine was given its own. The difference
is in what the engines then do.

| Construct | Agree | Differ | What happens | Write instead |
|---|---|---|---|---|
| Add one month to 31 January | PostgreSQL + SQL Server; MySQL + MariaDB | SQLite | Four engines clamp to 28 February. **SQLite's `date()` normalizes the overflow and returns 3 March**, a different month, with no error. (The two pairs differ only in returning a temporal value against text, which follows from each dialect's spelling.) | Do not add months in SQL if the result must match. Compute the clamped date in the application. |
| Join text to a number | SQLite, PostgreSQL, MySQL, MariaDB | SQL Server | SQL Server's `+` is also addition, so it tries to convert and fails. Loud, and therefore the safest case here. | `'a' + CAST(1 AS varchar(11))` on SQL Server, or `CONCAT`. |
| `SELECT TRUE` | SQLite, MySQL, MariaDB | PostgreSQL; SQL Server | PostgreSQL returns a real `bool`; three return the integer `1`; SQL Server has no boolean literal and reads `TRUE` as a column name, then fails. | Write `1` and `0`. |
| `SELECT (1 = 1)` | SQLite, MySQL, MariaDB | PostgreSQL; SQL Server | The same three-way split, SQL Server has no boolean value type, so a comparison cannot appear in a select list. | `CASE WHEN 1 = 1 THEN 1 ELSE 0 END`. |

### Probed and found identical

Recorded because a measured agreement is worth as much as a measured difference, and because the
suite does not keep passing no-op cases:

- **`COUNT(*)` and `MAX(v)` over an empty set**, all five return `0` and NULL.
- **`SUM(v)` over an empty set**, all five return NULL, not `0`.
- **Concatenating NULL**, all five return NULL (using each dialect's own concatenation form).

Note the second: `SUM` over no rows is NULL everywhere, so `COALESCE(SUM(v), 0)` is needed on
every engine, not just some.

## Reading this table

An entry is a permanent statement about the engines, not a known bug. Where JauntyQ could make
the engines agree, it does, and no entry appears. An entry means the difference is in the engine
and reconciling it would cost more than it is worth, the reason column says what that cost is.

Entries do not outlive the behavior they describe: if a divergence stops occurring on a run
where its query and engines were both exercised, the suite fails on the stale entry.

## Running it

    dotnet test samples/JauntyQ.Conduit.Differential.Tests

With Docker absent the cross-engine tests skip and say which engines were missing; the harness's
own unit tests and the SQLite-only corpus execution still run. Fewer than two engines is a skip,
not a pass, one engine cannot disagree with itself. The suite runs in the nightly
`full-suite` job, which has all five.
