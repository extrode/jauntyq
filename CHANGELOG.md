# Changelog

All notable changes to JauntyQ's core packages: `JauntyQ.Generator`, `JauntyQ.Runtime`,
`JauntyQ.SqlParser`, `JauntyQ.Schema`, `JauntyQ.Analysis`, `JauntyQ.Schema.Extraction`,
`JauntyQ.Cli.Core` and the `JauntyQ.Cli` tool. The paid tooling in `JauntyQ.Cli.Premium`
keeps its own changelog.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions
before 0.5.0 were released from the original repository; their entries below are
condensed.

## [Unreleased]

### Added
- **This repository.** The core is public from 0.5.0, under the Islamic Software License -
  Restricted (ISL-R) 1.2 with the Output Exception (ISL-OE) 1.2. `LICENSE.md`, `EXCEPTION.md`
  and `NOTICE.md` ship inside every package. The history behind this release is the core's real
  history, filtered before publication; see
  `docs/decisions/2026-09-02-001-history-filtered-before-first-public-release.md`.
- **The CLI is two tools behind one command, `jauntyq`.** `JauntyQ.Cli` on NuGet.org carries
  `schema pull`; `JauntyQ.Cli.Premium` carries everything else and replaces it. Both are
  .NET global tools.
- **New packages `JauntyQ.Schema.Extraction` and `JauntyQ.Cli.Core`.** The database extractors
  moved into their own package so that `schema pull` needs nothing paid, and `Schema`,
  `SqlParser` and `Analysis` are published as packages in their own right.
- **Scalar database functions are captured and callable**: `db.Functions.CalcTax(100m, 0.25m)`,
  typed from the snapshot, on PostgreSQL, SQL Server and MySQL.
- **Alias and DOMAIN types are recorded**, and columns declared through them say so.
- **Three diagnostics for the functions the generator will not emit**: JNT2023, JNT2024 and
  JNT2025 name the function and the reason instead of emitting nothing.
- **A differential query suite across all five engines**, and a divergence-stressing corpus
  that runs the same SQL through every dialect the generator supports.
- **Trusted publishing.** Releases go to NuGet.org through NuGet's OIDC login from a reviewed
  GitHub environment; no publishing key exists to leak.

### Changed
- **The command is `jauntyq`, not `jaunty`.** The old name collided with a sibling product.
- **`schema pull` accepts only its own four flags**; a stray flag is an error, not silence.
- **Packages carry `LICENSE.md` (ISL-R 1.2)** as their license file, with `EXCEPTION.md` and
  `NOTICE.md` beside it, and no longer require license acceptance on install.

### Fixed
- **A SQL Server table-valued parameter no longer collapses onto a shared pseudo-type.**
- **A live server with no sample schema now skips rather than fails**, and a live-server skip
  is for "nothing was asked for", never for a server that is down.

## [0.4.0] - 2026-08-26

### Changed
- **The project's identity moves to extrode.com.** Package metadata, license texts and the
  README name Extrode LLC.
- `schema verify --format json` no longer prints prose ahead of its JSON document.

### Added
- **`*.accept.json`**, a per-column opt-out for `JNT8004` on auto-CRUD queries, with `JNT6003`
  for a sidecar the build cannot act on and `JNT8012` for a dead sidecar entry.
- **`JNT3011`**, a non-repeatable directive written twice in one `.sql` file.
- **`-- @allow-sort <reason>`**, a per-query opt-out for `JNT8007` (unindexed `ORDER BY`).
- **`JNT2022` "Lossy Identifier Rename"** (Warning) and **`JNT5003` "Literal Type Mismatch"**
  (Error).

### Fixed
- **`JAUNTYQ_CONNECTION` is named once**, and both verbs that read it share the same
  resolution. `JAUNTYQ_VERBOSE` is read in one place instead of three.
- **`--provider mariadb`** works on every verb that takes a provider, not only some.
- **T-SQL's `TOP n PERCENT`** modeled `percent` as a projected column, stealing the real first
  column's name and type; `TOP n PERCENT` and `WITH TIES` are now skipped.
- **PostgreSQL materialized-view columns** carried no type facets and spelled array types
  differently from table columns.
- **PostgreSQL's `DISTINCT ON (...)`** silently mis-modeled the first selected column; it is now
  modeled.
- **JNT1002 blamed a missing quote** when the real cause was a MySQL backslash escape.
- **`bit` outside MySQL was validated by nothing at all.**
- CI's vulnerable-package gate had been failing on every push and was repaired.

## [0.3.0] - 2026-08-17

### Added
- **Views and materialized views are captured in the schema snapshot**, with `JNT2021` for a
  write to a view.
- **`-- @allow-unindexed <reason>`** directive.
- **`JNT1009`, unsupported syntax**, replacing the silence for grammar the parser does not model.
- **[Supported SQL surface reference](docs/06-reference/supported-sql.md).**

### Changed
- **Breaking: `JNT2001` no longer covers missing grammar.** That is `JNT1009`.
- Directive names may contain `-` after the first letter.
- `INTERSECT` and `EXCEPT` are tokenizer keywords and are refused as `JNT1006`.
- `JNT8004`, `JNT8006` and `JNT8007` stay silent on views, which declare no indexes.

### Fixed
- `LATERAL` was only guarded in the join position.
- `JNT8012` fired when no index analysis had run.
- `SchemaSimulator.Apply` dropped `IsView`.
- `MySqlExtractor` had no live view test, and now has one.

## [0.2.0] - 2026-08-17

