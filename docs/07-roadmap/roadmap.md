# JauntyQ Roadmap

Living list of post-baseline work. Items are grouped by whether they are being
built now, planned next, or deferred. Non-goals are recorded so scope stays
honest.

## In progress / next
(nothing currently in progress)

## Planned (spec-first)
The differentiator ("moat") specs authored 2026-07-10 (007–012) have all been
**built and merged to `dev`** (2026-07-11), see **Shipped** below. Nothing else is
currently queued.

Under evaluation (built, ship/hold decision pending):
- **License enforcement.** Signed offline `jaunty.license.json`, `jauntyq activate`
  and entitlement gating are built and live in `Extrode.JauntyQ.Cli.Premium`: `schema verify`,
  `migrate impact`, `explain`, `registry` and `usage export` consult the gate. The
  core codegen is never gated, and a lapse only warns, never hard-fails. What remains
  is commercial rather than technical. See
  `docs/03-guides/licensing-and-activation.md`.

Dropped (for SQL Server):
- **Bespoke Visual Studio extension for SQL Server.** Visual Studio ships
  native SQL Server support (SQL Server Object Explorer, T-SQL IntelliSense,
  live database validation) out of the box. Building a JauntyQ-specific
  extension would duplicate that for the one dialect VS already covers well.
  Not worth building.

