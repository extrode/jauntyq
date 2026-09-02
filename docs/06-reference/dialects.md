# Dialect differences

JauntyQ supports four dialect families. The dialect is fixed per project, it
comes from the snapshot's `dialect` field (or `JauntyQDialect` in DDL mode) and
determines the SQL the generator emits for synthesised operations. Your
hand-written query SQL is emitted verbatim; the differences below apply to
generated CRUD, identity return, upsert, sequences, and bulk insert.

## Identifiers and dialect strings

| Dialect | Snapshot `dialect` | CLI aliases | MariaDB |
|---|---|---|---|
| SQL Server / Azure SQL | `sqlserver` | `sqlserver`, `mssql` |, |
| PostgreSQL | `postgres` | `postgres`, `postgresql` |, |
| MySQL | `mysql` | `mysql` | Use `mysql` |
| SQLite | `sqlite` | `sqlite` |, |

MariaDB is wire- and SQL-compatible with MySQL; declare its snapshot dialect as
`mysql`. An unknown dialect string in a snapshot is `JNT7003`. The CLI also
accepts `mariadb` as a provider alias and writes `mysql` into the snapshot.

Quoted identifiers, `[brackets]`, `"double quotes"`, and `` `backticks` ``, are tokenized and validated in hand-written queries. Auto-CRUD synthesis and
bulk insert, however, emit **unquoted** identifiers, so tables that *require*
quoting to resolve (e.g. a PostgreSQL table created with a quoted mixed-case
name, or names with spaces) are not supported by the synthesized surface, write those queries by hand. (On MySQL, note that JauntyQ follows ANSI rules
and treats a double-quoted token as an identifier, not a string literal; use
single quotes for strings.)

## Parameters

All dialects use **named parameters** (`@Name`) in the emitted SQL; the ADO.NET
provider translates them to its native placeholder style. On PostgreSQL,
generated code binds through `NpgsqlParameter<T>` (typed, no boxing on the
generic path, no `DbType`, Npgsql infers the OID); the other dialects use the
provider-agnostic `CreateParameter()` path with an explicit `DbType` and, for
sized types, `Size`/`Precision`/`Scale`.

## Feature matrix

| Feature | SQL Server | PostgreSQL | MySQL / MariaDB | SQLite |
|---|---|---|---|---|
| Identity return (`-- @identity`) | `OUTPUT INSERTED.<col>` | `RETURNING <col>` | `SELECT LAST_INSERT_ID()` (narrowed) | `RETURNING <col>` |
| Upsert | `MERGE ... WITH (HOLDLOCK)` | `ON CONFLICT (...) DO UPDATE` | `ON DUPLICATE KEY UPDATE` | `ON CONFLICT (...) DO UPDATE` |
| Sequences | `NEXT VALUE FOR <seq>` | `nextval('<seq>')` | `NEXTVAL(<seq>)` (MariaDB only; real MySQL has no sequence object) | not supported |
| Bulk insert | `SqlBulkCopy` | binary `COPY` | `MySqlBulkCopy` | transactional prepared loop |

### Identity return

