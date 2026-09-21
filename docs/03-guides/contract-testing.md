# Database contract testing

JauntyQ compiles your queries against `jaunty.schema.json`, a committed snapshot
of the database structure. Every generated reader is a promise about the live
database (for example, a column read with `GetInt16` because the snapshot says it
is `smallint`). That promise holds only while the live database still matches the
snapshot. When a migration renames a column, tightens nullability, or drops a
table and the snapshot is not re-pulled, the generated code keeps compiling but
starts failing at runtime.

**Contract testing** closes that gap: it reflects a live database, compares it to
the committed snapshot, and classifies every difference as **breaking** or
**compatible**, so drift fails CI before it reaches production.

There are two entry points, both driven by the same classifier:

- The **`Extrode.JauntyQ.Schema.Contract`** package, a test-embeddable assertion API you
  drop into your own xUnit / NUnit / MSTest suite.
- The **`jauntyq schema verify`** CLI command, for pipelines without a test host
  (see the [CLI reference](../06-reference/cli.md)).

## Installing the package

`Extrode.JauntyQ.Schema.Contract` ships on the private feed. Add it to your **test**
project (it carries the ADO.NET provider dependencies, so it is a test-time /
tooling package, not part of the zero-dependency core):

```xml
<PackageReference Include="Extrode.JauntyQ.Schema.Contract" Version="x.y.z" />
```

## Writing a contract test

```csharp
using Extrode.JauntyQ.Schema.Contract;
using Xunit;

public class SchemaContractTests
{
    [Fact]
    public async Task Database_matches_the_compiled_contract()
    {
        var connectionString = Environment.GetEnvironmentVariable("CONTRACT_DB")!;

        var result = await SchemaContract
            .Against(connectionString, ContractDialect.SqlServer)
            .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
            .VerifyAsync();

        result.AssertNoBreakingDrift();
    }
}
```

`AssertNoBreakingDrift()` throws a `SchemaContractException`, failing the test, when any breaking difference is present, and lists each offending object. A
compatible difference (an added table, a widened column, a loosened nullability)
does **not** fail the assertion; use `AssertNoDrift()` if you want the strict
"exact match" gate instead.

The dialect is one of `ContractDialect.SqlServer`, `Postgres`, `MySql`, `Sqlite`.

## The severity model

Severity is decided by a fixed structural rule set. It depends only on the shape
change, never on which queries reference the object (unless you opt into
[usage-aware severity](#usage-aware-severity-downgrading-drift-you-cannot-hit),
which may *downgrade*, never raise, severity for unreferenced objects):

| Change | Severity |
|---|---|
| Table or column missing from the live database | **Breaking** |
| Column reorder / rename at a position (readers bind by ordinal) | **Breaking** |
| Type change, length narrowing, precision/scale change | **Breaking** |
| `null` → `not null`, primary-key / identity / row-version / unicode change | **Breaking** |
| Column added, length widening, `not null` → `null` | Compatible |
| Table present live but absent from the snapshot | Compatible |

## Ignoring objects

Exclude tables or columns you do not want to gate on, a framework
migration-history table, an ops-managed audit column, with case-insensitive
ignore rules:

```csharp
var result = await SchemaContract
    .Against(connectionString, ContractDialect.Postgres)
    .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
    .IgnoreTables("__EFMigrationsHistory", "audit_log")
    .IgnoreColumns("orders.internal_notes")
    .VerifyAsync();

result.AssertNoBreakingDrift();
```

## Per-service contracts: scoping to your slice

A whole-snapshot contract is exactly right when one service owns its database.
When several services share one database, it over-asserts: service B adding a
column to *its* table would show up as drift in service A's contract even though
A never touches that table. **Per-service scoping** narrows the contract to the
slice a service actually depends on, its owned/consumed tables, so:

- drift **inside** the slice fails, with the exact same severity rules as an
  unscoped contract (the scope is a filter over the same classifier, never a
  second rule set);
- changes **outside** the slice produce **no drift of any severity**, another
  team's migration cannot fail your gate.

Supply the scope explicitly:

```csharp
var result = await SchemaContract
    .Against(connectionString, ContractDialect.Postgres)
    .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
    .ForTables("orders", "order_items")     // only this slice is compared
    .VerifyAsync();

result.AssertNoBreakingDrift();
```

or derive it from the [schema-dependency registry](schema-registry.md)
(feature 009), so the dependence is declared once, in
`jaunty.registry.json`, and reused everywhere:

```csharp
var result = await SchemaContract
    .Against(connectionString, ContractDialect.Postgres)
    .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
    .ForService("billing", "jaunty.registry.json") // scope = billing's `consumes` tables
    .VerifyAsync();
```

`ForColumns("orders.total")` narrows further, to specific columns of a table, other columns of that table stop being your concern. Both compose with
`IgnoreTables`/`IgnoreColumns`, which still exclude *within* the slice.

Semantics worth knowing:

- An in-scope table or column **missing or narrowed** in the live database is
  breaking, a depended-on object vanished. An added column in a consumed
  in-scope table stays compatible.
- The report is never silent about scope: `result.Report.Scope` names the
  service and lists exactly which tables were compared, each tagged
  `owned`/`consumed` (annotation only, both are compared with the same rules).
- A scope naming a table the **snapshot** does not declare fails with a
  `ServiceScopeException`, a scope/registry error, distinct from drift.
- A service that declares **no** `consumes` yields an empty scope: the result is
  vacuously green and the report says "no scope" explicitly, never a
  pass-as-if-verified.
- Two services may both depend on `orders`; each verifies it independently.

The CLI equivalent is `jauntyq schema verify --service <id> [--registry <path>]`
(see the [CLI reference](../06-reference/cli.md)).

## Usage-aware severity: downgrading drift you cannot hit

The structural rules are deliberately conservative: a dropped column is breaking
whether or not any query reads it. For a service that touches a narrow slice of
a wide table, that fails the contract on changes the service is genuinely immune
to. **Usage-aware severity** (opt-in) layers what JauntyQ already knows offline, exactly which `(table, column)` pairs the generated queries reference, on top of
the structural classifier:

- a structurally-**breaking** change to an object **no generated query
  references** is downgraded to **informational** (it cannot break this app);
- a change to a **referenced** object keeps its structural severity;
- with no manifest, nothing changes, the structural rules stay the default.

### 1. Export the usage manifest from your corpus

```bash
jauntyq usage export --queries db/queries --snapshot db/schema/jaunty.schema.json \
                    --output jaunty.usage.json
```

The manifest is the union of every referenced table and `table.column` across
your hand-written `.sql` files **and** the auto-CRUD synthetics the generator
emits (pass `--no-auto-crud` only if your project sets
`<JauntyQAutoCrud>false</JauntyQAutoCrud>`), plus the snapshot hash it was built
against. Commit it, and **regenerate it in CI** whenever queries or the snapshot
change so it never drifts from the code.

### 2. Opt in at verification time

```csharp
var result = await SchemaContract
    .Against(connectionString, ContractDialect.Postgres)
    .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
    .WithUsage("jaunty.usage.json")        // opt-in: downgrade-only
    .VerifyAsync();

result.AssertNoBreakingDrift();            // downgraded drift no longer throws
```

CLI equivalent: `jauntyq schema verify ... --usage jaunty.usage.json`.

### The safety invariant: downgrade-only, never in doubt

Usage-aware mode can only ever **soften** a report, and only when the object is
**provably unreferenced**. It never upgrades a severity, and under any
uncertainty it keeps the structural verdict:

- an **unqualified column** in a multi-table query marks every in-scope table's
  columns potentially referenced (the same safe over-approximation the
  migration-impact classifier uses);
- a query that **doesn't parse** (or can't be attributed to a table) disables
  *all* downgrades, nothing is provable while it exists;
