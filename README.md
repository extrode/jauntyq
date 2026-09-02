# JauntyQ

A compile-time SQL-to-C# source-generator ORM. You write real SQL; JauntyQ validates it at build time against a schema snapshot and emits the fastest possible C#, typed getters by ordinal, no reflection, zero per-row boxing, no runtime SQL parsing. The generated code carries everything; the runtime package is intentionally near-empty.

**License.** JauntyQ is source-available under the Islamic Software License - Restricted
(ISL-R), Version 1.2, with the Output Exception (ISL-OE), Version 1.2: read and use the
tool, and the code it generates into your project is yours to ship. See [License](#license).

## Philosophy

JauntyQ does not hide SQL. Every query lives in a `.sql` file, versioned alongside the code that uses it. At build time the generator reads those files, parses them against a schema snapshot you committed, and emits typed C# methods. If a column is renamed or a table drops, the build breaks, not production.

The generated code is performance-aligned by construction:

- Column values are read by ordinal (`GetInt32(0)`, `GetString(1)`), never by name.
- Parameters are bound with explicit `DbType` from the schema, no provider type inference at runtime.
- There is no reflection, zero per-row boxing, and no runtime SQL parsing.
  (Parameter values box once per call at the ADO.NET boundary - `DbParameter.Value`
  is `object` - a fixed cost every data library pays.)
- A one-time shape guard validates the result-set column names and count on the first call per query, then never again.
- Because there is no reflection or runtime code generation, the emitted code is **Native AOT and trim compatible**. `JauntyQ.Runtime` is marked `IsAotCompatible`, and `samples/JauntyQ.Aot.Smoke` publishes to a self-contained native binary (verified in CI) that runs the full auto-CRUD, identity-return, and `@stream` paths against SQLite. (Your database provider must also be AOT-friendly, e.g. `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3`.)

---

## JauntyQ or Jaunty?

**JauntyQ starts from SQL. [Jaunty](https://github.com/extrode/jaunty) starts from C#.** They are two
products, not two modes of one product, and the split is a question about you rather than about your
database.

JauntyQ is for the developer whose position is: *I know SQL, I know what I want to run, validate it
and otherwise stay out of my way.* Everything above is that request taken seriously. The SQL that
runs is the SQL you wrote, in a file you can open, diff and review; JauntyQ's job is to make it a
compiler-checked citizen and then stay out of the runtime.

Jaunty is the same author's answer to the opposite developer: the traditional ORM, for someone who
would rather solve data access with C#'s own mechanisms and drop to a SQL string only when it is the
clearer tool. Its optional Fluent package (`Extrode.Jaunty.Fluent`) builds parameterized SQL from
typed expressions, so column references are checked by the compiler and injection is prevented by
construction. It is expression-first, not a LINQ provider: it does not translate arbitrary
`IQueryable`. Jaunty's core maps strictly by default, so `Query<T>` throws when an entity property
has no matching column, and `QueryPartial<T>` marks a projection as deliberate.

Neither product will silently map the wrong thing. They catch it at different moments: JauntyQ at
build time against a snapshot, Jaunty at the call site against the live result set.

### The two directions, and what each one produces

The distinction is easiest to see by looking at what you write and what comes out the other side.
**With JauntyQ you write SQL and get C#. With Jaunty you write C# and get SQL.** Every listing below
is real output, not an illustration.

#### JauntyQ: SQL in, C# out

You write the file. This one ships as-is in `samples/JauntyQ.Northwind.Tests`:

```sql
-- db/tables/Products/GetByCategory.sql
select p.ProductId, p.ProductName, p.UnitPrice, p.UnitsInStock, c.CategoryName
from Products p
join Categories c on p.CategoryId = c.CategoryId
where p.CategoryId = @CategoryId
```

The generator validates it against the committed schema snapshot and emits this, which is what your
code calls:

```csharp
public class GetByCategory
{
    public required int ProductId { get; set; }
    public required string ProductName { get; set; }
    public required decimal? UnitPrice { get; set; }
    public required short? UnitsInStock { get; set; }
    public required string CategoryName { get; set; }
}

public List<Result.GetByCategory> GetByCategory(short? CategoryId)
{
    using var cmd = _conn.CreateCommand();
    cmd.CommandText = @"select p.ProductId, ... where p.CategoryId = @CategoryId";

    var p0 = cmd.CreateParameter();
    p0.ParameterName = "@CategoryId";
    p0.DbType = System.Data.DbType.Int16;
    p0.Value = (object?)CategoryId ?? System.DBNull.Value;
    cmd.Parameters.Add(p0);

    using var reader = cmd.ExecuteReader(CommandBehavior.SingleResult);
    JauntyQShapeGuard.Validate(reader, __GetByCategoryColumns, "Products.GetByCategory");
    ...
}
```

so you call:

```csharp
var db = new JauntyDb(connection);
var rows = db.Products.GetByCategory(1);

foreach (var row in rows)
    Console.WriteLine($"{row.ProductName} ({row.CategoryName})");
```

Three things the schema snapshot decided for you without being asked: the parameter is `short?`
because `CategoryId` is a `smallint`, its `DbType` is `Int16` rather than left to provider
inference, and `UnitPrice` is `decimal?` because the column is nullable. Rename `CategoryName` in
the database, re-pull the snapshot, and this file stops compiling.

#### Jaunty: C# in, SQL out

Jaunty's core API needs no query at all. The entity carries the mapping, and its source generator
emits the SQL at build time:

```csharp
using Jaunty;

var all      = connection.GetAll<Product>();
var one      = connection.Get<Product>(1);
long newId   = connection.Insert(product);
int updated  = connection.Update(product);
int deleted  = connection.Delete(product);
```

What those five calls send to the database:

```sql
SELECT product_id, product_name, category_id, unit_price, units_in_stock, discontinued FROM products
SELECT product_id, product_name, category_id, unit_price, units_in_stock, discontinued FROM products WHERE product_id = @product_id
INSERT INTO products (product_name, category_id, unit_price, units_in_stock, discontinued) VALUES (@product_name, @category_id, @unit_price, @units_in_stock, @discontinued); SELECT last_insert_rowid();
UPDATE products SET product_name = @product_name, category_id = @category_id, unit_price = @unit_price, units_in_stock = @units_in_stock, discontinued = @discontinued WHERE product_id = @product_id
DELETE FROM products WHERE product_id = @product_id
```

For anything past CRUD, its Fluent builder takes typed expressions:

```csharp
using Jaunty.Fluent;

var rows = connection.From<Product>()
    .InnerJoin<Category>()
    .On(p => p.CategoryId, c => c.CategoryId)
    .Where((p, c) => p.CategoryId == 1)
    .SelectBoth();                       // List<(Product From, Category Joined)>
```

and produces:

```sql
SELECT products.product_id AS f_product_id, products.product_name AS f_product_name,
       products.category_id AS f_category_id, products.unit_price AS f_unit_price,
       products.units_in_stock AS f_units_in_stock, products.discontinued AS f_discontinued,
       categories.category_id AS j_category_id, categories.category_name AS j_category_name,
       categories.description AS j_description
FROM products
INNER JOIN categories ON products.category_id = categories.category_id
WHERE (products.category_id = @jp0)
```

Both products end up issuing parameterized ADO.NET commands over hand-shaped SQL. The question is
only which end you author from, and when the mismatch is caught.

### Choose JauntyQ if

- SQL is where you are most fluent, and you want every query to be SQL you wrote, versioned as
  `.sql` files next to the code that calls them.
- You want a renamed column or a dropped table to break the build rather than a request in
  production.
- You want the generated code to be the code you would have hand-written: ordinal reads, typed
  parameters, no reflection, no runtime parsing.
- You want auto-CRUD, FK loaders, migration impact analysis and a build-time performance analyzer
  over your own SQL.

### Choose Jaunty if

- You want data access solved in C#, reaching for SQL strings when they are the clearer tool rather
  than as the default.
- Your queries take shape at runtime, or a committed schema snapshot and a generator step do not fit
  your workflow.
- You want runtime strict mapping at the call site, plus bulk copy, scaffolding, DuckDB and
  flat-file querying, and NativeAOT publishing from one library family.

If the second list is you, Jaunty is at [github.com/extrode/jaunty](https://github.com/extrode/jaunty)
and you will be better served there.

---

## Quickstart

### 0. Install the CLI

```bash
dotnet tool install --global JauntyQ.Cli
```

Installs as `jauntyq`. Working inside a clone of this repository instead, substitute
`dotnet run --project src/JauntyQ.Cli --` for `jauntyq` in every command below.

### 1. Pull a schema snapshot

```bash
jauntyq schema pull \
  --provider sqlserver \
  --connection "Server=localhost;Database=MyDb;Trusted_Connection=true" \
  --output db/schema/jaunty.schema.json
```

Supported providers: `sqlserver`, `postgres`, `mysql`, `sqlite`.

The snapshot is a JSON file you commit. It records table names, column names, types, nullability, primary key flags, and identity flags, plus the dialect. Example fragment:

```json
{
  "dialect": "sqlserver",
  "tables": {
    "Products": {
      "name": "Products",
      "columns": {
        "ProductId": { "name": "ProductId", "dbType": "int", "isNullable": false, "isPrimaryKey": true, "isIdentity": true },
        "ProductName": { "name": "ProductName", "dbType": "nvarchar", "isNullable": false, "isPrimaryKey": false, "isIdentity": false }
      }
    }
  }
}
```

### 2. Wire up the generator in your project

```xml
<ItemGroup>
  <!-- SQL query files -->
  <AdditionalFiles Include="db\**\*.sql" />
  <!-- Schema snapshot -->
  <AdditionalFiles Include="db\schema\*.schema.json" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\JauntyQ.Generator\JauntyQ.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\JauntyQ.Runtime\JauntyQ.Runtime.csproj" />
</ItemGroup>
```

Or consume the packages from NuGet.org:

```xml
<ItemGroup>
  <PackageReference Include="JauntyQ.Generator" Version="0.5.0" PrivateAssets="all" />
  <PackageReference Include="JauntyQ.Runtime" Version="0.5.0" />
</ItemGroup>
```

The generator package installs as a Roslyn analyzer automatically; no
`OutputItemType` needed.

### 3. Use the generated code

```csharp
using var conn = new SqlConnection(connectionString);
var db = new JauntyDb(conn);

// Instance API, connection opened/closed per call unless already open
var products = db.Products.GetAll();
var product  = db.Products.GetById(42);   // returns Row? (-- @first query)

// Static fallback, no db object needed
var products2 = Products.GetAll(conn);

// Async twins, every method has a CancellationToken variant
var products3 = await db.Products.GetAllAsync(cancellationToken);
```

---

## Auto-CRUD

For every table in the schema snapshot, JauntyQ synthesises five methods with zero SQL written by you:

| Method | SQL emitted |
|---|---|
| `GetAll` | `SELECT <all columns> FROM <Table>` |
| `GetById` | `SELECT ... WHERE <pk> = @Id` |
| `Insert` | `INSERT INTO ... VALUES (...)`, or, on a table with exactly one identity column, dialect-native identity return (see `-- @identity` below) |
| `Update` | `UPDATE ... SET ... WHERE <pk> = @Id` |
| `Delete` | `DELETE FROM ... WHERE <pk> = @Id` |

A user `.sql` file with the matching entity + method name overrides the synthetic. To write a custom `GetAll` for `Products`, create `db/tables/Products/GetAll.sql` and the auto-generated one is discarded.

To disable auto-CRUD for the whole project:

```xml
<PropertyGroup>
  <JauntyQAutoCrud>false</JauntyQAutoCrud>
</PropertyGroup>
```

---

## Result types: canonical row POCOs

Full-row queries return the canonical per-table POCO, the singularized
table name:

```csharp
List<Shipper> all = db.Shippers.GetAll();
Shipper?      one = db.Shippers.GetById(1);
Product?      p   = db.Products.GetById(5);
```

When the table name is already singular (or singularization is unsafe),
the POCO is `<Entity>Row` (`Region` table -> `RegionRow`). Custom
projections, joins, partial selects, get query-specific types under
`<Entity>.Result.<Method>` because no table POCO can represent them.
Each POCO carries a shared static `Read(DbDataReader)` materializer.

## FK loaders and Upsert

Foreign keys in the snapshot generate one loader per FK column on the
child table:

```csharp
List<Product> beverages = db.Products.GetByCategoryId(1);
List<Order>   orders    = db.Orders.GetByCustomerId("ALFKI");
```

Tables with a non-identity primary key also get a dialect-native Upsert
(MERGE / ON CONFLICT / ON DUPLICATE KEY), keyed on the PK, parameters in
column order:

```csharp
int affected = db.Customers.Upsert("ALFKI", "Alfreds Futterkiste", ...);
```

Skipped for identity-only keys (nothing to match on before insert), for
tables with no non-key columns, and for tables whose only usable unique
constraint is a MySQL/MariaDB column-prefix key, a prefix index enforces
uniqueness over the truncated prefix, not the full value, so no Upsert can
honour the method's contract (reported as JNT2019 rather than skipped
silently). As with all synthetics, a user .sql file with the same method
name overrides.

Every table with a synthetic `Insert` also gets a `BulkInsert(IEnumerable<Row>)`
(and async twin), far faster than looping `Insert`. It uses each dialect's
native bulk-copy API where one exists (`SqlBulkCopy` on SQL Server, binary
`COPY` on PostgreSQL, `MySqlBulkCopy` on MySQL) and a single-transaction
prepared-command loop everywhere else (SQLite and any other dialect).
Identity and rowversion columns are excluded.

```csharp
int inserted = db.Products.BulkInsert(newProducts);   // one transaction
await db.Products.BulkInsertAsync(newProducts, ct);
```

## POCO overloads

Synthetic write methods also accept the canonical POCO, the standard
read-modify-write flow:

```csharp
Shipper? shipper = db.Shippers.GetById(1);
shipper.CompanyName = "New name";
db.Shippers.Update(shipper);          // scalar order handled for you
db.Shippers.Delete(shipper);          // by primary key

var s = new Shipper { CompanyName = "Acme", Phone = "555" };
int id = db.Shippers.Insert(s);       // s.ShipperId is written back
```

Overloads exist only where the scalar method is the synthetic one; a
user-overridden .sql owns its signature and gets no POCO overload. All
writes return the affected row count (identity Inserts return the new
id). Every method has an Async twin with a CancellationToken.

## db/ folder convention

```
db/
  tables/
    Products/
      GetAll.sql          -- auto-CRUD or user override
      GetByCategory.sql   -- custom query
      GetById.sql         -- user override with -- @first
    Orders/
      GetByCustomer.sql
      Insert.sql          -- user override
  views/
    SummaryOfSalesByYear/
      GetAll.sql
  schema/
    jaunty.schema.json    -- committed snapshot
```

Folder name = entity name = generated partial class name. File name (without `.sql`) = method name.

---

## Directives reference

Directives are `--` comment lines at the top of a `.sql` file. They are stripped from the emitted `CommandText`.

| Directive | Effect |
|---|---|
| `-- @first` | Return type becomes `Row?` (single row or null) instead of `List<Row>`. Async twin returns `Task<Row?>`. |
| `-- @stream` | Return type becomes `IEnumerable<Row>` (sync) / `IAsyncEnumerable<Row>` (async), yielding rows lazily off the reader instead of buffering into a `List<Row>`. Keeps memory constant for large result sets. SELECT-only; cannot be combined with `-- @first` or `-- @proc` (reported as `JNT3003`). |
| `-- @result void` | Method returns `int` (row count). Used for INSERT/UPDATE/DELETE without a SELECT. |
| `-- @result TypeName` | Use an existing type as the DTO instead of generating one. |
| `-- @result (int Id, string Name)` | Inline column list, generates an anonymous-style DTO. |
| `-- @params Name:type, ...` | Explicitly declare parameters when the parser cannot infer them (e.g., `@Param` used in a subquery). |
| `-- @each ParamName` | Expands a parameter into a runtime IN-list: the C# parameter becomes `IReadOnlyList<T>`, and `@ParamName` in the SQL becomes `@ParamName0, @ParamName1, ...` at call time. Repeatable. SELECT-only; a non-SELECT or a bare/typo'd occurrence is `JNT3003`/`JNT3008`. |
| `-- @type alias dbtype` | Declares the DB type of a projected expression (aggregates, computed columns) the generator cannot infer from the schema. Repeatable; `<alias>` must match a SELECT alias. |
| `-- @proc` | Emits a `Proc` nested class with a `CREATE OR ALTER PROCEDURE` constant. Method uses `CommandType.StoredProcedure`. SQL Server only (`JNT7002` on any other dialect). |
| `-- @proc ProcedureName` | Same, with an explicit procedure name instead of inferring from entity + method. |
| `-- @call ProcedureName` | Bind to a stored procedure that already exists in the database (JauntyQ does not author the body). The file has no SQL; the params and result columns come from the schema snapshot's `procedures` section (captured by `jauntyq schema pull`). The sync overload emits a `CommandType.StoredProcedure` call with typed IN parameters (OUT/INOUT become C# `out`/`ref`), returning `List<Result>` for row-returning procs or `int` (row count) otherwise. The async twin can't declare `out`/`ref` (C# doesn't allow it on async methods), so it drops OUT parameters from its signature and returns them in a tuple alongside the normal result instead. Unknown procedure names are reported as `JNT2005`. |
| `-- @identity` | INSERT returns the typed database-assigned id instead of a row count: `OUTPUT INSERTED.<col>` (SQL Server), `RETURNING <col>` (PostgreSQL/SQLite), or `SELECT LAST_INSERT_ID()` narrowed to the column's type (MySQL). Requires an INSERT statement, a single identity column on the target table, and a dialect in the schema snapshot; violations are reported as `JNT7001`. Cannot be combined with `-- @proc`. Auto-CRUD Inserts get `-- @identity` automatically on tables with exactly one identity column. |

Parameter types that cannot be inferred from the SQL are a build error (`JNT4003`), not a silent `object` fallback, the message includes the exact fix, e.g. `-- @params CategoryId:int`.

### Example, single-row lookup

```sql
-- @first
SELECT ProductId, ProductName, UnitPrice
FROM Products
WHERE ProductId = @ProductId
```

Generated signature: `Product? GetById(int productId)` + async twin.

### Example, void result

```sql
-- @result void
DELETE FROM Products WHERE ProductId = @ProductId
```

Generated signature: `int Delete(int productId)` (returns row count).

### Example, explicit params

```sql
-- @params CategoryId:int
-- @result (int ProductId, string ProductName, decimal UnitPrice)
SELECT p.ProductId, p.ProductName, p.UnitPrice
FROM Products p
WHERE p.CategoryId = @CategoryId
```

---

## Shape guard

On the first call to each generated query method, JauntyQ validates the actual result-set column names and count against the expected list baked in at compile time. If the database has drifted from the snapshot, a column was renamed in production but the snapshot was not re-pulled, you get a precise error at first use rather than a silent wrong-column read. The check runs once per unique query path; it does not repeat on subsequent calls.

---

## Connection lifecycle

JauntyQ follows a simple rule in every generated method:

- Connection already open: use it, do not close it.
- Connection closed: open it, execute the query, close it.

```csharp
// Each call manages its own open/close
var db = new JauntyDb(conn);
db.Products.GetAll();         // opens -> query -> closes
db.Products.GetById(1);       // opens -> query -> closes

// Or keep the connection open yourself
conn.Open();
db.Products.GetAll();         // already open, left open
db.Products.GetById(1);       // already open, left open
conn.Close();
```

---

## Transactions

`JauntyDb` exposes `BeginTransaction()` / `BeginTransactionAsync()`, returning a nested `Transaction : IDisposable` that wraps the underlying `DbTransaction`:

```csharp
var db = new JauntyDb(conn);
using var tx = db.BeginTransaction();
db.Orders.Insert(...);
db.Inventory.Decrement(...);
tx.Commit();
```

```csharp
var tx = await db.BeginTransactionAsync(cancellationToken);
await db.Orders.InsertAsync(...);
await db.Inventory.DecrementAsync(...);
tx.Commit();
```

- Disposing without calling `Commit()` rolls back, the safe default when a request is abandoned mid-flight. `Transaction` implements only synchronous `IDisposable` (`Commit()`/`Rollback()`/`Dispose()`), there is no `CommitAsync`/`RollbackAsync`/`IAsyncDisposable`, so don't `await using` it.
- Calling `BeginTransaction()` twice on the same `JauntyDb` throws; commit or dispose the first one before starting another.
- The connection is closed on dispose only if the transaction itself had to open it.
- Instance methods (`db.Orders.Insert(...)`) auto-enlist in the ambient transaction via `cmd.Transaction`, no extra parameter needed.
- **Static methods do not auto-enlist** (`Orders.Insert(conn, ...)` never sees a `JauntyDb`, so there is nothing to enlist against). If you need a transaction with the static API, wrap the calls in a `System.Transactions.TransactionScope` instead.

---

## What JauntyDb is (and is not)

`JauntyDb` is the generated root facade: it holds your `DbConnection`
(never owns or disposes it), lazily exposes the entity accessors
(`db.Products`), and carries the active transaction so instance calls
auto-enlist. It is NOT a DbContext: no change tracking, no caching, no
unit-of-work state, a stateless router you can `new` per request for
free.

## Write semantics you should know

- **`Update(poco)` is a full-row update**: it SETs every non-key column.
  Concurrent edits between your read and your write are overwritten
  (last-writer-wins). Rowversion-based optimistic concurrency is planned;
  until then, treat contested rows accordingly.
- **MySQL/MariaDB `Upsert` affected counts** are server semantics: 1 for
  an insert, 2 for an update of an existing row (0 if values were
  identical). Don't assert `== 1` against it.
- **Inside `db.BeginTransaction()`, stay on `db.*`.** Static methods
  (`Products.GetAll(conn)`) don't know about the db-held transaction, and
  ADO.NET throws if a command runs on a connection with an active
  transaction it isn't assigned to. Statics pair with `TransactionScope`
  (ambient enlistment) instead.

## Value safety

The schema snapshot carries every column's max length, Unicode-ness, and
decimal precision/scale. JauntyQ uses that in three layers:

**1. Compile-time literal checks.** A SQL literal that cannot fit its
target column fails the build, not the deploy:

```sql
insert into products (product_name) values ('A very long name that exceeds forty characters')
```

```
error JNT5001: String literal (47 chars) exceeds products.product_name max
length of 40. It would truncate on write and can never match on read.
```

Numeric literals are checked against decimal precision/scale and integer
ranges (JNT5002): `where product_id = 9999999999` on an `int` column is a
build error, because it can only overflow at runtime.

**2. Client-side write guards.** Generated write methods (Insert, Update,
Upsert, and POCO overloads) validate string/binary parameter lengths before
touching the connection:

```
System.ArgumentException: Value (41 characters) exceeds Shippers.CompanyName
max length (40). (Parameter 'CompanyName')
```

You get the exact column and limit instantly, instead of a server
round-trip ending in a truncation error.

**3. Stable query plans.** Every sized parameter is emitted with an
explicit `DbParameter.Size` (and `Precision`/`Scale` for decimals). Without
this, SQL Server compiles one plan per distinct value length (`nvarchar(6)`,
`nvarchar(11)`, ...), polluting the plan cache. Write parameters use the
column's size (safe: the guard proved the value fits). Comparison
parameters size dynamically and are never truncated - a truncated WHERE
key could match the *wrong* row; an oversize key simply matches nothing.

Older snapshots without the metadata still work: the checks and sizing
just don't apply until you re-run `jauntyq schema pull`.

## Optimistic concurrency: rowversion

Add a `rowversion` column to a SQL Server table and re-pull the snapshot;
auto-CRUD turns it into a concurrency token automatically:

```csharp
Gadget? g = db.Gadgets.GetById(5);        // POCO carries byte[]? RowVersion
g.Name = "New name";
int affected = db.Gadgets.Update(g);      // WHERE gadget_id = @id AND row_version = @token
if (affected == 0)
{
    // conflict: someone updated (or deleted) the row after your read
}
```

The token is never inserted or updated - the database maintains it. It is
appended to the WHERE of both `Update` and `Delete`, so a stale read can't
overwrite or delete a newer write; 0 rows affected means conflict, and you
decide whether to re-read, merge, or give up. The version parameter has no
default: the signature forces you to pass it, because omitting it would
always look like a conflict.

Notes:
- After a successful Update the POCO's `RowVersion` is stale (the database
  assigned a new one). Re-fetch before updating the same object again.
- `Upsert` is deliberately last-writer-wins and ignores the token.
- SQL Server only for now. On other providers, model a manual version
  column in your own SQL (`set version = version + 1 where version = @version`).

## Performance analyzer

The snapshot now carries every table's indexes, and the generator runs a
static performance pass over your SQL. Eight warnings (never errors - the
query compiles; you just learn it will not be fast):

| Code | Fires when |
|------|-----------|
| JNT8001 | more tables than join conditions: possible cartesian product |
| JNT8002 | `where upper(col) = @p` - a function on the column defeats any index |
| JNT8003 | `like '%...'` - a leading wildcard cannot seek an index |
| JNT8004 | a filter/join column no index covers (needs index metadata; a composite index's non-leading column also counts when every column ahead of it in that index is filtered in the same query) |
| JNT8005 | two query files compile to identical SQL (formatting/case-insensitive fingerprint) - consolidate them |
| JNT8006 | a join's column pair matches no declared foreign key between the two tables |
| JNT8007 | `order by` names a column no index can order, forcing a runtime sort |
| JNT8008 | a child point-lookup by foreign key paired with a parent-collection query over that FK - an N+1 access pattern; join or batch with `IN` instead |

Example:

```
warning JNT8002: upper(products.product_name) in WHERE prevents index use:
the function runs on every row. Compute on the parameter side instead.

warning JNT8004: No index covers products.quantity_note used as a
filter/join key: this query scans. Add an index or filter on an indexed
column.
```

JNT8004 stays silent for snapshots that predate index capture (re-run
`jauntyq schema pull` to opt in), but it and every other JNT8xxx warning
apply to auto-CRUD synthetics too, e.g. a `GetBy<FkColumn>` loader filters
on the FK column, not the primary key, so an unindexed FK still warns.
Suppress any of these
per-project with standard analyzer configuration
(`dotnet_diagnostic.JNT8004.severity = none` in .editorconfig) if a scan
is intentional.

## Migrations: the build knows the future

Put pending DDL migrations in `db/migrations/`, ordered by filename:

```
db/migrations/0001_create_gadgets.sql
db/migrations/0002_drop_legacy_column.sql
```

JauntyQ applies them to the schema snapshot (a simulation - nothing touches
a database) and validates AND emits everything against that effective,
post-migration schema. Two consequences:

**Breaking migrations fail the build, not the deploy.** If migration 0002
drops a column one of your queries still selects, the build fails with the
usual JNT2002 - before anything ships. Shrinking `nvarchar(40)` to
`nvarchar(10)` makes previously-fine literals fail JNT5001. The migration
and the code that must survive it are checked as a unit.

**New tables get their API immediately.** A `create table` migration
produces the full typed surface - auto-CRUD, canonical POCO, identity
insert, rowversion concurrency, value-safety guards - before the table
exists anywhere. Write the migration, use the API, deploy both together.

Supported DDL: `CREATE TABLE` (column defs, facets, `NOT NULL`,
`PRIMARY KEY` inline or table-level, `IDENTITY`), `DROP TABLE [IF EXISTS]`,
`ALTER TABLE ADD / DROP COLUMN / ALTER COLUMN` (plus MySQL `MODIFY` and
PostgreSQL `TYPE`). Statements split on `;` and `GO`. Index, transaction
and permission statements are ignored (they cannot change the model);
anything else - renames in particular - is JNT9001, a warning that the
effective schema may be incomplete. Invalid DDL against the effective
schema (creating a table that exists, dropping one that does not) is
JNT9002, an error.

Convention: `db/migrations/` holds **pending** migrations only. After you
deploy one and re-run `jauntyq schema pull`, the snapshot embodies it -
archive the file (a re-applied CREATE will fail JNT9002 with a hint).
JauntyQ simulates migrations; running them against the database stays in
your deploy pipeline.

## Schema drift: verify on build

`schema verify` is a premium, entitlement-gated verb (`contract-testing`), without an installed, entitling license it prints an entitlement-required
message and exits `3` instead of running. See
[Licensing and activation](docs/03-guides/licensing-and-activation.md).

The snapshot is the contract; if the database drifts, the shape guard
turns that into runtime errors. Close the loop in CI or locally with:

```bash
jauntyq schema verify --provider sqlserver --connection "$CONN" \
    --output db/schema/jaunty.schema.json
# exit 0 = match, exit 2 = drift (differences listed)
```

Optional MSBuild wiring (opt-in, because CI often has no DB access, that's why the snapshot is checked in):

```xml
<Target Name="VerifyJauntySchema" BeforeTargets="Build"
        Condition="'$(JAUNTY_VERIFY_CONN)' != ''">
  <Exec Command="jauntyq schema verify --provider sqlserver --connection &quot;$(JAUNTY_VERIFY_CONN)&quot; --output db/schema/jaunty.schema.json" />
</Target>
```

## PostgreSQL: typed parameters, zero boxing

When the snapshot dialect is `postgres`, generated code binds parameters
as `NpgsqlParameter<T>` with `TypedValue` instead of the `object`-typed
`DbParameter.Value`:

```csharp
var p0 = new global::Npgsql.NpgsqlParameter<int> { ParameterName = "@product_id", TypedValue = product_id };
```

The value stays strongly typed through the ADO.NET boundary - the one
box per parameter that every reflection-free library still pays on the
generic API is gone. Npgsql infers the exact wire type from `T`, so no
`DbType` is set, and PostgreSQL's parameter typing is OID-based, so the
per-length plan-cache sizing emitted for SQL Server is unnecessary and
omitted. Client-side write guards (value safety) apply unchanged.

This makes Npgsql a compile-time dependency of projects using a postgres
snapshot - which they have at runtime anyway. Other dialects keep the
provider-agnostic `CreateParameter()` path.

## Supported providers

| `--provider` value | Database |
|---|---|
| `sqlserver` or `mssql` | SQL Server / Azure SQL |
| `postgres` or `postgresql` | PostgreSQL |
| `mysql` or `mariadb` | MySQL / MariaDB |
| `sqlite` | SQLite |

---

## Contributing

Bug reports, questions and suggestions are welcome as GitHub issues and discussions.
Code contributions are not accepted at present: the ISL-R grants no right to modify or
redistribute the software, and JauntyQ adopts no contributor agreement. If you have a
patch, describe it in an issue and Extrode will take it from there.

## License

JauntyQ is a commercial, source-available product with a free core and a paid team-safety tier.

- **This repository, the core** (generator, runtime, analysis, schema tooling and the
  `JauntyQ.Cli` tool) is licensed under the Islamic Software License - Restricted (ISL-R),
  Version 1.2. Text: [LICENSE.md](LICENSE.md), published at
  <https://islamiclicense.org/isl-r/1.2/LICENSE.md>.
- **Code JauntyQ generates into your project is yours**, under the Islamic Software License -
  Output Exception (ISL-OE), Version 1.2, adopted in [NOTICE.md](NOTICE.md): modify, compile,
  distribute and sell generated code, snapshot files and reports as part of your own
  applications, with no obligation to disclose source or reproduce the license text. The
  ethical use restrictions (Sections 4 and 5) continue to apply to what you build; that is
  deliberate, and it is the one way this differs from an ordinary compiler exception. Text:
  [EXCEPTION.md](EXCEPTION.md), published at <https://islamiclicense.org/isl-oe/1.2/EXCEPTION.md>.
- **The paid team-safety tooling** (migration impact analysis, database contract testing,
  the schema registry, usage export and live EXPLAIN) ships separately as
  `JauntyQ.Cli.Premium` under the Islamic Software End User License Agreement (ISL-EULA),
  Version 1.0, published at <https://islamiclicense.org/isl-eula/1.0/LICENSE.md>, with the
  grant conditioned on an Order.

Governing law is the Commonwealth of Virginia. Neither license is an open-source license.
Tiers and prices: [docs/00-overview/pricing.md](docs/00-overview/pricing.md) and
<https://extrode.com/jauntyq>.
