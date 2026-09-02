# Migrating from Dapper or EF Core

JauntyQ occupies the space between Dapper (hand-written SQL, hand-written
mapping) and EF Core (LINQ, change tracking, runtime SQL generation). It keeps
the SQL-first control of Dapper but generates the mapping, and validates the
SQL, at compile time. This guide covers what changes when you move an existing
data layer over.

## Mental model shift

| | Dapper | EF Core | JauntyQ |
|---|---|---|---|
| Where SQL lives | inline strings | generated from LINQ | `.sql` files |
| Mapping | reflection at runtime | reflection/expression trees | generated at build time |
| Schema errors surface | at runtime | at runtime (mostly) | **at build time** |
| Change tracking | none | yes | none |
| Runtime cost | reflection per query | high (tracking, translation) | none (typed ordinal reads) |

The core move is the same in both cases: SQL (or LINQ) that lived in C# moves
into `.sql` files that the generator turns into typed methods.

## From Dapper

Dapper is the closer fit, you already write SQL and think in terms of
parameters and result shapes.

**Before (Dapper):**

```csharp
var product = conn.QuerySingleOrDefault<Product>(
    "SELECT ProductId, ProductName, UnitPrice FROM Products WHERE ProductId = @id",
    new { id = 42 });
```

**After (JauntyQ)**, move the SQL into `db/tables/Products/GetById.sql`:

```sql
-- @first
SELECT ProductId, ProductName, UnitPrice FROM Products WHERE ProductId = @ProductId
```

```csharp
var db = new JauntyDb(conn);
Product? product = db.Products.GetById(42);
```

Mapping to `Product`, parameter typing, and the null-when-missing shape all come
from the generator. The SQL is validated against the snapshot at build time, so
a typo in a column name breaks the build instead of throwing at runtime.

Translation notes:

- `QuerySingleOrDefault` / `QueryFirstOrDefault` → [`-- @first`](../06-reference/directives.md#-first)
  (returns `Row?`).
- `Query` (many rows) → default (returns `List<Row>`); use
  [`-- @stream`](../06-reference/directives.md#-stream) for large sets.
- `Execute` (no result) → [`-- @result void`](../06-reference/directives.md#-result)
  (returns row count).
- `IN (@ids)` list expansion → [`-- @each`](../06-reference/directives.md#-each).
- Anonymous-parameter objects → named parameters (`@ProductId`) become typed
  method arguments; declare any the parser cannot infer with
  [`-- @params`](../06-reference/directives.md#-params).
- Custom projections (joins, partial selects) → a
  [`-- @result (...)`](../06-reference/directives.md#-result) inline shape or a
  named DTO.
- Simple CRUD you were writing by hand → delete it; auto-CRUD synthesises
  `GetAll`/`GetById`/`Insert`/`Update`/`Delete`/`Upsert`/`BulkInsert`.

## From EF Core

The move is larger because you give up LINQ-to-SQL translation and change
tracking, and gain compile-time validation and zero runtime overhead.

- **LINQ queries → `.sql` files.** Each `context.Products.Where(...)` becomes a
  `.sql` file. This is the bulk of the work, but it is mechanical, and the
  result is SQL you can read and tune.
- **`DbContext` → `JauntyDb`.** `JauntyDb` is a stateless facade over a
  `DbConnection`, no `OnModelCreating`, no migrations engine, no tracking. `new`
  one per request.
- **Change tracking → explicit writes.** There is no `SaveChanges()`.
  `Update(poco)` is a full-row update (last-writer-wins); for concurrency use a
  SQL Server `rowversion` column, which auto-CRUD turns into an optimistic
  token. See the [API overview](../06-reference/api-overview.md).
- **EF migrations → snapshot + `db/migrations/`.** JauntyQ does not run
  migrations against the database; it *simulates* pending DDL in
  `db/migrations/` on top of the snapshot so the build validates against the
  post-migration schema. Applying migrations to the database stays in your
  deploy pipeline.
- **Navigation properties / lazy loading → explicit FK loaders.** A foreign key
  in the snapshot generates `db.Orders.GetByCustomerId(...)`; you load related
  data with an explicit call, not a traversal.
- **Entity graphs / includes → joins in SQL.** Write the join yourself and shape
  the result with `-- @result`.

## Suggested order of migration

1. Pull a snapshot with `jauntyq schema pull` and commit it.
2. Wire the generator into the project (see [getting started](../01-getting-started/README.md)).
3. Delete hand-written CRUD you can get from auto-CRUD; keep only custom queries.
4. Port custom queries into `.sql` files one entity folder at a time; let the
   build tell you (via `JNTxxxx`) where SQL and schema disagree.
5. Replace `DbContext`/Dapper call sites with `JauntyDb` calls.
6. Add `jauntyq schema verify` to CI to catch future drift.

## What does not come along

JauntyQ deliberately omits change tracking, lazy loading, a LINQ provider, and
runtime model configuration. If you depend heavily on those, read
[Why JauntyQ (and why not)](../00-overview/why-jauntyq.md) before committing to
the move.
