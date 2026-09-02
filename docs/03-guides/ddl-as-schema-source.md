# DDL as schema source

JauntyQ normally validates your queries against a schema snapshot you pull from
a live database with `jauntyq schema pull` and commit as `.schema.json`. DDL mode
is an *alternative* input for projects that would rather define the schema from
`CREATE TABLE` files, with no database connection ever required, sqlc-style.

## When to use it

- You keep your schema as checked-in DDL and do not want a separate snapshot
  step.
- CI has no database to pull from and you would rather not commit a JSON
  snapshot.
- You are prototyping a schema before any database exists.

If you already pull a `.schema.json`, keep doing that: the snapshot carries
richer metadata (see [Limitations](#limitations)).

## Setup

1. Put your schema DDL under a `db/ddl/` folder, ordered by filename:

   ```
   db/
     ddl/
       001_schema.sql
       002_more_tables.sql
   ```

   ```sql
   -- db/ddl/001_schema.sql
   CREATE TABLE Widgets (
       WidgetId  INTEGER PRIMARY KEY,
       Name      TEXT NOT NULL,
       Quantity  INTEGER NOT NULL
   );
   ```

2. Declare the dialect in the `.csproj` (DDL mode has no snapshot to infer it
   from):

   ```xml
   <PropertyGroup>
     <JauntyQDialect>sqlite</JauntyQDialect>
   </PropertyGroup>

   <ItemGroup>
     <AdditionalFiles Include="db\**\*.sql" />
   </ItemGroup>
   ```

   Valid values: `sqlserver`, `postgres`, `mysql`, `sqlite`. The standard
   `db\**\*.sql` glob already picks up `db/ddl/*.sql`; you do not need a separate
   entry. When you consume JauntyQ as a package, `<JauntyQDialect>` reaches the
   generator automatically (the package ships the property registration); no
   extra `<CompilerVisibleProperty>` is required.

3. Do **not** commit a `db/schema/*.schema.json` snapshot for this project. See
   precedence below.

That is all. Build the project and the full typed surface (auto-CRUD, row
POCOs, value-safety guards) is generated from the DDL exactly as it would be
from a pulled snapshot.

## Precedence: JSON snapshot always wins

DDL mode is only active when **no** `.schema.json` snapshot is present. If a
snapshot is present, it is authoritative and any `db/ddl/*.sql` files are
silently ignored (no merge, no error). This keeps a pulled snapshot the single
source of truth whenever you have one.

## Migrations still apply on top

`db/migrations/*.sql` migrations are applied on top of the DDL-built base schema
exactly as they apply on top of a pulled snapshot. The base schema (from DDL) is
built first, then migrations simulate on top of it.

## Supported DDL

The same subset the migration simulator understands:

- `CREATE TABLE`, column definitions, type facets, `NOT NULL`, `PRIMARY KEY`
  (inline or table-level), `IDENTITY`.
- `DROP TABLE [IF EXISTS]`.
- `ALTER TABLE ADD / DROP COLUMN / ALTER COLUMN` (plus MySQL `MODIFY` and
  PostgreSQL `TYPE`).

Statements split on `;` and `GO`. Index, transaction, and permission statements
are ignored (they cannot change the model JauntyQ needs). Anything else, renames
in particular, is `JNT9001` (a warning that the effective schema may be
incomplete). DDL that is invalid against the schema being built (creating a
table that exists, dropping one that does not) is `JNT9002` (an error).

## Diagnostics

| Code | When |
|---|---|
| `JNT9003` | `db/ddl/*.sql` files are present with no snapshot, but `<JauntyQDialect>` is missing or not one of `sqlserver`/`postgres`/`mysql`/`sqlite`. |
| `JNT9002` | A DDL statement targets a table/column that does not exist (or already exists). |
| `JNT9001` | A DDL statement is not simulated (e.g. a rename); the effective schema may be incomplete. |

## Limitations

The migration parser and schema simulator do **not** carry secondary
`UNIQUE`-index metadata. A DDL-sourced table exposes its primary key, identity,
and column types, but no secondary unique indexes. Two consequences, both
matching every migration-based schema (not specific to DDL mode):

- The `JNT8004` composite-index performance check and Upsert alternate-key
  resolution only ever see the primary key for a DDL-sourced table.
- If you rely on unique-index-aware performance analysis or alternate-key
  upserts, pull a `.schema.json` snapshot instead; it captures index metadata.
