# JauntyQ Documentation

JauntyQ is a compile-time SQL-to-C# source-generator ORM. You write real SQL in `.sql` files; JauntyQ validates it at build time against a committed schema snapshot and emits the fastest possible C#, typed getters by ordinal, no reflection, zero per-row boxing, no runtime SQL parsing. The generated code carries everything; the runtime package is intentionally near-empty.

## How it works

1. Pull a schema snapshot from your database into a JSON file you commit (`sqlserver`, `postgres`, `mysql`, or `sqlite`).
2. Write queries as `.sql` files, versioned alongside the code that uses them.
3. At build time the generator parses those files against the snapshot and emits typed C# methods. If a column is renamed or a table drops, the build breaks, not production.

Because there is no reflection or runtime code generation, the emitted code is Native AOT and trim compatible.

## These docs

- **[Why JauntyQ (and why not)](why-jauntyq.md)**, the honest pitch: problems solved, problems deliberately not solved, and how to choose between JauntyQ, Jaunty, and EF Core.
- **[Pricing](pricing.md)**, free core, paid team-safety tier; flat per-organization pricing (Community, Team, Business, Enterprise); how the licensing works.
- **[Getting started](../01-getting-started/README.md)**, requirements, installing the packages, wiring the generator, and your first build.
- **[Learn by doing](../02-learn/README.md)**, a guided first hour against SQLite plus coding exercises, no database server required.
- **[Guides](../03-guides/README.md)**, task-focused walkthroughs: DDL as schema source, sequences, bulk insert, and migrating from Dapper/EF.
- **[Reference](../04-reference/README.md)**, the CLI, snapshot format, configuration, directives, dialect differences, diagnostics, and troubleshooting.
- **[Roadmap](../09-roadmap/roadmap.md)**, where JauntyQ is headed.

For quickstart instructions and the full feature walkthrough, see the repository `README.md`.