- a query **invalid against the snapshot** marks all its tables potentially
  referenced;
- a **table drop** is downgraded only when *no* column of that table is
  referenced anywhere;
- a **column reorder** is downgraded only when the *whole table* is
  unreferenced, a reorder can shift referenced columns.

So a stale or over-broad manifest can make the mode over-conservative, never
unsafe: a real breaking change on something you use always stays breaking.

Downgrades are never silent: each downgraded entry stays in the report with
`Downgraded = true`, its original `StructuralSeverity`, and the reason; the
report's `Usage` block records the manifest hash and the downgrade count. If the
manifest was built against a **different snapshot** than the one being verified,
the report is flagged stale (and the CLI warns), re-run `jauntyq usage export`.

Usage-aware severity **composes with per-service scoping**: the scope filters
*which objects are compared*, usage adjusts *the severity of what remains*
(scope-then-usage). Both only ever narrow failures.

## Inspecting the report

For custom assertions or CI annotations, read the structured report instead of (or
in addition to) asserting:

```csharp
var result = await SchemaContract.Against(conn, ContractDialect.MySql)
    .LoadSnapshotFromFile("db/schema/jaunty.schema.json")
    .VerifyAsync();

foreach (var drift in result.Report.Drifts)
    Console.WriteLine($"{drift.Severity}: {drift.Message}");

string json = result.Report.ToJson();
```

## Where to point the test

Contract testing is only meaningful against a database that reflects what the app
will run on. Good targets:

- A **Testcontainers** instance seeded with your migrations (per-run, hermetic).
- A **CI service database** the pipeline provisions.
- A **staging** database, as a pre-deploy smoke test.

The verification reflects the live database over the network, so it belongs in an
integration / end-to-end test stage, not a fast unit-test stage.

## Failures are never silent

If the database cannot be reached, `VerifyAsync()` throws a
`SchemaContractConnectionException`; if the snapshot file is missing or unreadable
it throws too. A verification that could not actually run is never reported as a
passing contract. If you want the test to be skipped when no database is
available (e.g. Docker absent locally), guard it in your own test, the library
itself does not soft-skip.

## See also

- [Runtime startup schema verification](startup-verification.md), the same
  classifier at boot time: fail fast (or warn) when the database a deployment
  actually connects to has drifted.
- [CLI reference, `schema verify`](../06-reference/cli.md), the same classifier
  for pipelines without a test host.
- [Schema snapshot format](../06-reference/schema-snapshot-format.md), the file
  the contract is checked against.
- [Migration impact analysis](../06-reference/cli.md), the offline `migrate
  impact` command for pending migration sets.