Under consideration (future, not now — no commitment):
- **Bespoke Visual Studio extension for PostgreSQL, MySQL/MariaDB, and
  SQLite.** Unlike SQL Server, Visual Studio has no built-in editing or live
  validation for these three dialects; support today comes only from
  third-party extensions of uneven quality, maintenance, and pricing. A
  JauntyQ-authored extension giving these dialects the same `.sql` editing +
  live-validation experience SQL Server users get natively is a real gap and
  may be worth building later. No commitment, no timeline.

  **Scoping (2026-09-19, training-knowledge based, not a live web search —
  verify before spec'ing):**
  - *Reusable infra*: `Extrode.JauntyQ.SqlParser` + `Extrode.JauntyQ.Analysis`
    are already offline, Roslyn-free, dialect-aware SQL tokenizer/parser/
    validator libraries used today for build-time diagnostics. They lower the
    floor for autocomplete/inline-validation logic; the missing pieces are
    editor integration and live schema introspection, not SQL understanding.
  - *Landscape (VS-native, not VS Code — different extensibility model)*: no
    maintained free/OSS VS extension reaches SQL Server-level parity for any
    of the three dialects. Postgres and MySQL/MariaDB have only aging or
    commercial options (e.g. Oracle's "MySQL for Visual Studio", dbForge);
    SQLite has more actively maintained community tooling (e.g. SQLite/SQL
    Server Compact Toolbox) but scoped to data browsing, not schema-aware
    IntelliSense. Full IDEs (DBeaver, DataGrip, Azure Data Studio) cover all
    three well but are separate products, not VS extensions.
  - *Architecture*: VS's newer LSP client support
    (`Microsoft.VisualStudio.LanguageServer.Client`) is the leverage point —
    one shared LSP host + a pluggable per-dialect backend (reusing
    `SqlParser`'s existing dialect modes), rather than three independent
    heavyweight VSPackage/MEF extensions. Live schema introspection still
    needs each dialect's own ADO provider (Npgsql, MySqlConnector,
    Microsoft.Data.Sqlite) wired in separately — that part doesn't share.
  - *Effort*: one dialect to IntelliSense + inline-validation parity (not
    full Server Explorer/execution-plan parity) is roughly 2-4 months for one
    experienced developer. All three, built LSP-first with a shared host, is
    closer to 1.6-2x a single dialect's effort rather than 3x — still a
    multi-quarter effort, not a side project. Full parity with SQL Server's
    native depth (Server Explorer-grade connection browsing, execution plan
    visualization, project-wide rename refactoring) is a dedicated-team,
    multi-quarter undertaking on top of that.
  - *Hardest parity gaps*: (1) live graphical execution-plan visualization —
    no VS-native equivalent to render Postgres/MySQL `EXPLAIN` output against,
    though the existing `jauntyq explain` CLI's cached EXPLAIN output could
    seed this; (2) Server Explorer-grade live connection/object-tree browsing;
    (3) rename-across-references refactoring, which needs project-wide
    reference tracking beyond editor-level IntelliSense.
  - *Recommendation*: **not yet — needs more research.** The reusable parser
    lowers the floor but this still competes with existing commercial tooling
    and unvalidated demand (how many JauntyQ prospects use non-SQL-Server
    dialects in Visual Studio specifically, vs. VS Code or Rider). Before
    spec'ing: a live web search to confirm/refresh the landscape survey above,
    and a demand check with prospects/customers.

## Shipped
- **Migration Impact Analysis (003).** Reuses the migration simulator + query
  validator to report which queries a pending migration set affects, classified
  **SAFE / RISKY / BREAKING**, at build time (`JNT9004` RISKY warning) and via
  the offline `jauntyq migrate impact` CLI verb. The shared Roslyn-free engine now
  lives in `Extrode.JauntyQ.Analysis`..
- **Deeper query analysis (004).** Added `JNT8006` (join-not-a-foreign-key) and
  `JNT8007` (ORDER BY without a supporting index) to the offline JNT8xxx perf
  pass, plus ORDER BY capture in the SqlParser IR. The **N+1 heuristic** from the
  original scope was split out as 008, which has since shipped too (see below)..
- **Database contract testing (005).** The structural CI-gate core shipped: the
  `Extrode.JauntyQ.Schema.Contract` NuGet (test-embeddable assertion API + a fixed
  breaking/compatible severity model) and an upgraded `jauntyq schema verify`
  (`--format`, `--fail-on`); the extractors were relocated out of the CLI so the
  core stays zero-dependency. The **per-service contracts** and **central
  registry** parts of the original vision were split into Planned specs 010 + 009
  (and runtime startup verification + usage-aware severity into 011 + 012)..
- **N+1 query heuristic (008).** Build-time, offline `JNT8008` (Warning) over the
  query corpus + FK graph: pairs a child point-lookup by foreign key with a
  parent-collection query and suggests a join or batched `IN`, with guards
  (already-`IN`, joined child, single-row-PK parent, no FK, project-level
  suppression). The perf check split out of 004..
- **Opt-in live EXPLAIN plan analyzer (007).** `jauntyq explain` CLI verb only, so
  the Roslyn generator stays offline; lives in the **non-core** `Extrode.JauntyQ.Explain`
  library. Per-dialect estimate-only `EXPLAIN` (Postgres/SQLite/MySQL/SQL Server),
  capped, cached at `.jaunty/explain-cache.json`, report-only (exit 0; opt-in
  `--fail-on`), a missing/unreachable DB skips cleanly. Entitlement-gated..
- **Central schema-dependency registry (009).** `jaunty.registry.json` naming
  schema snapshots + the services that depend on them, with `registry
  resolve|dependents|validate|list`, in the **non-core** `Extrode.JauntyQ.Registry`
  library. Additive/opt-in; the substrate for per-service contracts..
- **Per-service contracts (010).** Registry-driven scope filter for `schema
  verify` (`--service`/`--registry`) that narrows a contract to the slice a
  service actually depends on (narrows, never widens), so another service's
  change does not fail this service's gate. Builds on 005 + 009..
- **Runtime startup verification (011).** Opt-in boot-time `StartupSchemaGuard`
  (fail-fast / warn / skip per policy) in the **non-core**
  `Extrode.JauntyQ.Schema.Contract` package, with optional `<JauntyQEmbedSnapshot>`
  build wiring; `Extrode.JauntyQ.Runtime` stays zero-dependency..
- **Usage-aware severity (012).** Opt-in, downgrade-only `UsageAwareReclassifier`
  + `jauntyq usage export` (usage manifest from `ReferencedObjects`); `schema
  verify --usage` downgrades breaking drift on objects no generated query
  references and never hides a real break. Composes with 010 scope..
- **DDL-as-schema-source (sqlc-style).** A project with no pulled JSON snapshot
  can define its base schema purely from `CREATE TABLE` / `ALTER TABLE` files
  under the `db/ddl/*.sql` convention, reusing the existing migration parser +
  schema simulator to build the schema from an empty database. Framed as an
  *alternative input*, not a replacement: when a `.schema.json` snapshot is
  present it stays authoritative and any `db/ddl/*.sql` files are silently
  ignored (no merge, no error). Because there is no snapshot to infer the
  dialect from, DDL mode requires the consumer to declare it in the `.csproj`
  via `<JauntyQDialect>sqlserver</JauntyQDialect>` (or `postgres`, `mysql`,
  `sqlite`); a missing/unrecognized value is a build error (JNT9003). Existing
  `db/migrations/*.sql` files still apply on top of the ddl-built base exactly
  as they apply on top of a snapshot. Known limitation: the migration parser
  does not model index DDL, so tables *defined in* DDL/migration files expose
  PK/identity/column-type info but no secondary unique indexes, the JNT8004
  composite-index check and Upsert alternate-key resolution only ever see the
  PK for such tables. (Indexes, procedures, and sequences captured in a pulled
  snapshot survive migration simulation unchanged.)
- **Provider-native bulk fast paths.** `BulkInsert(IEnumerable<Row>)` /
  `BulkInsertAsync` (and their static overloads) now specialize the emission per
  dialect: Npgsql binary `COPY` (Postgres), `SqlBulkCopy` (SQL Server), and
  `MySqlBulkCopy` (MySQL). SQL Server and MySQL feed their `WriteToServer` through
  a single reflection-free, AOT-safe `DbDataReader`-over-`IEnumerable<Row>`
  adapter emitted once per table (ordinal-based `switch`, no reflection). `sqlite`
  and any unrecognized dialect keep the portable single-transaction
  prepared-command loop unchanged. Note: the `MySqlBulkCopy` path requires the
  consumer's connection string to set `AllowLoadLocalInfile=true` (and the server
  to allow `LOCAL INFILE`); JauntyQ cannot set this and documents it on the
  generated method.
- **Sequences (SQL Server + PostgreSQL + MariaDB).** `jauntyq schema pull`
  captures sequence objects and the generator emits typed
  `db.Sequences.Next{Name}()` / `Next{Name}Async()` accessors. Real/Oracle
  MySQL and SQLite have no true sequence object, so this is dialect-specific;
  MariaDB (which shares the `mysql` dialect string with real MySQL) is
  detected separately and does get accessors, using `SELECT NEXTVAL(<seq>)`.
  (Broader schema-object generation, functions, enums, UDTs, remains
  deferred; see below.)
- **API reference / JNT diagnostics reference docs.**
  `docs/06-reference/diagnostics.md` (every JNTxxxx code, severity, meaning)
  and `docs/06-reference/api-overview.md` (shape of the generated surface,
  pointing to XML doc comments in `src/Extrode.JauntyQ.Generator/CodeEmitter*.cs` as the
  authoritative reference), alongside the CLI, configuration, snapshot-format,
  directives, and dialect references under `docs/06-reference/`. Full
  XML-doc-driven site generation (DocFX or similar) remains deferred; see below.
- **SBOM generation.** `.github/workflows/release.yml` generates a CycloneDX
  SBOM (`dotnet CycloneDX`) for every tagged release and attaches it alongside
  the packages, no signing cert needed for this part.
- **Parser hardening (report M3).** Unterminated-token diagnostics (JNT1002)
  and input-size caps (JNT1003, `SqlTokenizer.MaxInputLength`).

## Deferred (future, not now)
- **Broader schema-object generation.** Functions, enums, and UDTs (jOOQ-level
  completeness). Sequences already shipped (see above); this covers the
  remaining object types. Lower priority.
- **Package signing.** The core packages ship from NuGet.org over HTTPS with
  repository signing by the feed. Authenticode or NuGet author signing is a
  value-add warranted when an enterprise customer's policy demands it or when a
  native-AOT CLI binary ships; it would go through a cloud signer, never a stored
  `.pfx`.
- **XML-doc-driven API reference site (DocFX or similar).** A generated site
  built from the generator's XML doc comments; the hand-written
  `docs/06-reference/` pages cover the reference need for now.

  **Scoped (2026-09-19):** JauntyQ's docs already run on a shared engine — the
  sibling repo `../docs` (`github.com/extrode/docs`, née `Beparey.DocsGen`),
  which `scripts/build-docs.sh` invokes directly (`../docs/src/Docs`). That
  engine is a pure markdown-in/HTML-out static-site pipeline (Markdig +
  System.CommandLine only — no Roslyn, no reflection): it walks a numbered
  markdown tree and renders theming, sidebar nav, per-page TOC, search index,
  and Mermaid-to-SVG at build time. It has **no** XML-doc-comment or
  reflection-based API reference capability today — confirmed by grepping its
  entire source tree for `XmlDoc`/`DocFX`/`Reflection`/`Roslyn` with zero hits.
  So adopting a site generator is already done; generating reference pages
  from XML doc comments is not, and is a genuinely new component, not a config
  flag on the existing engine. Smallest-lift design: a converter that reads
  the generator's `.xml` doc-comment file + reflects over its public members
  (resolving `<see cref>`/`<paramref>` etc.) and emits pages into the same
  numbered-markdown-tree shape `../docs` already consumes — reuses theming,
  nav, search, and versioning for free, and confines the new work to member
  enumeration + doc-comment parsing + a markdown template per member kind.
  Roughly comparable in scope to a small DocFX subset. Still deferred; no
  timeline.

## Non-goals (owned elsewhere or out of scope)
- **Dynamic / runtime-composed SQL**, this is Jaunty's job, not JauntyQ's.
  JauntyQ keeps the "all SQL known and validated at build time" contract.
- **LINQ query composition, change tracking, lazy loading, entity graphs**, EF Core's domain; deliberately rejected ("SQL as source of truth").
