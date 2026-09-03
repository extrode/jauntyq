# CLI reference: `jauntyq`

The `jauntyq` command-line tool extracts a database schema into a committed
`.schema.json` snapshot (`schema pull`), checks a snapshot against a live
database (`schema verify`), reports the blast radius of a pending migration
set offline (`migrate impact`), and runs the query corpus through the
database's estimate-only EXPLAIN (`explain`). The generator itself never
connects to a database; only `schema pull`/`schema verify`/`explain` do, `migrate impact` and `registry` are fully offline.

## Two packages, one command

| Package | Feed | Verbs | Install |
|---|---|---|---|
| `Extrode.JauntyQ.Cli` | NuGet.org | `schema pull` | `dotnet tool install --global Extrode.JauntyQ.Cli` |
| `Extrode.JauntyQ.Cli.Premium` | a private feed for subscribers | everything in `Extrode.JauntyQ.Cli` plus `schema verify`, `usage export`, `migrate impact`, `explain`, `registry`, `activate`, `license` | `dotnet tool install --global Extrode.JauntyQ.Cli.Premium --add-source <feed>` |

Both install the same command, `jauntyq`. The premium package replaces the
free one; never install both. `schema pull` is implemented once, in the shared
`Extrode.JauntyQ.Cli.Core` library, so it behaves identically in either. A premium verb
typed at the free tool exits `3` and prints the install line; it never reads
its inputs.

In this repository the free tool runs via `dotnet run`:

```bash
dotnet run --project src/Extrode.JauntyQ.Cli -- schema pull --provider sqlserver ...
```

The premium verbs come from an installed `Extrode.JauntyQ.Cli.Premium`, which is
built elsewhere; there is no project here to run them from.

The examples below use `jauntyq`.

## Synopsis

```
jauntyq schema pull    --provider <provider> (--connection-env <VAR> | --connection <connstr>) [--output <path>]
jauntyq schema verify  --provider <provider> (--connection-env <VAR> | --connection <connstr>) [--output <snapshot-path>] [--format text|json] [--fail-on any|breaking] [--service <id> [--registry <path>]] [--usage <path>]
jauntyq usage export   [--queries <dir>] [--snapshot <path>] [--output <file>] [--no-auto-crud]
jauntyq migrate impact [--snapshot <path>] [--migrations <dir>] [--queries <dir>] [--format text|json] [--fail-on breaking|risky] [--fail-on-warnings]
jauntyq explain        --provider <provider> [--connection-env <VAR> | --connection <connstr>] [--queries <dir>] [--max <n>] [--format text|json] [--fail-on high|informational] [--cache-dir <dir>]
jauntyq registry resolve    --schema <id> [--registry <path>] [--format text|json]
jauntyq registry dependents (--schema <id> | --table <schemaId.table>) [--registry <path>] [--format text|json]
jauntyq registry validate   [--registry <path>] [--format text|json]
jauntyq registry list       [--registry <path>] [--format text|json]
jauntyq activate       --license <path>
jauntyq license status
jauntyq license deactivate
```

Top-level commands are `schema` (subcommands `pull`, `verify`), `usage`
(subcommand `export`), `migrate` (subcommand `impact`), `explain`, `registry`
(subcommands `resolve`, `dependents`, `validate`, `list`), `activate`, and
`license` (subcommands `status`, `deactivate`).

