# Migration tracking contract

JauntyQ reads your migrations but never runs them. You apply them with your own
runner (a script, Flyway, DbUp, dbmate, anything). This page is what that runner
has to do so that it and JauntyQ agree about which migrations exist, in what
order, and which have been applied.

## Why JauntyQ does not apply migrations

Every other JauntyQ command that touches a database only reads it (`schema
pull`, `schema verify`). Applying a migration would be the first thing JauntyQ
does that changes a database, and a mistake there cannot be undone by running
it again. Plenty of tools already apply migrations well. What none of them do is
what JauntyQ does with a migration before it runs: check every query in your
project against the schema it produces, and report what it would break. JauntyQ
keeps to that half. Applying is left to you, and this contract covers the part
hand-written runners most often get wrong.

## What JauntyQ does with `db/migrations/`

- **Only pending migrations live there.** At build time the generator applies
  every `.sql` file under a `migrations` folder, in memory, on top of the
  committed schema snapshot, and checks your queries against the result. It
  does not know which files your database has already run. Once a migration is
  deployed and you re-pull the snapshot, the snapshot already contains its
  changes, so move the file out of `db/migrations/` (see
  [troubleshooting](troubleshooting.md#jnt9002-table-already-exists-on-a-migration)).
- **Order is by file name.** Files are sorted by their file name alone (the
  folder they sit in does not count). Runs of digits compare as numbers, so
  `V2__add_col.sql` comes before `V10__rename.sql`. Everything else compares
  character by character, case-sensitively. `0002_x.sql`, `002_x.sql` and
  `2_x.sql` are the same number; the one with more leading zeros sorts later.

## What your runner must do

1. **Apply in JauntyQ's order.** Sort the same way, or name files so every sort
   agrees: zero-padded numbers (`0001_`, `0002_`) or a fixed-width timestamp
   (`20260930_1200_`).
2. **Record each migration in a `schema_migrations` table**, one row per
   applied file, keyed by its file name.
3. **Write that row in the same transaction as the migration.** Begin a
   transaction, run the migration, insert its `schema_migrations` row, commit.
   If the row is written after the commit, a crash between the two leaves a
   migration applied but unrecorded, and the next run applies it twice. If it is
   written before, a failed migration is recorded as applied.
4. **Use a plain `INSERT`**, not an upsert, so two runners applying the same
   file at once make the second one fail and roll back instead of silently
   succeeding.

## The table

Two columns. `version` is the file name exactly as it appears in
`db/migrations/`, including `.sql`.

```sql
-- PostgreSQL
CREATE TABLE schema_migrations (
    version    text        PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

-- SQL Server
CREATE TABLE schema_migrations (
    version    nvarchar(255) NOT NULL PRIMARY KEY,
    applied_at datetime2     NOT NULL DEFAULT sysutcdatetime()
);

-- MySQL
CREATE TABLE schema_migrations (
    version    varchar(255) NOT NULL PRIMARY KEY,
    applied_at datetime(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
);

-- SQLite
CREATE TABLE schema_migrations (
    version    TEXT NOT NULL PRIMARY KEY,
    applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

Extra columns (who applied it, a checksum, run duration) are fine. Keep
`version` and `applied_at` as named.

## Where a transaction cannot help

- **MySQL commits on every DDL statement**, so a migration with several
  `ALTER`s that fails part-way leaves the earlier ones applied, and no runner can
  roll them back. Keep MySQL migrations to one schema change per file, and write
  the tracking row right after it.
- **Some statements refuse to run inside a transaction**, for example
  PostgreSQL's `CREATE INDEX CONCURRENTLY`. Put each such statement in a file of
  its own, run it outside a transaction, and record it only after it succeeds.

## Checking a database against this contract

`jauntyq migrate status` reads the `schema_migrations` table and compares it
with `db/migrations/`, using the table shape and file order above. It lists the
pending files in apply order and exits `2` on drift: a file that is recorded as
applied but still in the folder, or a pending file that sorts before the latest
applied one. `--fail-on pending` also fails on any pending file, for a deploy
gate. It only reads; see the [CLI reference](cli.md#migrate-status).
