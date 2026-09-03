# Runtime startup schema verification

Contract testing catches drift in CI, but CI verifies the database CI points
at, not necessarily the one a given deployment connects to at runtime. A service
can pass CI and still boot against a database that drifted: a hand-run
migration, a wrong connection string, a replica lagging a DDL change.

**Startup verification** closes that last gap. At boot, after the app knows its
connection string and before it serves traffic, `StartupSchemaGuard` reflects
the live database and compares it to the compiled snapshot with the **same
classifier** as [contract testing](contract-testing.md). On breaking drift it
fails fast (or warns, per policy), in the deploy that has the problem, instead
of on the first unlucky query.

The guard lives in **`Extrode.JauntyQ.Schema.Contract`** (the non-core package that
already carries the ADO.NET providers and the comparer). The zero-dependency
`Extrode.JauntyQ.Runtime` is untouched: apps that do not opt in keep a pristine core.

## Basic usage

Call the guard **once**, from the startup path (`Program.cs` / `Startup`):

```csharp
using Extrode.JauntyQ.Schema.Contract;

// Program.cs, after configuration, before serving traffic.
await StartupSchemaGuard.VerifyAsync(
    connectionString,
    ContractDialect.SqlServer,
    SnapshotSource.FromFile("db/schema/jaunty.schema.json"),
    StartupVerificationMode.Throw);
```

If the live database has breaking drift from the snapshot, this throws a
`StartupSchemaException` naming every breaking object, and the app fails to
start. With no breaking drift it returns the comparison report and startup
proceeds. A synchronous `StartupSchemaGuard.Verify(...)` exists for plain
`Main` paths.

> **Run once, at boot.** Every call reflects the live schema over the network.
> Do not call the guard per request, per connection, or on a hot path.

## Modes

The mode is a `StartupVerificationMode`, chosen by the host, typically from
configuration or an environment variable, never hard-coded on:

| Mode | Behavior | Use for |
|---|---|---|
| `Throw` | Breaking drift throws `StartupSchemaException`; app fails to start | Canaries, staging, fail-fast production |
| `Warn` | Drift is written to the sink; startup proceeds | Observability without blocking |
| `Off` | Returns immediately, no snapshot load, **no database I/O** | Environments that must not reflect at boot |

```csharp
var mode = Environment.GetEnvironmentVariable("SCHEMA_GUARD") switch
{
    "warn" => StartupVerificationMode.Warn,
    "off"  => StartupVerificationMode.Off,
    _      => StartupVerificationMode.Throw,
};
```

In `Throw` mode, **compatible-only** drift (an added table, a widened column)
never fails startup. Pass `strict: true` to reject any drift at all, the
boot-time mirror of `AssertNoDrift()`.

### The Warn sink

`Warn` takes a plain `Action<string>`, deliberately not a logging framework,
so this package adds no logging dependency. Adapt it to whatever you use:

```csharp
await StartupSchemaGuard.VerifyAsync(
    connectionString, ContractDialect.Postgres,
    SnapshotSource.FromFile("db/schema/jaunty.schema.json"),
    StartupVerificationMode.Warn,
    warnSink: msg => logger.LogWarning("{SchemaDrift}", msg));
```

When no sink is supplied, Warn writes to **stderr**, it is never silent.

## Locating the compiled snapshot

The guard is only meaningful against the exact snapshot the app was compiled
with. `SnapshotSource` offers three ways to supply it:

```csharp
SnapshotSource.FromFile("db/schema/jaunty.schema.json");          // shipped alongside the app
SnapshotSource.FromEmbeddedResource(typeof(Program).Assembly,
                                    "jaunty.schema.json");        // single-file / AOT friendly
SnapshotSource.FromSchema(databaseSchema);                        // an object you already hold
```

`FromEmbeddedResource` accepts the full manifest name or a unique suffix
(resource names are prefixed with the root namespace and folder path).

### Embedding the snapshot at build time

To make a self-contained binary carry its own contract, opt in from the project
that references `Extrode.JauntyQ.Schema.Contract`:

```xml
<PropertyGroup>
  <JauntyQEmbedSnapshot>true</JauntyQEmbedSnapshot>
</PropertyGroup>
```

The package's build targets then embed the snapshot from the conventional
locations (`schema/*.schema.json` or `db/schema/*.schema.json`), or from an
explicit `<JauntyQSnapshotPath>path/to/jaunty.schema.json</JauntyQSnapshotPath>`, as an assembly resource. Projects that do not set the property are
completely unaffected.

## Failures are honest and distinct

A verification that could not run is **never** reported as passing:

| Failure | Exception |
|---|---|
| Snapshot missing / unreadable / malformed | `SnapshotSourceException` |
| Database unreachable / reflection failed | `SchemaContractConnectionException` |
| Drift rejected by policy (Throw mode) | `StartupSchemaException` (carries the full `Report`) |

`Off` mode is the only path that skips verification, explicitly, by policy,
before any I/O.

## See also

- [Database contract testing](contract-testing.md), the same classifier at
  test time, gating CI on drift.
- [CLI reference, `schema verify`](../06-reference/cli.md), the classifier
  for pipelines without a test host.
- [Schema snapshot format](../06-reference/schema-snapshot-format.md), the
  contract file itself.