**Premium gating.** `migrate impact`, `schema verify`, `explain`, `registry`
(every subcommand) and `usage export` are premium features: they require an
entitling license and otherwise exit `3` without doing the work;
`schema verify --service` requires a further, separate entitlement on top of
base `schema verify` (see
[License and entitlements](#license-and-entitlements)). `schema pull`,
`activate`, `license`, and the core source generator are never gated.

Being **offline** and being **ungated** are independent: `migrate impact`,
`registry` and `usage export` need no database connection and are still gated.

## Commands

### `schema pull`

Connects to the database, extracts tables, columns, foreign keys, stored
procedures, and sequences, and writes the snapshot to `--output`. Creates the
output directory if it does not exist. On success prints the table and
foreign-key counts and `Schema written to <output>`.

### `schema verify`

Loads the snapshot at `--output`, re-extracts the live schema, and compares
them. Use it in CI or locally to catch drift between the committed snapshot and
the real database.

Each difference is classified **breaking** or **compatible** by a fixed
structural rule set (independent of which queries reference the object):

| Change | Severity |
|---|---|
| Table or column missing from the live database | breaking |
| Column reorder / rename at a position (readers bind by ordinal) | breaking |
| Type change, length narrowing, precision/scale change | breaking |
| `null` → `not null`, primary-key / identity / row-version / unicode change | breaking |
| Column added, length widening, `not null` → `null` | compatible |
| Table present live but absent from the snapshot | informational (never fails) |

**Options**

| Option | Default | Meaning |
|---|---|---|
| `--format text\|json` | `text` | Human-readable report or the machine-readable JSON report (`{ "drifts": [ { "kind", "objectPath", "severity", "message", ... } ], "hasBreaking" }`), with severities as strings. A scoped run (`--service`) adds a `"scope"` block: `{ "serviceId", "isEmpty", "comparedTables": [ { "name", "ownership" } ] }`. |
| `--fail-on any\|breaking` | `any` | `any` (default) exits `2` on any difference, preserving the prior behavior, while a brand-new table stays informational. `breaking` exits `2` only when breaking drift is present, letting compatible drift pass. |
| `--service <id>` |, | **Per-service scoping.** Verify only the slice the named service declares in the registry (its `consumes` tables): drift inside the slice is classified by the same rules; changes outside it produce no drift of any severity and cannot fail this service's gate. The report names the service and the compared tables (no silent scope). Requires the separate `per-service-contracts` entitlement in addition to base `contract-testing` (exit `3` if missing, before any comparison runs). |
| `--registry <path>` | `./jaunty.registry.json` | The registry to derive the `--service` scope from. Only meaningful with `--service`. |
| `--usage <path>` |, | **Usage-aware severity (opt-in, downgrade-only).** Reclassifies the structural report against the referenced-objects manifest written by [`usage export`](#usage-export): breaking drift on an object **no generated query references** becomes **informational**, still listed, annotated with the reason and its original structural severity, but never gate-failing (neither under `breaking` nor under the strict `any` default, like a brand-new table). Referenced objects always keep their structural severity; any uncertainty (unqualified references, unresolved queries, a referenced column on a dropped/reordered table) keeps the structural verdict. Composes with `--service`: scope filters what is compared, usage adjusts the severity of the remainder. The JSON report adds a `"usage"` block (`{ "manifestSnapshotHash", "verifiedSnapshotHash", "stale", "downgradedCount", "unresolvedQueryCount" }`) and per-drift `"downgraded"`/`"downgradeReason"`/`"structuralSeverity"` fields. If the manifest was built against a different snapshot, a staleness warning is printed on stderr (exit code unaffected, downgrade-only semantics cannot become unsafe). A missing/unreadable manifest exits `1`, never a silent structural fallback. |

- Exit `0`, snapshot matches, or only differences below the `--fail-on`
  threshold (e.g. compatible drift under `--fail-on breaking`). With
  `--service`, this includes any change outside the service's slice, and the
  vacuous case where the service declares no `consumes` (printed as an explicit
  "no scope declared" note, never as a verified pass).
- Exit `2`, a difference at or above the threshold; each is listed on stderr
  with its severity. With `--service`, only in-slice differences count.
- Exit `1`, with `--service`: a scope/registry error (registry unreadable,
  unknown service id, or a scope table the snapshot does not declare), deliberately distinct from both a pass and drift.

For a test-embeddable equivalent, asserting a live database matches the snapshot
from inside your own xUnit/NUnit/MSTest suite, use the
[`Extrode.JauntyQ.Schema.Contract`](../03-guides/contract-testing.md) package, which
shares the exact same classifier. The same package also offers
[boot-time verification](../03-guides/startup-verification.md)
(`StartupSchemaGuard`) so an app can fail fast at startup when the database it
actually connects to has drifted.

### `usage export`

Writes the **usage manifest** (`jaunty.usage.json`) consumed by
`schema verify --usage` and `SchemaContract.WithUsage(...)`: the union of every
table and `table.column` the project's generated queries reference, hand-written
`.sql` files **plus** the auto-CRUD synthetics the generator emits, resolved by
the same offline analysis that powers `migrate impact`. No database connection.
Gated on `contract-testing`, the same entitlement as the verb it feeds,
`schema verify --usage`, since a manifest is useless without it.

The manifest also records the hash of the snapshot it was built against, so a
verification against a different snapshot can warn that usage may be stale.
Commit the manifest and regenerate it in CI whenever queries or the snapshot
change.

Uncertainty is encoded conservatively (downgrades must never be unsafe): an
unqualified column reference in a multi-table query marks every in-scope table's
columns potentially referenced (`table.*`); a query that is invalid against the
snapshot or uses unsupported constructs marks all its tables `table.*`; a query
that cannot be parsed/attributed at all is recorded under `unresolvedQueries`,
which disables **all** downgrades until it is fixed (the export prints a warning
listing each one).

**Options**

| Option | Default | Meaning |
|---|---|---|
| `--queries <dir>` |, | Directory of hand-written `.sql` query files (searched recursively). Omit to cover only auto-CRUD synthetics. |
| `--snapshot <path>` | `schema/jaunty.schema.json` | Committed snapshot: seeds the auto-CRUD synthetics and the staleness hash. |
| `--output <file>` | `jaunty.usage.json` | Where to write the manifest. |
| `--no-auto-crud` |, | Exclude the auto-CRUD synthetics, only for projects that set `<JauntyQAutoCrud>false</JauntyQAutoCrud>` (otherwise the generated CRUD really does reference every table). |

- Exit `0`, manifest written.
- Exit `1`, argument or I/O error (e.g. snapshot not found).
- Exit `3`, not entitled (`contract-testing`), checked before any work.

**Manifest shape**:

```json
{
  "snapshotHash": "sha256:9f2c…",
  "tables": ["customers", "orders"],
  "columns": ["customers.*", "orders.order_id", "orders.total"],
  "unresolvedQueries": []
}
```

### `migrate impact`

Reports how a pending `db/migrations/*.sql` set affects your queries, fully
offline (snapshot + migration files + query files; no database connection). Each
hand-written query and each auto-CRUD synthetic is classified as exactly one of:

- **SAFE**, references no object the migration changed.
- **RISKY**, still compiles, but a referenced column changed type, nullability,
  length, or precision (or a referenced table was changed by a statement the
  simulator could not model). Reported at build time as the `JNT9004` warning.
- **BREAKING**, the migration removes a referenced table or column, so the query
  no longer compiles. These already fail the build through the existing `JNT2xxx`
  validation errors; `migrate impact` names and groups them.

A query already invalid against the baseline snapshot is **not** attributed to
the migration (reported SAFE, no reasons).

**Options**

| Option | Default | Meaning |
|---|---|---|
| `--snapshot <path>` | `schema/jaunty.schema.json` | Committed baseline snapshot. |
| `--migrations <dir>` | `db/migrations` | Directory of pending `*.sql` migrations (applied in filename order). |
| `--queries <dir>` |, | Directory of hand-written `.sql` query files (searched recursively). Omit to classify only auto-CRUD synthetics. |
| `--format text\|json` | `text` | Human-readable report or the machine-readable JSON shape below. |
| `--fail-on breaking\|risky` |, | Exit non-zero when an entry at or above the threshold exists. Omit for report-only (always exit 0). |
| `--fail-on-warnings` | off | Also exit `2` when the run produced any warning. A warning always means the analyzed corpus was narrower than the invocation asked for, a `--migrations` or `--queries` directory that does not exist, or a migration statement the simulator could not apply, so the report is a verdict over less than it appears to cover. Independent of `--fail-on`: the two gates are ORed, and classification is unaffected. |

- Exit `0`, report-only, or nothing reached the `--fail-on` threshold.
- Exit `2`, an entry at or above `--fail-on` exists, or `--fail-on-warnings` is set and the run warned.
- Exit `1`, usage or I/O error (e.g. snapshot not found).

**JSON shape** (`--format json`):

```json
{
  "baselineId": "schema/jaunty.schema.json",
  "migrationSet": ["0007_drop_legacy_col.sql"],
  "entries": [
    {
      "queryFile": "Queries/GetUser.sql",
      "entityMethod": "User.GetById",
      "classification": "BREAKING",
      "reasons": [
        { "schemaObject": "users.legacy_flag", "changeKind": "removed", "effect": "column referenced by the query no longer exists" }
      ]
    }
  ]
}
```

**MVP limitations**

- JauntyQ has no rename tracking: a rename is a drop + an add, so queries on the
  old name are BREAKING and the new column is simply available.
- A committed JSON snapshot baseline is the supported path; DDL-as-schema-source
  projects are analyzed against the DDL-built base.
- Attribution is per-migration-set (cumulative effective schema), not per file.
- Index DDL (`CREATE`/`DROP INDEX`) is not modeled at all: `MigrationParser`
  classifies it `Ignored`, so it produces no diagnostic and no RISKY
  classification. An index drop that breaks a query's performance assumptions
  is currently invisible to both `migrate impact` and the build.
- Entity naming uses the query file's immediate folder (or `Queries` at the
  root); structural `tables/`/`views/` prefixes are not special-cased.

### `explain`

Runs each `.sql` file under `--queries` through the target database's
**estimate-only** EXPLAIN, parses the plan, and reports access paths, planner
estimates, and severity-tagged plan problems per query file. Opt-in and
on-demand only, the source generator never runs this and a normal build is
unaffected. See the [live EXPLAIN guide](../03-guides/explain-analysis.md) for
the guardrails, heuristics, and optional MSBuild wiring.

- **Estimate-only, never executes**: Postgres `EXPLAIN (FORMAT JSON)`, SQLite
  `EXPLAIN QUERY PLAN`, MySQL `EXPLAIN FORMAT=JSON`, SQL Server estimated
  `SHOWPLAN_XML`. Write statements are skipped with a note.
- **Capped**: at most `--max` live EXPLAIN calls per run; the remainder is
  listed as `not analyzed (cap reached)`.
- **Cached**: `.jaunty/explain-cache.json`, keyed by dialect + normalized-SQL
  fingerprint + schema-snapshot fingerprint; cached queries do not consume the
  cap.
- **Never breaks**: unreachable database, auth failure, or an unexplainable
  query → per-query skip, exit `0`. No connection string configured → clean
  no-op, exit `0`.

**Options**

| Option | Default | Meaning |
|---|---|---|
| `--provider <provider>` | required | Database provider / dialect (same values as `schema pull`). |
| `--connection-env <VAR>` / `--connection` |, | Connection string source (same resolution as `schema pull`). **Unlike** `schema pull`, no connection is not an error: the run is a clean exit-`0` no-op. |
| `--queries <dir>` | `db/tables` | Directory of `.sql` query files (searched recursively). |
| `--max <n>` | `10` | Cap on live EXPLAIN calls per run. |
| `--format text\|json` | `text` | Human-readable report or the machine-readable JSON report (`{ "dialect", "cap", "results": [ { "queryFile", "status", "planSummary", "problems": [...] } ], "notes" }`). |
| `--fail-on high\|informational` |, | Exit `2` when a problem at or above the severity exists. `info` is accepted as an alias for `informational`. Omit for report-only (always exit `0`). |
| `--cache-dir <dir>` | `.jaunty` | Where `explain-cache.json` lives. |

- Exit `0`, report-only, nothing at/above `--fail-on`, or nothing analyzed
  (no/unreachable database).
- Exit `2`, a problem at or above `--fail-on` exists.
- Exit `1`, usage error (bad flag values, missing `--provider`).
- Exit `3`, not entitled (`live-explain`).

Severity heuristics (documented in the guide): sequential/full scan with est.
rows ≥ 10,000 → high; cost-dominant sort (≥ 50% of total plan cost) → high;
SQLite `USE TEMP B-TREE` → high; smaller scans/sorts and estimate-less full
scans → informational. Costs are planner estimates from the connected database
at run time; thresholds compare within a run only.

### `registry resolve` / `dependents` / `validate` / `list`

Offline queries against a checked-in `jaunty.registry.json`, the central
schema-dependency registry naming schema snapshots and the services that
depend on them. Additive and opt-in: a project without a registry is
completely unaffected. Fully offline, but **gated on `contract-testing`**, the registry is part of the contract-testing family, not a free verb. See the
[schema registry guide](../03-guides/schema-registry.md) for the file format
and workflow.

| Subcommand | Meaning |
|---|---|
| `resolve --schema <id>` | Prints the schema's snapshot path (resolved relative to the registry file), dialect, and owner, resolve-by-name for builds/scripts. |
| `dependents --schema <id>` | Lists every service whose `dependsOn` names the schema. |
| `dependents --table <schemaId.table>` | Narrows to services whose `consumes` declares that table. A service that depends on the schema but declares **no** `consumes` list cannot be confirmed or ruled out at table granularity, so it is not counted as a dependent; it is listed separately under "N further service(s) … declare no consumes list, so their use of this table is unknown" (JSON: an `unknownUsage` array beside `dependents`). Both are omitted when there are none. |
| `validate` | Checks the whole graph: unique ids, snapshot exists within the registry tree, `dependsOn` resolvable, consumed tables present in the snapshot. Every problem is individually named. |
| `list` | Summarizes the registry's schemas and services. |

**Options** (all subcommands)

| Option | Default | Meaning |
|---|---|---|
| `--registry <path>` | `./jaunty.registry.json` | The registry file to query. |
| `--format text\|json` | `text` | Human-readable or machine-readable output. |

- Exit `0`, success (`validate`: no problems).
- Exit `1`, usage or error: missing registry file, unknown schema id, or a
  malformed `--table` reference.
- Exit `2`, `validate` found problems (each listed and named on stderr, e.g.
  `DanglingDependsOn: service 'billing' depends on undeclared schema 'inventory'.`).
- Exit `3`, not entitled (`contract-testing`), checked before the registry is
  even read, so an unlicensed run cannot be told apart from a missing registry
  by anything but the exit code.

Snapshot paths declared in the registry must be **relative to the registry
file** and stay inside its directory tree, rooted paths and `..` escapes are
rejected at load, mirroring the [`--output` constraints](#--output-constraints).

### `activate`

Verifies a signed `jaunty.license.json` offline against the tool's embedded
public key and installs it to the per-user location. Nothing is installed on
failure; the error distinguishes a bad signature, malformed JSON, a wrong
product, and an unrecognized license version.

```
jauntyq activate --license path/to/jaunty.license.json
```

- Exit `0`, verified and installed (prints tier, licensee, entitlements).
- Exit `1`, missing/unreadable file, malformed JSON, bad signature, wrong
  product, or unknown license version.

### `license status` / `license deactivate`

`license status` prints the active tier, licensee, seats, term dates,
entitlements, and whether the license has lapsed, fully offline, no network.
`license deactivate` removes the installed license. Both exit `0`.

### License and entitlements

`migrate impact` (entitlement `migration-impact`), `schema verify`
(entitlement `contract-testing`; `--service` additionally requires
`per-service-contracts`), `usage export` and every `registry` subcommand
(both `contract-testing`, gated 2026-08-01 with the commercialization model, they predate it and were ungated by accident of shipping order), and `explain`
(entitlement `live-explain`) each consult a single offline entitlement gate
before doing premium work:

- **Entitled**, runs normally.
- **Lapsed** (term ended), still runs and prints a non-blocking warning; the
  perpetual-use grant means a lapse never blocks a feature you were entitled to.
- **Not entitled** (no license, or the license does not grant the feature), prints the entitlement-required message and exits `3`, doing no premium work.

The **core source generator, JNT1xxx–JNT7xxx validation, and the runtime are
never gated**: generated output and diagnostics are identical with or without a
license. This is entitlement gating for a source-viewable product, not DRM. In
CI, set `JAUNTYQ_LICENSE` to a license file path instead of running `activate`.
Feature keys: `migration-impact`, `contract-testing`, `per-service-contracts`,
`live-explain`, and the reserved `deep-query-analysis` (defined for a future,
not-yet-built feature, it does not gate the JNT8006/JNT8007 diagnostics
already shipped in the generator, which are core and never gated). See the
[licensing and activation guide](../03-guides/licensing-and-activation.md).

## Options

These apply to `schema pull` and `schema verify`.

| Option | Required | Default | Meaning |
|---|---|---|---|
| `--provider <provider>` | Yes |, | Database provider / dialect. See values below. |
| `--connection-env <VAR>` | See note |, | Name of an environment variable holding the connection string. **Preferred.** |
| `--connection <connstr>` | See note |, | Inline connection string. Avoid: visible in process lists, shell history, and CI logs. |
| `--output <path>` | No | `schema/jaunty.schema.json` | Snapshot path, **relative** to the current directory. For `verify`, the snapshot to compare against. |

**Connection note:** a connection string must come from exactly one source.
Resolution order, most secure first:

1. `--connection-env <VAR>`, read from the named environment variable.
2. `JAUNTYQ_CONNECTION`, used when neither flag is given.
3. `--connection <connstr>`, inline, least secure.

If none is present the command errors and prints usage.

### `--output` constraints

The path must be **relative** and must stay inside the project directory. A
rooted path (`C:\...`, `/etc/...`) or one that escapes the tree with `..` is
rejected. The convention is `db/schema/jaunty.schema.json`; the built-in
default (`schema/jaunty.schema.json`) is used only when `--output` is omitted.

## Providers

| `--provider` value | Aliases | Driver |
|---|---|---|
| `sqlserver` | `mssql` | Microsoft.Data.SqlClient |
| `postgres` | `postgresql` | Npgsql |
| `mysql` | `mariadb` | MySqlConnector |
| `sqlite` |, | Microsoft.Data.Sqlite |

Provider matching is case-insensitive. An unrecognised value prints
`Error: unknown provider '<value>'. Supported: postgres, sqlserver, mysql, mariadb, sqlite.`
and exits 1. MariaDB is wire/SQL-compatible with MySQL for everything JauntyQ
emits, so `--provider mariadb` maps to the `mysql` dialect in the snapshot.

## Environment variables

| Variable | Effect |
|---|---|
| `JAUNTYQ_CONNECTION` | Default connection string when neither `--connection-env` nor `--connection` is given. |
| `JAUNTYQ_VERBOSE` | Set to `1` to print full exception detail on failure. Otherwise errors are anonymized to avoid leaking connection-string secrets into logs. |
| `JAUNTYQ_LICENSE` | Path to a `jaunty.license.json` used for premium-feature entitlement checks. Overrides the per-user install; intended for CI so it need not run `activate` interactively. |

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. `pull`: snapshot written. `verify`: snapshot matches (with `--usage`, this includes runs whose only drift was downgraded to informational). `usage export`: manifest written. `activate`/`license`: completed. |
| `1` | Usage or runtime error, bad/missing arguments, unknown provider, no connection string, invalid `--output` path, an extraction/connection failure, an activation failure (bad signature, malformed license, wrong product, unknown version), or a `registry` error (missing registry file, unknown schema id). |
| `2` | `verify`: schema drift detected (differences listed on stderr). `migrate impact`: an entry at or above `--fail-on` exists, or `--fail-on-warnings` is set and the run warned. `explain`: a plan problem at or above `--fail-on` exists. `registry validate`: the registry has problems (each named on stderr). |
| `3` | Reserved: a premium command (`migrate impact`, `schema verify`, `explain`, `registry`, `usage export`) was run without an entitling license, or any premium verb was run at the free `Extrode.JauntyQ.Cli` tool, which prints the `Extrode.JauntyQ.Cli.Premium` install line instead. Distinct from `1`/`2` so scripts can tell "not licensed" from "found problems." A lapsed license does **not** produce this code, it runs with a warning. |

## Examples

Pull with the connection string from an environment variable (recommended):

```bash
export MYDB_CONN='Host=localhost;Database=app;Username=app;Password=...'
jauntyq schema pull --provider postgres --connection-env MYDB_CONN \
  --output db/schema/jaunty.schema.json
```

Verify in CI, failing the job on drift:

```bash
jauntyq schema verify --provider sqlserver --connection-env CI_DB_CONN \
  --output db/schema/jaunty.schema.json
# exit 0 = match, exit 2 = drift
```

Optional MSBuild wiring that verifies before every build when a connection is
available (CI often has none, which is why the snapshot is committed):

```xml
<Target Name="VerifyJauntySchema" BeforeTargets="Build"
        Condition="'$(JAUNTY_VERIFY_CONN)' != ''">
  <Exec Command="jauntyq schema verify --provider sqlserver --connection-env JAUNTY_VERIFY_CONN --output db/schema/jaunty.schema.json" />
</Target>
```

See the [schema snapshot format](schema-snapshot-format.md) for the structure
of the file this command writes.
