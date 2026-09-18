# JauntyQ

<p align="center">
  <img src="docs/_assets/logo/jauntyq-mark.svg" alt="JauntyQ" width="160">
</p>

A compile-time SQL-to-C# generator. You write real `.sql` files; JauntyQ validates them at
build time against a committed schema snapshot and emits typed, ordinal-read C# with no
reflection and no runtime parsing.

[![CI](https://github.com/extrode/jauntyq/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/extrode/jauntyq/actions/workflows/ci.yml)
[![License: ISL-R](https://img.shields.io/badge/license-ISL--R%201.2-blue)](LICENSE.md)
[![Targets](https://img.shields.io/badge/targets-netstandard2.0%20%7C%20net8.0%20%7C%20net10.0-512BD4)](#installation)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-verified%20in%20CI-brightgreen)](#why-compile-time-sql)
[![Dependencies](https://img.shields.io/badge/dependencies-System.Text.Json%20only-informational)](#installation)
[![Providers](https://img.shields.io/badge/providers-SQL%20Server%20%7C%20PostgreSQL%20%7C%20MySQL%20%2F%20MariaDB%20%7C%20SQLite-informational)](#supported-providers)

> [!IMPORTANT]
> The core is free, no seat count, no trial clock, no telemetry: the generator, the runtime,
> auto-CRUD, and every build-time check (`JNT1xxx`-`JNT8xxx`). Code JauntyQ generates into your
> project is yours to ship under the Output Exception. A paid tier adds migration-impact
> analysis, contract testing and a schema registry for teams; see [License](#license).

You write the query once, as SQL:

```sql
-- db/tables/Products/GetByCategory.sql
select p.ProductId, p.ProductName, p.UnitPrice, c.CategoryName
from Products p
join Categories c on p.CategoryId = c.CategoryId
where p.CategoryId = @CategoryId
```

and call it like any other typed method:

```csharp
var db = new JauntyDb(connection);
var rows = db.Products.GetByCategory(1);

foreach (var row in rows)
    Console.WriteLine($"{row.ProductName} ({row.CategoryName})");
```

Rename `CategoryName` in the database, re-pull the snapshot, and this file stops compiling.
Nothing about the query is guessed at runtime: the parameter's `DbType`, the result columns'
nullability, the reader loop, all decided once, at build time, from a schema you committed.

---

## Contents

- [Quickstart](#quickstart)
- [Why compile-time SQL](#why-compile-time-sql)
- [JauntyQ vs. the alternatives](#jauntyq-vs-the-alternatives)
- [JauntyQ or Jaunty?](#jauntyq-or-jaunty)
- [Auto-CRUD, FK loaders, bulk](#auto-crud-fk-loaders-bulk)
- [Directives reference](#directives-reference)
- [Build-time guarantees](#build-time-guarantees)
- [Migrations](#migrations)
- [Benchmarks](#benchmarks)
- [Supported providers](#supported-providers)
- [Installation](#installation)
- [Documentation](#documentation)
- [License](#license)
- [Contributing](#contributing)

---

## Quickstart

```bash
dotnet tool install --global Extrode.JauntyQ.Cli
```

**1. Pull a schema snapshot**, a committed JSON file recording table and column names, types,
nullability, keys and indexes:

```bash
jauntyq schema pull \
  --provider sqlserver \
  --connection "Server=localhost;Database=MyDb;Trusted_Connection=true" \
  --output db/schema/jaunty.schema.json
```

**2. Wire up the generator**, either as a project reference inside this repo or from NuGet:

```xml
<ItemGroup>
  <AdditionalFiles Include="db\**\*.sql" />
  <AdditionalFiles Include="db\schema\*.schema.json" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Extrode.JauntyQ.Generator" Version="0.5.0" PrivateAssets="all" />
  <PackageReference Include="Extrode.JauntyQ.Runtime" Version="0.5.0" />
</ItemGroup>
```

**3. Write a query and build.** Drop a `.sql` file under `db/tables/<Entity>/<Method>.sql` and
the generator emits a typed method on that entity's partial class:

```csharp
using var conn = new SqlConnection(connectionString);
var db = new JauntyDb(conn);

var products = db.Products.GetAll();               // instance API
var products2 = Products.GetAll(conn);              // static fallback, no db object
var products3 = await db.Products.GetAllAsync(ct);   // every method has an async twin
```

Every table in the snapshot also gets `GetAll`, `GetById`, `Insert`, `Update` and `Delete` for
free with zero SQL written by you; see [Auto-CRUD](#auto-crud-fk-loaders-bulk).

---

## Why compile-time SQL

Every query lives in a `.sql` file, versioned next to the code that calls it. At build time the
generator parses it against your committed schema snapshot and emits typed C#: if a column is
renamed or a table drops, **the build breaks, not production.**

The generated code is performance-aligned by construction:

- Columns are read by ordinal (`GetInt32(0)`), never by name.
- Parameters bind with explicit `DbType` from the schema, no provider type inference at runtime.
- No reflection, no runtime SQL parsing, zero per-row boxing beyond the one unavoidable box every
  ADO.NET parameter pays at the `DbParameter.Value` boundary.
- A shape guard validates the actual result-set columns once per query, on first use, then never
  again.
- With no reflection or runtime codegen, the emitted code is **Native AOT and trim compatible**;
  `samples/Extrode.JauntyQ.Aot.Smoke` publishes to a self-contained native binary, verified in CI,
  running the full auto-CRUD, identity-return and `@stream` paths.

---

## JauntyQ vs. the alternatives

| | Hand-written ADO.NET | Dapper | EF Core | JauntyQ |
|---|:---:|:---:|:---:|:---:|
| You author the SQL | ✔ | ✔ | LINQ, not SQL | ✔ |
| Schema drift caught | never | at the call site, runtime | at the call site, runtime | **at the build**, against a snapshot |
| Column reads | ordinal | reflection | reflection/expression trees | ordinal, generated |
| Runtime SQL parsing | none | per-call, cached | translator, every call | **none, ever** |
| NativeAOT | yes | separate package | partial | in the box, verified in CI |
| Migration safety | manual | manual | manual | **build fails on a breaking migration** |
| Index/plan warnings | none | none | none | build-time, `JNT8xxx` |
| Oversize string literal | server round-trip | server round-trip | server round-trip | **build error**, `JNT5001` |

The row that matters most: everyone else finds out about a schema mismatch when a request hits
it. JauntyQ finds out when you press build, because the schema is a file you can diff, not a live
connection you hope agrees with your code.

---

## JauntyQ or Jaunty?

**JauntyQ starts from SQL. [Jaunty](https://github.com/extrode/jaunty) starts from C#.** Same
author, two products, not two modes of one. JauntyQ is for *I know SQL, I know what I want to
run, validate it and stay out of my way* — every query is a real `.sql` file, checked against a
schema snapshot at build time. Jaunty is the traditional-ORM answer: strict-by-default mapping at
the call site, an optional Fluent expression builder, and no snapshot to maintain. Neither product
silently maps the wrong thing; they just catch it at different moments. If the Jaunty description
sounds like you, its README covers that direction in the same depth.

---

## Auto-CRUD, FK loaders, bulk

For every table in the snapshot, JauntyQ synthesizes five methods with zero SQL written by you:

| Method | SQL emitted |
|---|---|
| `GetAll` | `SELECT <all columns> FROM <Table>` |
| `GetById` | `SELECT ... WHERE <pk> = @Id` |
| `Insert` | `INSERT INTO ...`, dialect-native identity return on a single-identity-column table |
| `Update` | `UPDATE ... SET ... WHERE <pk> = @Id` |
| `Delete` | `DELETE FROM ... WHERE <pk> = @Id` |

A `.sql` file with the matching entity + method name overrides the synthetic one. Foreign keys
in the snapshot also generate a loader per FK column (`db.Products.GetByCategoryId(1)`), tables
with a non-identity key get a dialect-native `Upsert` (MERGE / `ON CONFLICT` / `ON DUPLICATE KEY`),
and every table with a synthetic `Insert` gets a `BulkInsert` that uses each dialect's native
bulk-copy API where one exists (`SqlBulkCopy`, PostgreSQL binary `COPY`, `MySqlBulkCopy`) and a
single-transaction prepared-command loop elsewhere:

```csharp
List<Product> beverages = db.Products.GetByCategoryId(1);      // FK loader
int affected = db.Customers.Upsert("ALFKI", "Alfreds Futterkiste", ...);
int inserted = db.Products.BulkInsert(newProducts);             // one transaction
```

To disable auto-CRUD project-wide: `<JauntyQAutoCrud>false</JauntyQAutoCrud>`.

---

## Directives reference

Directives are `--` comment lines at the top of a `.sql` file, stripped from the emitted
`CommandText`.

| Directive | Effect |
|---|---|
| `-- @first` | Return `Row?` instead of `List<Row>`. |
| `-- @stream` | Return `IEnumerable<Row>` / `IAsyncEnumerable<Row>`, constant memory over large result sets. SELECT-only. |
| `-- @result void` | Returns `int` (row count); for INSERT/UPDATE/DELETE with no SELECT. |
| `-- @result TypeName` | Use an existing type as the DTO instead of generating one. |
| `-- @result (int Id, string Name)` | Inline column list, generates an anonymous-style DTO. |
| `-- @params Name:type, ...` | Explicit parameters when the parser can't infer them. |
| `-- @each ParamName` | Expands a parameter into a runtime `IN`-list at call time. Repeatable, SELECT-only. |
| `-- @type alias dbtype` | Declares the type of a projected expression the schema can't infer (aggregates, computed columns). |
| `-- @proc [Name]` | Emits a `CommandType.StoredProcedure` call; a `CREATE OR ALTER PROCEDURE` on SQL Server, generated from the SQL. |
| `-- @call ProcedureName` | Binds to a procedure that already exists in the database; params and result columns come from the snapshot. |
| `-- @identity` | INSERT returns the typed database-assigned id (`OUTPUT INSERTED.<col>` / `RETURNING` / `LAST_INSERT_ID()`) instead of a row count. Auto-CRUD applies this automatically on single-identity tables. |
| `-- @mirrors OtherQuery` | Declares this query's WHERE must match another query's, and has the build check it (`JNT8011`) — for a paginated list and its count query drifting apart. |
| `-- @allow-unindexed <reason>` | Accepts an unindexed filter column deliberately, suppressing `JNT8004` for this query only. Reason is mandatory. |
| `-- @allow-sort <reason>` | Accepts a runtime sort deliberately, suppressing `JNT8007` for this query only. Reason is mandatory. |
| `-- @allow-n-plus-one <reason>` | Accepts a point-lookup query deliberately, suppressing `JNT8008` for this query only. Reason is mandatory. |

A parameter type the parser can't infer from the SQL is a build error (`JNT4003`) naming the exact
fix, never a silent `object` fallback.

---

## Build-time guarantees

**Value safety.** The snapshot carries every column's max length and decimal precision, checked
three ways. A literal that can't fit its column fails the build (`JNT5001`/`JNT5002`). A generated
write method validates parameter lengths before touching the connection. And every sized parameter
carries an explicit `DbParameter.Size`, so SQL Server doesn't fragment its plan cache on
`nvarchar(6)`, `nvarchar(11)`, ...

**Performance analyzer.** Eight warnings, never errors, run as a static pass over your SQL using
the snapshot's index metadata:

| Code | Fires when |
|---|---|
| `JNT8001` | more tables than join conditions — possible cartesian product |
| `JNT8002` | a function wraps the filtered column, defeating any index |
| `JNT8003` | a leading `%` wildcard can't seek an index |
| `JNT8004` | a filter/join column no index covers |
| `JNT8005` | two query files compile to identical SQL |
| `JNT8006` | a join's column pair matches no declared foreign key |
| `JNT8007` | `order by` names a column no index can order |
| `JNT8008` | a child point-lookup paired with a parent-collection query over the same FK — an N+1 shape |

**Rowversion concurrency.** Add a `rowversion` column on SQL Server and re-pull; auto-CRUD turns
it into a concurrency token automatically, appended to the `WHERE` of `Update`/`Delete` so a stale
read can't clobber a newer write.

---

## Migrations

Put pending DDL in `db/migrations/`, ordered by filename. JauntyQ simulates them against the
schema snapshot, nothing touches a database, and validates and emits everything against that
effective, post-migration schema:

```
db/migrations/0001_create_gadgets.sql
db/migrations/0002_drop_legacy_column.sql
```

**Breaking migrations fail the build, not the deploy.** Drop a column a query still selects, and
the build fails with `JNT2002` before anything ships. **New tables get their full typed surface**
immediately: auto-CRUD, the canonical POCO, identity insert, value-safety guards, before the table
exists anywhere. Write the migration, use the API, deploy both together.

Schema drift between deploys is closed with `jauntyq schema verify` in CI (a paid, entitlement-gated
verb): exit `0` on match, `2` on drift, with the differences listed.

---

## Benchmarks

BenchmarkDotNet, Release, real in-memory SQLite (no Docker), measuring the actual generated code
path against the suite's own `Widgets` table. Run 2026-09-16 on an AMD Ryzen 7 7840HS (8 physical
cores), BenchmarkDotNet v0.14.0, .NET 8.0.31.

**Point lookups don't care how big the table is.** `GetById` (`@first`) reads one row off the
reader with no list allocation, so it costs the same whether the table has 10 rows or 100,000:

| Method | Rows | Mean | Allocated |
|---|---:|---:|---:|
| `GetAll` | 10 | 12.2 us | 2.23 KB |
| `GetById` (`@first`) | 10 | 4.9 us | 1.15 KB |
| `GetAll` | 1,000 | 819.3 us | 148.9 KB |
| `GetById` (`@first`) | 1,000 | 5.0 us | 1.15 KB |
| `GetAll` | 100,000 | 105.7 ms | 15.3 MB |
| `GetById` (`@first`) | 100,000 | 5.0 us | 1.15 KB |

**`@stream` stops paying once it has what it needs.** Reading the first 100 rows off a
100,000-row table costs the same whether the table has 1,000 rows or 100,000 — the buffered path
pays for the whole `List<T>` regardless:

| Method | Rows | Mean | Allocated |
|---|---:|---:|---:|
| Buffered, read first 100 | 1,000 | 835.2 us | 148.9 KB |
| `@stream`, read first 100 | 1,000 | 87.6 us | 13.3 KB |
| Buffered, read first 100 | 100,000 | 91.0 ms | 15.3 MB |
| `@stream`, read first 100 | 100,000 | 80.3 us | 13.3 KB |

That's a 1,133x difference at 100,000 rows for the identical result: `@stream` never buffers past
the rows you actually consume. Consuming the *entire* stream is close to the buffered cost, since
both read every row either way, with `@stream`'s smaller `Gen2`-free footprint as the only edge.

**The build-time tokenizer is cheap.** The generator re-tokenizes every `.sql` file on each
design-time build; 100 queries costs 132 us total, well under the noise floor of an IDE keystroke:

| Method | Mean | Allocated |
|---|---:|---:|
| Tokenize one query | 1.3 us | 3.5 KB |
| Tokenize 100 queries | 132.3 us | 349.2 KB |

Reproduce with `dotnet run -c Release --project benchmarks/Extrode.JauntyQ.Benchmarks -- --filter '*'`;
methodology in [the suite's own README](benchmarks/Extrode.JauntyQ.Benchmarks/README.md).

---

## Supported providers

| `--provider` value | Database |
|---|---|
| `sqlserver` or `mssql` | SQL Server / Azure SQL |
| `postgres` or `postgresql` | PostgreSQL |
| `mysql` or `mariadb` | MySQL / MariaDB |
| `sqlite` | SQLite |

PostgreSQL gets one extra: generated code binds `NpgsqlParameter<T>` with `TypedValue` instead of
the `object`-typed `DbParameter.Value`, so the one unavoidable box every other dialect pays at the
ADO.NET boundary is gone there too.

---

## Installation

```bash
dotnet tool install --global Extrode.JauntyQ.Cli
```

```xml
<PackageReference Include="Extrode.JauntyQ.Generator" Version="0.5.0" PrivateAssets="all" />
<PackageReference Include="Extrode.JauntyQ.Runtime" Version="0.5.0" />
```

Targets `netstandard2.0`, `net8.0` and `net10.0`. The generator installs as a Roslyn analyzer automatically;
no `OutputItemType` wiring needed on the package reference.

---

## Documentation

- [Directives reference](docs/06-reference/directives.md) and the [CLI reference](docs/06-reference/cli.md)
- [Schema snapshot format](docs/06-reference/schema-snapshot-format.md) and [supported SQL](docs/06-reference/supported-sql.md)
- [Licensing and activation](docs/03-guides/licensing-and-activation.md)
- [Pricing](docs/00-overview/pricing.md)

---

## License

JauntyQ is a commercial, source-available product with a free core and a paid team-safety tier.

- **This repository, the core** (generator, runtime, analysis, schema tooling, the
  `Extrode.JauntyQ.Cli` tool) is licensed under the Islamic Software License - Restricted (ISL-R),
  Version 1.2. Text: [LICENSE.md](LICENSE.md), published at
  <https://islamiclicense.org/isl-r/1.2/LICENSE.md>.
- **Code JauntyQ generates into your project is yours**, under the Islamic Software License -
  Output Exception (ISL-OE), Version 1.2, adopted in [NOTICE.md](NOTICE.md): modify, compile,
  distribute and sell generated code, snapshots and reports as part of your own applications, with
  no obligation to disclose source or reproduce the license text. Text:
  [EXCEPTION.md](EXCEPTION.md), published at <https://islamiclicense.org/isl-oe/1.2/EXCEPTION.md>.
- **The paid team-safety tooling** (migration impact analysis, contract testing, the schema
  registry, usage export, live EXPLAIN) ships as `Extrode.JauntyQ.Cli.Premium` under the Islamic
  Software End User License Agreement (ISL-EULA), Version 1.0, published at
  <https://islamiclicense.org/isl-eula/1.0/LICENSE.md>. While JauntyQ is `0.x`, these run free
  under a Preview license issued on request. Once you're on a real subscription, a lapse never
  breaks a build either — it prints a renewal reminder, and updates and support resume on renewal.

Neither license is open-source. Tiers and prices: [pricing](docs/00-overview/pricing.md).

---

## Contributing

Bug reports, questions and suggestions are welcome as GitHub issues and discussions. Code
contributions aren't accepted at present: the ISL-R grants no right to modify or redistribute the
software, and JauntyQ adopts no contributor agreement. If you have a patch, describe it in an
issue and Extrode will take it from there.
