# Guides

Task-focused walkthroughs for specific JauntyQ features. Each guide assumes you
have the generator wired into a project (see the repository `README.md`
Quickstart and the [first-hour tutorial](../02-learn/README.md)).

- **[DDL as schema source](ddl-as-schema-source.md)**, build the schema from
  `CREATE TABLE` files instead of pulling a live snapshot.
- **[Sequences](sequences.md)**, typed accessors for database sequence objects
  (SQL Server and PostgreSQL).
- **[Bulk insert](bulk-insert.md)**, insert many rows fast via the provider's
  native bulk-copy path.
- **[Migrating from Dapper or EF Core](migrating-from-dapper-and-ef.md)**, how
  an existing data layer maps onto JauntyQ's SQL-first, compile-time model.
- **[Database contract testing](contract-testing.md)**, assert a live database
  matches the committed snapshot from your own test suite, gating CI on drift.
- **[Runtime startup schema verification](startup-verification.md)**, fail fast
  at boot (or warn, or skip, per policy) when the live database has drifted from
  the compiled snapshot.
- **[Central schema-dependency registry](schema-registry.md)**, name schema
  snapshots in `jaunty.registry.json`, declare which services depend on which
  tables, and query the blast radius (`jauntyq registry ...`).
- **[Live EXPLAIN plan analysis](explain-analysis.md)**, opt-in `jauntyq explain`
  runs the query corpus through the database's estimate-only EXPLAIN and flags
  plan-level problems (large-table scans, missing-index sorts) by severity.
