# Sequences

For databases with true sequence objects, JauntyQ generates typed accessors that
advance a sequence and return the next value. Supported on **SQL Server**,
**PostgreSQL**, and **MariaDB** (via its `CREATE SEQUENCE` support since 10.3).
Real/Oracle MySQL and SQLite have no sequence object, so their snapshots carry
no sequences and no accessors are generated. MariaDB shares the `mysql`
dialect string with real MySQL, but is detected separately by the extractor
and generator so its sequences are still picked up.

## How it works

`jauntyq schema pull` captures every sequence object into the schema snapshot
(SQL Server via `sys.sequences`, PostgreSQL via `information_schema.sequences`
joined to `pg_sequences`, MariaDB via its `information_schema.sequences`-style
catalog), recording its start value, increment, min/max, and current value.

When the snapshot has sequences, the generator emits a lazily-constructed
`db.Sequences` accessor with one method per sequence, named `Next{Name}()`
(plus an async twin). The name is PascalCased from the sequence name.

```csharp
var db = new JauntyDb(conn);

long id  = db.Sequences.NextOrderNumber();
long id2 = await db.Sequences.NextOrderNumberAsync(cancellationToken);
```

Each method returns `long` (the newly advanced value) and runs the
dialect-native statement:

| Dialect | Emitted SQL |
|---|---|
| SQL Server | `SELECT NEXT VALUE FOR {name}` |
| PostgreSQL | `SELECT nextval('{name}')` |
| MariaDB | `SELECT NEXTVAL({name})` |

## Transactions and connection lifecycle

`db.Sequences.Next*` follows the same rules as every other `db.*` call:

- It enlists in the ambient `JauntyDb` transaction if one is active (via
  `BeginTransaction()`), so a sequence value drawn inside a transaction is
  covered by it.
- It opens the connection if closed and closes it again afterward; if the
  connection is already open, it is left open.

Note that in most databases sequence advancement is **not** rolled back with a
transaction: rolling back after drawing a value typically leaves a gap in the
sequence. That is standard sequence behavior, not a JauntyQ choice.

## When it appears

The `db.Sequences` accessor is only generated when the snapshot actually
contains sequences. Add a sequence to the database, re-run `jauntyq schema pull`,
and rebuild to pick it up.
