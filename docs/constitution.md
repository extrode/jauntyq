# Constitution — jauntyq

Binding project rules. These are the WON'T-change decisions. Contributors obey them;
changing one is a deliberate governance act, not a casual edit. This is NOT lessons-learned
(see docs/lessons/) and NOT mechanical conventions (see docs/conventions.md).

Status: draft, not yet populated with governance decisions

## Stack constraints
- **Zero dependencies in the runtime.** `Extrode.JauntyQ.Runtime` has none. The generator, CLI,
  and schema-extraction packages carry their own dependencies separately.
- **NativeAOT-compatible.** No runtime reflection in the runtime's hot paths.
  `Extrode.JauntyQ.Runtime` is marked `IsAotCompatible`.
- **ADO.NET only.** All database integration goes through `IDbConnection` and `DbDataReader`.
  Providers are validated per-dialect (`sqlserver`, `postgres`, `mysql`, `sqlite`), never special
  cases hardcoded into the generator.
- **Compile-time validation, not runtime.** Queries are validated against a committed schema
  snapshot at build time. The runtime package does no schema inspection of its own.

This file is a stub. Populate it with the project's actual won't-change decisions as they are
made, following the same structure as `docs/conventions.md`.
