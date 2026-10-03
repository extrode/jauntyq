# Tenant scoping

In a multi-tenant database one missing `WHERE tenant_id = @tenantId` returns
another customer's rows. JauntyQ can check at build time that every query
filters each tenant-owned table by its tenant column, and can generate
auto-CRUD methods that take the tenant value first and filter on it.

It checks the SQL's shape. It cannot check that the value you pass is the
right tenant: that comes from your authentication, not from the query.

## Declare the scoped tables

Add a `jaunty.scope.json` next to the snapshot, and include it in the
project's `AdditionalFiles`:

```json
{
  "scopes": [
    { "table": "orders", "column": "tenant_id" },
    { "table": "order_lines", "column": "tenant_id" }
  ]
}
```

```xml
<AdditionalFiles Include="db\schema\*.scope.json" />
```

Each entry names one table and the column that scopes it. A table can have
more than one entry; every one of them must be proven. Mistakes in the file
are `JNT6004` warnings, and a dropped entry leaves its table unscoped, so read
those warnings. The [configuration reference](../06-reference/configuration.md#the-scope-sidecar-scopejson)
lists the rules.

With no scope file, or an empty one, nothing is checked and the generated code
is byte-identical to a project without the feature. An empty file still raises
`JNT6004`, so a file that scopes nothing by mistake is not silent.

## Hand-written queries

A query that reaches a scoped table without proving the filter is refused
with `JNT4005` (Error), and no method is generated for it:

```sql
-- refused: orders is read without its scope column
SELECT o.id, o.note FROM orders o WHERE o.note = @note

-- accepted
SELECT o.id, o.note FROM orders o WHERE o.tenant_id = @tenantId AND o.note = @note
```

A proof is a top-level `AND` condition of exactly `<ref>.<column> = @param`,
where `<ref>` is the alias that table has in that query. The parameter may
have any name. Where the proof has to be depends on how the table is reached:

| Reached as | Proof goes in |
|---|---|
| `FROM`, or an inner `JOIN` | `WHERE`, or the `ON` of any inner join |
| the nullable side of a `LEFT` join | that join's own `ON` |
| a table kept by a later `RIGHT` join | that `RIGHT` join's `ON` |
| any side of a `FULL` join | `WHERE` only: a `FULL` join keeps unmatched rows from both sides, so an `ON` condition filters nothing |
| inside a CTE body, or a `WHERE` `IN`/`EXISTS` subquery | inside that body. A proof in the outer query does not cover it |
| `UPDATE` or `DELETE` target | the statement's `WHERE` |
| `INSERT` target | the column list, bound to a parameter |

These are not proof: a condition under `OR` or `NOT`, a literal
(`tenant_id = 42`), another column (`o.tenant_id = c.tenant_id`), `IS NULL`,
and `IN (@ids)` under `-- @each`, which allows more than one tenant.

Each unproven reach is its own error, and the message names the table, where
it was reached, and the condition to add. JauntyQ never adds the condition
for you: a query that silently means something other than what you wrote is
worse than one that fails to build.

### Queries that must cross tenants

Billing roll-ups, admin consoles and data fixes read every tenant on purpose.
Mark them with a reason:

```sql
-- @unscoped nightly billing totals every tenant's orders
SELECT tenant_id, SUM(total) AS total FROM orders GROUP BY tenant_id
```

The reason is required. A directive on a query that already proves every
scope accepts nothing and is reported as `JNT4006`. See
[`-- @unscoped`](../06-reference/directives.md#-unscoped).

## Auto-CRUD on a scoped table

Every generated method of a scoped table takes the scope value before its
other parameters, and filters or writes with it:

| Method | Scoped form |
|---|---|
| `GetAll(tenant_id)` | `WHERE orders.tenant_id = @tenant_id` |
| `GetById(tenant_id, id)` | the scope, then the key |
| `GetBy<Fk>(tenant_id, fk)` | the scope, then the foreign key. No loader is generated for a foreign key on the scope column itself: `GetAll` is that query |
| `Insert(tenant_id, row)` | the scope column comes from the argument. The row's own value is ignored |
| `Update(tenant_id, row)` | the scope column is never in `SET`, so a row cannot move to another tenant, and `WHERE` filters on it |
| `Delete(tenant_id, row)` | `WHERE` filters on the scope |
| `BulkInsert(tenant_id, rows)` | each row's scope property is set to the argument as the row is read. Your row objects are changed |
| `Upsert(tenant_id, row)` | see below |

The parameter is named after the column, as every auto-CRUD parameter is.

A row whose key belongs to another tenant is out of reach: `GetById` returns
null, and `Update` and `Delete` return 0.

### Upsert

An upsert that collides with another tenant's key must not overwrite that row:

| Dialect | Scoped form |
|---|---|
| PostgreSQL, SQLite | `ON CONFLICT (key) DO UPDATE ... WHERE orders.tenant_id = EXCLUDED.tenant_id`. A collision with another tenant's row updates nothing and returns 0 |
| SQL Server | the `MERGE` matches on the key and the scope. A collision with another tenant's key falls through to the insert, which the primary key rejects with an error |
| MySQL | not generated. `ON DUPLICATE KEY UPDATE` has no `WHERE`, so it cannot leave another tenant's row alone. `JNT4007` (Info) says so; use `Insert` and `Update`, or write the upsert by hand |

## What is not checked

### Views

The snapshot records that a view exists, not which tables it reads. A view
over `orders` that is not in the scope file is not checked. A view that
exposes the scope column can be listed in the scope file itself, and is then
checked like a table.

### Procedures and functions

`-- @call`, `-- @proc`, and scalar and table-valued functions are opaque:
JauntyQ cannot see their bodies, so it cannot check them.

### The value

The check proves that a scope parameter is there, not that the caller passed
the right tenant. Take the tenant from the authenticated request, never from
the request body.

## Row-level security

Where the database has row-level security (PostgreSQL, SQL Server), use it as
well. JauntyQ catches the missing filter at build time, in the queries it
generates; row-level security catches what JauntyQ cannot see: views,
procedures, and SQL run outside JauntyQ.

## Turning the check off

Remove the scope file. For a single query, use `-- @unscoped <reason>`: it is
narrower, and the reason stays next to the SQL.

Setting `dotnet_diagnostic.JNT4005.severity = none` hides the error but does
not bring the method back, so the query silently has no generated method.
Do not use it as an off switch.
