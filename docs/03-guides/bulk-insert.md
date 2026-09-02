# Bulk insert

Every table gets a `BulkInsert` method that inserts many rows in one round of
work, far faster than looping single-row `Insert` calls. Where the provider has
a native bulk-copy path, JauntyQ uses it; otherwise it falls back to a single
transaction with one prepared command.

## API

```csharp
var db = new JauntyDb(conn);

int inserted = db.Products.BulkInsert(newProducts);              // IEnumerable<Product>
int inserted2 = await db.Products.BulkInsertAsync(newProducts, cancellationToken);

// Static overloads take an explicit connection:
int inserted3 = Products.BulkInsert(conn, newProducts);
int inserted4 = await Products.BulkInsertAsync(conn, newProducts, cancellationToken);
```

`BulkInsert` returns the number of rows inserted (`int`; async returns
`Task<int>`). It accepts `IEnumerable<Row>`, so you can stream rows in without
materializing them all first.

Identity and `rowversion` columns are **excluded** from the insert (the database
assigns them), exactly as with single-row `Insert`.

## Per-dialect fast paths

The emitted body specializes on the snapshot dialect:

| Dialect | Mechanism |
|---|---|
| PostgreSQL | Npgsql binary `COPY` (`NpgsqlBinaryImporter`) |
| SQL Server | `SqlBulkCopy` |
| MySQL / MariaDB | `MySqlBulkCopy` |
| SQLite / other | Portable single-transaction prepared-command loop |

SQL Server and MySQL feed their bulk-copy through a single reflection-free,
AOT-safe `DbDataReader`-over-`IEnumerable<Row>` adapter emitted once per table
(ordinal-based, no reflection), so the fast paths stay Native-AOT compatible.

## MySQL / MariaDB: enable LOCAL INFILE

`MySqlBulkCopy` requires `LOCAL INFILE` to be enabled on **both** sides:

- Connection string: add `AllowLoadLocalInfile=true`.
- Server: `local_infile` must be `ON` (`SET GLOBAL local_infile = 1`, or the
  server/container configured with `--local-infile=1`).

JauntyQ cannot set these for you and documents the requirement on the generated
method. Note that `MySqlConnector` describes its `MySqlBulkCopy` API as
experimental and subject to change.

```csharp
// Connection string must permit local infile for the MySQL fast path:
var conn = new MySqlConnection("Server=...;Database=...;AllowLoadLocalInfile=true");
```

## Choosing bulk vs looped insert

Use `BulkInsert` when you have many rows to add at once. For a handful of rows,
or when you need each row's assigned identity back, use single-row `Insert`
(which can return the identity via `-- @identity`); bulk copy does not return
per-row generated keys.
