# Live EXPLAIN plan analysis

JauntyQ's static performance diagnostics (JNT8001–JNT8011) reason about queries
against the committed snapshot, entirely offline. They cannot see what the
database's **actual planner** will do: row-count estimates, chosen access paths,
sequential scans on large tables, or expensive sorts. The database already knows
all of this via `EXPLAIN`.

**`jauntyq explain`** closes that gap: point it at a representative database and
it runs each `.sql` query in your corpus through the dialect's *estimate-only*
EXPLAIN, parses the plan, and reports plan-level problems by severity, attributed to the query file that produced them.

```bash
export DEV_DB='Host=localhost;Database=shop;Username=dev;Password=...'
jauntyq explain --provider postgres --connection-env DEV_DB --queries db/tables
```

```
jauntyq explain, postgres (estimated plans; costs are planner estimates from the connected database at run time)
Orders/GetByCustomer.sql: Seq Scan on orders [est. cost 23345]
  [high] sequential scan on 'orders' (est. 1,200,000 rows), consider an index covering the filtered column(s)
Articles/GetBySlug.sql: Index Scan on articles [est. cost 8.3]
1 analyzed, 1 cached, 0 skipped, 0 not analyzed (cap 10); 1 high / 0 informational problem(s).
```

This is a premium feature (entitlement `live-explain`); without an entitling
license the verb exits `3` and does nothing.

## The contract: opt-in, estimates only, never breaks a build

- **Never in the generator.** The Roslyn source generator stays fully offline;
  it never opens a database connection and a normal `dotnet build` is completely
  unaffected by this feature. `jauntyq explain` is a separate, on-demand CLI tool.
- **Estimate-only.** Every dialect uses the plan-without-executing variant:
  Postgres `EXPLAIN (FORMAT JSON)` (never `ANALYZE`), SQLite
  `EXPLAIN QUERY PLAN`, MySQL `EXPLAIN FORMAT=JSON`, SQL Server estimated
  `SET SHOWPLAN_XML ON`. No query runs; no data is read or mutated. Write
  statements (`INSERT`/`UPDATE`/`DELETE`/DDL, including data-modifying CTEs) are
  **skipped entirely**, safety over coverage.
- **Never breaks.** Report-only by default (exit `0`). An unreachable database,
  auth failure, or unexplainable query is a per-query *skip with a note*, never
  an error. With no connection string configured the run is a clean no-op, which is what makes the optional CI wiring below safe.
- **Capped.** At most `--max` (default `10`) live EXPLAIN calls per run; any
  remainder is listed explicitly as `not analyzed (cap reached)`, no silent
  truncation.
- **Cached.** Results are memoized in `.jaunty/explain-cache.json` (gitignored),
  keyed by *(dialect, normalized-SQL fingerprint, schema-snapshot fingerprint)*.
  Unchanged queries cost nothing on repeat runs, and cached queries do not
  consume the cap, successive runs walk further through a large corpus.
  Editing a query or re-pulling the snapshot invalidates exactly the affected
  entries.

## Parameters

EXPLAIN needs bindable placeholders, not real values. The analyzer scans each
query for `@Name` / `:Name` / `$Name` (and Postgres `$1` positional)
placeholders and binds representative **typed nulls**, plans are estimates, so
sample data is unnecessary.

## Severity heuristics

| Finding | Severity |
|---|---|
| Sequential/full table scan, est. rows ≥ threshold (default 10,000) | **high** |
| Sequential/full table scan below the threshold | informational |
| Full scan where the dialect exposes no row estimate (SQLite) | informational |
| Sort step whose est. cost is ≥ 50% of the plan's total cost | **high** |
| Sort step below the dominance ratio | informational |
| SQLite `USE TEMP B-TREE FOR ORDER BY/GROUP BY` (always means no supporting index) | **high** |
| Index-backed access (seeks, index/covering-index scans) | not flagged |

Costs and row counts are **planner estimates from the connected database at run
time**, they vary with statistics, data volume, and engine version. Thresholds
compare within a single run; do not treat costs as comparable across
environments.

## Failing a pipeline on purpose: `--fail-on`

By default the exit code is `0` no matter what the report says. To gate a
dedicated CI stage, opt in:

```bash
jauntyq explain --provider postgres --connection-env CI_EXPLAIN_DB \
  --queries db/tables --fail-on high
# exit 0 = nothing at/above the threshold; exit 2 = a high-severity plan problem
```

`--fail-on informational` fails on any finding; `--fail-on high` (recommended)
fails only on high-severity ones.

## Optional MSBuild wiring (off by default)

Exactly like the documented `schema verify` wiring: an explicit `Exec` target
**guarded by a connection variable**. When the variable is unset, the default
everywhere, including CI, the target does not run and the build is untouched.

```xml
<Target Name="ExplainJauntyQueries" AfterTargets="Build"
        Condition="'$(JAUNTY_EXPLAIN_CONN)' != ''">
  <!-- No fail-on switch: report-only, never fails the build. -->
  <Exec Command="jauntyq explain --provider postgres --connection-env JAUNTY_EXPLAIN_CONN --queries db/tables" />
</Target>
```

Even if the variable is set and the database is down, the command exits `0`
(clean skip), the build cannot break.

## What it will not do

- Run inside the Roslyn generator, ever (the generator has no connection code).
- Execute or mutate anything (`EXPLAIN ANALYZE` is deliberately unsupported).
- Explain write statements (skipped with a note).
- Prescribe DDL, it reports the problem and the hint, not a migration.

See the [CLI reference](../06-reference/cli.md) for the full option table and
exit codes.