### Added
- **JNT8009, unstable pagination**, and **JNT8010, unordered pagination.**
- **`-- @mirrors <Query>` and JNT8011, predicate drift**, with JNT3010 for a mirrored query
  that is not comparable. Predicate atoms joined the IR to make it possible.
- **JNT1008**, more than one SQL statement in a file is an error.
- **PostgreSQL and MySQL enum capture.** Database enums are emitted as real C# enums, a value
  the snapshot does not contain throws `JauntyQEnumValueException`, and a MySQL `ENUM(...)`
  column now maps to a generated enum (breaking, pre-1.0). MySQL `SET` columns still map to
  `string`.
- **Unique expression indexes are represented** and count as competing keys; **MySQL prefix
  key parts are captured**, and a prefix-only upsert key is refused with `JNT2019` instead of
  emitted wrong.
- **68-case hostile-SQL battery**, plus the Chinook (SQLite), Pagila (PostgreSQL) and
  AdventureWorks (SQL Server) sample suites.

### Changed
- `count(...) OVER (...)` is typed like the plain aggregate call: `bigint`, NOT NULL.
- JNT3006 no longer blames a spelling mistake for a CTE-projected alias.
- Test fixtures share one container per assembly, and a green full-solution run now means
  everything ran.

### Fixed
- **MySQL upserts target the resolved conflict key.** A table with more than one UNIQUE
  constraint emits a key-targeted `UPDATE ...; INSERT ... SELECT ... FROM DUAL`, with `JNT2018`
  when no key can be resolved.
- **A MySQL `BIT(n)` column is range-checked on its true 0..2^n-1**, and a `TINYINT(1)` column
  no longer draws a false `JNT5002`.
- **A decimal literal with more fractional digits than its column's scale is an error**, and
  JNT5001 no longer rejects valid SQL on PostgreSQL, MySQL and SQLite.
- A trailing space no longer silently disables `-- @first`, `-- @identity` or `-- @stream`;
  a tab between a directive's name and its value is honored; a bare `-- @call` reports
  JNT3008; `-- @type <alias>` with no db type is no longer ignored; `-- @first` on a plain
  INSERT/UPDATE/DELETE is JNT3003.
- JNT2019/JNT2020 no longer fire when you have written the Upsert yourself; JNT2015 and
  JNT2014 no longer claim generated API surface is absent when it is present.
- A pending migration no longer erases your database enums, and JNT8004 no longer goes quiet
  on a range scan.
- JNT2007 no longer fires on enum-linked columns in auto-CRUD, and generated row POCOs no
  longer leak CS8618 into consumer builds.
- `-- @proc` scaffolds declare a decimal parameter at the bound column's real precision.
- Ten diagnostic codes had no severity assertion; the diagnostic and directive registries are
  now checked for membership drift on every test run.
- A schema snapshot containing the JSON literal `null` is a parse failure, not a silent empty
  schema.

## [0.1.0] - 2026-07-07

The first release: the generator, the runtime, the parser and the CLI.

### Added
- Static variants can enlist in a caller-managed transaction.
- Quoted-identifier tokenization.
- `JNT6002` (warning): multiple schema snapshots.
- `-- @each` oversize-list guard, and argument null guards on the generated surface.
- CLI accepts `mariadb`.
- Diagnostics and API reference docs.
- Sequence objects (SQL Server and PostgreSQL).
- DDL as schema source.
- Bulk insert, with provider-native fast paths (Npgsql binary `COPY`, `SqlBulkCopy`,
  `MySqlBulkCopy`).
- Stored-procedure consumption (`-- @call`).
- Native AOT and trim compatibility, with a published smoke app in CI.
- `-- @stream` directive.
- NuGet package metadata and versioning, deterministic builds with SourceLink, a release
  SBOM, and CI with build (warnings as errors), test and a vulnerable-package scan.
- `SECURITY.md` and this changelog.

### Security
- Build-time code-injection defenses: identifiers derived from SQL aliases, file paths,
  schema names and parameter names are validated or neutralized before they reach emitted C#
  (`JNT2004`), and embedded string literals are encoded.
- CLI secret hygiene: connection strings via `--connection-env` or `JAUNTYQ_CONNECTION`,
  redacted error output by default.
- CLI path containment for every path the tool writes.

### Fixed
- Columns named after C# keywords no longer generate uncompilable code.
- `JNT3008` (warning): directive-lookalike comments that parse as nothing.
- `-- @proc` / `-- @call` no longer prefix-match ordinary comments.
- `-- @each` on INSERT/UPDATE/DELETE or with `-- @proc` fails with a diagnostic, and `-- @each`
  expansion no longer rewrites matches inside string literals.
- Unterminated string literals stop tokenization with `JNT1002`, unterminated `/* comment` or
  `[bracket identifier]` fails the build, and the tokenizer caps input size.
- An unknown `--provider` prints a clean one-line error and exits 1.
- `JNT8005`/`JNT8008` diagnostics carry a file location.
- MSBuild knobs reach the generator in real consumer projects.
- Auto-CRUD `Upsert` synthesis no longer skips tables with an alternate key, synthesized
  queries surface `JNT8xxx` performance warnings, and `-- @each` parameters get
  `DbParameter.Size` set per element.
- Unrecognized dialect strings fail the build (`JNT7003`) instead of falling back.
- `JNT8004` composite-index coverage no longer false-positives on a covered prefix.
- CRUD parameter resolution no longer ignores explicit alias qualification.
- The shape guard runs once per query.

### Performance
- `UsageManifest` construction is O(n).
- Tokenizer allocations reduced.
