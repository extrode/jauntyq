# API reference overview

JauntyQ generates code at compile time; there is no runtime API surface to
browse beyond what your own schema produces. This page orients you to the
*shape* of that generated surface. Every generated member carries an XML doc
comment (see `CodeEmitter.cs` and its `Part*.cs` siblings in
`src/Extrode.JauntyQ.Generator/` for the exact templates), those doc comments are
the authoritative, always-in-sync reference; this page is a map, not a
substitute.

Full XML-doc-driven API site generation (e.g. DocFX) is not set up, that is
a separate, larger infrastructure task (a docs pipeline, hosting, CI wiring)
outside the scope of this reference page. See `docs/07-roadmap/roadmap.md`.

## Shape of the generated surface

- **`JauntyDb`**, the root object, constructed from a `DbConnection`. Exposes
  one lazily-constructed property per table/view entity (`db.Products`,
  `db.Orders`, ...) and, when the schema has sequences, `db.Sequences`.
  `BeginTransaction()`/`BeginTransactionAsync()` return a `Transaction` that
  every subsequent call on `db` automatically enlists in until
  commit/rollback/dispose.
- **Per-entity accessor** (`db.Products`), one method per query: hand-written
  `.sql` files under that entity's folder, plus auto-CRUD synthetics
  (`GetAll`, `GetById`, `GetBy<ForeignKey>`, `Insert`, `Update`, `Delete`,
  `Upsert`, `BulkInsert`) unless a hand-written file of the same name already
  exists (hand-written always wins) or `<JauntyQAutoCrud>false</JauntyQAutoCrud>`
  disables synthesis project-wide. Every method has a sync and an async twin,
  and both an instance form (`db.Products.GetById(1)`) and a `static` form
  taking an explicit `DbConnection` for callers that don't use `JauntyDb`.
  Static forms also take an optional trailing `DbTransaction? transaction`
  so they can enlist in a caller-managed transaction (instance forms flow
  the ambient `JauntyDb.CurrentTransaction` automatically); on `BulkInsert`
  a caller-supplied transaction suppresses the method's own commit, the
  caller owns the unit of work.
- **Row POCOs** (`Product`, or `<Entity>Row` when singularizing the entity
  name collides with the accessor class name, e.g. `MailQueueRow` next to
  `JauntyDb.MailQueue`), plain data-carrying classes, one property per
  column, a `required` modifier on non-nullable reference-typed properties,
  and a static `Read(DbDataReader)` used by every full-row query. `Insert`/
  `Update`/`Delete`/`Upsert` also get POCO-taking overloads
  (`db.Products.Insert(productRow)`) that forward to the scalar form;
  `BulkInsert` is inherently row-native (`IEnumerable<Row>`) and has no
  separate scalar form to forward to.
- **Directives** (SQL-file comment pragmas: `-- @first`, `-- @stream`,
  `-- @identity`, `-- @result`, `-- @each`, `-- @params`, `-- @type`,
  `-- @proc`, `-- @call`), control codegen shape per query file. See
  `src/Extrode.JauntyQ.Generator/Directives/` for the full parser.
- **Diagnostics**, every `JNTxxxx` code the generator can report, with
  meaning and fix guidance: see `docs/06-reference/diagnostics.md`.

## Where to look for exact signatures

Generated code is always visible: build once and inspect the emitted
`*.g.cs` files (in `obj/Debug/<tfm>/generated/Extrode.JauntyQ.Generator/...` under
your project), or read the emission logic directly in
`src/Extrode.JauntyQ.Generator/CodeEmitter*.cs` (each `Emit*` method's XML doc
describes exactly what it produces and why).
