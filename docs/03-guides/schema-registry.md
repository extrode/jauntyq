# Central schema-dependency registry

Real systems are not one-service-one-schema: several services read the same
database, one team owns the schema, and others consume slices of it. The
**central schema-dependency registry**, a checked-in `jaunty.registry.json`, names the canonical schema snapshots and records which services depend on which
schema (and, optionally, which tables). With it, tooling can resolve a snapshot
by id, enumerate a schema's or a table's dependents, and keep the declared
graph honest in CI.

The registry is **tooling metadata, opt-in, and additive**: a project without
one behaves exactly as today, the generator is unchanged, and nothing about it
exists at runtime.

## The registry file

`jaunty.registry.json` lives at the root the platform team chooses (typically
the repo root of the schema-owning project, or a shared infra repo). It has two
lists:

```json
{
  "schemas": [
    {
      "id": "sales",
      "snapshot": "schemas/sales.schema.json",
      "dialect": "postgres",
      "owner": "platform-team"
    }
  ],
  "services": [
    {
      "id": "billing",
      "dependsOn": ["sales"],
      "consumes": ["sales.orders", "sales.invoices"],
      "owner": "billing-team"
    },
    {
      "id": "reporting",
      "dependsOn": ["sales"]
    }
  ]
}
```

### `schemas`, the named snapshots

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | Unique schema id, referenced by services' `dependsOn`. |
| `snapshot` | Yes | Snapshot path, **relative to the registry file**. Rooted paths and `..` escapes are rejected (same rules as the CLI `--output`). |
| `dialect` | Yes | `postgres`, `sqlserver`, `mysql`, or `sqlite`. |
| `owner` | No | Owning team/contact. |

### `services`, the dependents

| Field | Required | Meaning |
|---|---|---|
| `id` | Yes | Unique service id. |
| `dependsOn` | Yes | Ids of the schemas the service depends on. |
| `consumes` | No | Declared table usage: `"schemaId.table"`, or a bare `"table"` when the service depends on exactly one schema. When omitted, dependents-by-table cannot narrow for this service, it appears under schema-level dependents only. |
| `owner`, `description` | No | Team/contact and free-form notes. |

## CLI verbs

All verbs are offline and read-only. The registry defaults to
`./jaunty.registry.json`; `--registry <path>` overrides. All verbs accept
`--format text|json` (default `text`).

```bash
# Resolve a snapshot by name instead of copying the file:
jauntyq registry resolve --schema sales
# schema:   sales
# snapshot: /abs/path/to/schemas/sales.schema.json
# dialect:  postgres
# owner:    platform-team

# Who depends on the schema at all?
jauntyq registry dependents --schema sales

# Who consumes a specific table? (the blast-radius question)
jauntyq registry dependents --table sales.orders

# Keep the registry honest in CI:
jauntyq registry validate

# Summarize everything:
jauntyq registry list
```

**Exit codes**: `0` ok; `1` usage or error (missing registry, unknown schema
id, bad `--table` reference); `2`, `validate` found problems (each problem is
individually named, e.g. `DanglingDependsOn: service 'billing' depends on
undeclared schema 'inventory'.`).

`validate` checks: unique schema/service ids; every snapshot path is
relative-within-tree and the file exists and parses; every `dependsOn` points
at a declared schema; every `consumes` entry resolves to a schema the service
depends on and to a table that exists in that schema's snapshot.

## Workflow: owner declares, consumers declare, CI validates

1. **The owning team** pulls the canonical snapshot (`jauntyq schema pull`),
   commits it, and declares it in the registry under a stable id.
2. **Each consuming team** adds its service with `dependsOn` (and ideally
   `consumes`, so table-level blast radius works for it).
3. **CI runs `jauntyq registry validate`** on every change to the registry or a
   snapshot, so a dangling reference or a consumed table that no longer exists
   fails the build.
4. **Before a schema change**, the owner runs
   `jauntyq registry dependents --table <schemaId.table>` to see exactly who is
   affected, and `--schema <id>` for the full dependent list.

A build or script can consume `resolve` instead of copying snapshots around:

```bash
SNAPSHOT=$(jauntyq registry resolve --schema sales --format json | jq -r .snapshot)
```

## Programmatic use

The registry lives in the non-core `Extrode.JauntyQ.Registry` library (references only
`Extrode.JauntyQ.Schema`): `RegistryLoader.LoadFile(path)` returns a `SchemaRegistry`
with `ResolveSchema(id)`, `DependentsOfSchema(id)`, and
`DependentsOfTable(schemaId, table)`; `RegistryValidator.Validate(registry)`
returns every problem, named. Per-service contracts (feature 010) build on this
surface.

## Non-goals (current)

- **Multi-schema code generation**, the registry only *locates* a snapshot;
  codegen stays single-schema. The `<JauntyQSchemaId>`/`<JauntyQRegistry>`
  build-property hook is deferred with it.
- **A hosted/served registry**, it is a checked-in JSON file, not a service.
- **Runtime use**, build/tooling metadata only; nothing ships in your app.
- **Auto-derived `consumes`**, declare table usage explicitly for now;
  deriving it from a service's query corpus is a planned follow-up.
- **Cross-service `migrate impact --registry`**, a follow-up built on
  dependents enumeration.
- **Federation**, one registry file per invocation.