MySQL's `LAST_INSERT_ID()` returns `BIGINT UNSIGNED`; the generated code
narrows it (`checked`) to the identity column's C# type. See
[`-- @identity`](directives.md#-identity).

### Upsert

Synthesised for tables that have a non-identity key to match on (primary key,
else the first full-column secondary unique index). Identity-only keys with no
such index skip upsert. A prefix unique (MySQL/MariaDB `SUB_PART`) never
qualifies as the key, when it is the only candidate, synthesis is refused
with [JNT2019](diagnostics.md); an expression unique index is likewise never
the key and always counts as a competing constraint for
[JNT2018](diagnostics.md). MySQL/MariaDB uses the legacy `VALUES(col)` form for
broad compatibility, and its affected-row count is server semantics (1 insert,
2 update), do not assert `== 1`. SQLite uses the same `ON CONFLICT` form as
PostgreSQL (SQLite 3.24+). See the [bulk insert guide](../03-guides/bulk-insert.md)
and [sequences guide](../03-guides/sequences.md) for the non-CRUD paths.

### Bulk insert

Each dialect uses its provider's native bulk path where one exists; SQLite and
any other provider fall back to a single-transaction prepared-command loop.
MySQL's `MySqlBulkCopy` requires `AllowLoadLocalInfile=true` in the connection
string and `local_infile` enabled server-side. Identity and rowversion columns
are excluded from bulk insert on every dialect. Details in the
[bulk insert guide](../03-guides/bulk-insert.md).

## Type mapping

Type mapping is uniform across dialects, a database type name maps to the same
C# type regardless of dialect, with a few dialect-specific inputs:

| C# type | Representative DB types |
|---|---|
| `int` / `long` | `int`/`int4`/`serial`; `bigint`/`int8`/`bigserial`; SQLite `integer` |
| `string` | `varchar`, `nvarchar`, `text`, `character varying`, `citext` |
| `decimal` | `decimal`, `numeric`, `money` |
| `System.DateTime` | `datetime`, `datetime2`, `timestamp[ without time zone]` |
| `System.Guid` | SQL Server `uniqueidentifier`, PostgreSQL `uuid` |
| `byte[]` | `varbinary`, `binary`, `image`, `bytea`, `blob` |
| `byte[]?` (concurrency token) | SQL Server `rowversion`/`timestamp` |
| `T[]` | PostgreSQL array types (`text[]`, `integer[]`), PostgreSQL only |
| `System.Net.IPAddress` | PostgreSQL `inet`, `cidr` |
| `string` | `json`, `jsonb` (PostgreSQL), the raw text, not a parsed document |
| `byte` / `short` | `tinyint`, dialect-dependent, see below |
| `bool` / `object` | `bit`, length-dependent, see below |

A database type with no mapping falls through to `object` and raises `JNT2007`;
declare the intended type with [`-- @type`](directives.md#-type) or a
`-- @result` shape.

**`tinyint` is dialect-aware.** SQL Server's `tinyint` is unsigned 0–255 and
the provider (`Microsoft.Data.SqlClient`) hands it back as `System.Byte`, so
`sqlserver` maps it to `byte`. MySQL/MariaDB's `tinyint` is signed by default
and round-trips through `GetInt16` fine, so it keeps the historical `short`
mapping - **except `tinyint(1)`** (including the `BOOLEAN`/`BOOL` DDL synonym,
which the server desugars to `tinyint(1)`), which maps to `bool` instead,
matching `MySqlConnector`'s default "Treat Tiny As Boolean" behavior.

**`bit(n)` degrades to `object` when `n > 1`.** SQL Server's `BIT` is always
single-bit and maps to `bool`. MySQL and PostgreSQL both allow a multi-bit
`BIT(n)`, which neither `MySqlConnector` (returns `ulong`) nor Npgsql (returns
`BitArray`) hands back as a `bool`, mapping it to `bool` would silently
corrupt or throw at the generated cast, so a `bit`/`bit[]` column with a
declared length greater than 1 maps to `object`/`object[]` instead. `bit
varying` was already unmapped (no case matches the string) and stays that way.

## Dialect-specific notes

- **PostgreSQL** is the only dialect with array types and the typed
  `NpgsqlParameter<T>` path; it is therefore a compile-time dependency of
  projects using a postgres snapshot.
- **SQL Server** is the only dialect with `rowversion`-based optimistic
  concurrency and per-length parameter sizing for plan-cache stability.
- **MySQL/MariaDB** has no sequence objects and no `rowversion`; bulk insert
  needs `LOCAL INFILE` enabled on both ends.
- **SQLite** has no sequences and no `rowversion`; bulk insert uses the
  portable loop. `INTEGER PRIMARY KEY` is the identity mechanism.
