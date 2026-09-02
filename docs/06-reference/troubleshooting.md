# Troubleshooting & FAQ

Common problems, why they happen, and the fix. For the full list of build
diagnostics see [diagnostics](diagnostics.md); this page maps symptoms to
causes.

## Nothing is generated

**No `db.*` accessors exist / `JauntyDb` is missing.** The generator only runs
when it can see your files and schema through MSBuild.

- Confirm the `AdditionalFiles` globs are present and match your layout:
  `db\**\*.sql` and `db\schema\*.schema.json`. See
  [configuration](configuration.md).
- Confirm the generator is referenced as an **analyzer**
  (`OutputItemType="Analyzer"` for a project reference; the package wires this
  automatically).
- Confirm a schema source exists, a committed `.schema.json`, or `db/ddl/*.sql`
  plus `<JauntyQDialect>`. Without either you get `JNT6001`.
- Do a clean rebuild. Roslyn caches generator output; an incremental build can
  hide a fixed configuration issue.

**Inspect what was emitted.** Set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>`
and look under `obj/<config>/<tfm>/generated/JauntyQ.Generator/` to see the
actual `.g.cs` files.

### First, find out whether the generator ran at all

This is worth doing before anything above, because the two failures behind
"nothing is generated" have opposite fixes and a screen of `CS0246`s looks
identical either way. One file tells them apart.

`JauntyQShapeGuard.g.cs` is a **post-initialization output**: Roslyn emits it
before any of JauntyQ's pipeline runs, so it exists if and only if the generator
loaded and started. It is also the only generated file that does not depend on
your SQL or schema.

| What you see | What it means | Where to look next |
|---|---|---|
| No `JauntyQShapeGuard.g.cs` at all | The generator never loaded or never started. Not a schema, SQL or configuration problem, nothing of yours has been read yet. | The analyzer reference itself. Roslyn reports a generator it cannot load as `CS8784`/`CS8034`, both **warnings**, so check the build log for those; the usual cause is a version or dependency mismatch between the generator and the compiler. |
| `JauntyQShapeGuard.g.cs` present, nothing else | The generator ran and emitted nothing. Your files or schema did not reach it, or every file was refused. | The `AdditionalFiles` globs and the `JNT` diagnostics in the build log, per the list above. |
| `JNT0001` in the build output | The generator threw. This is a bug in JauntyQ, not in your project. | Nothing on your side. The message names the stage and the exception, please report it. Generated output may be partly or entirely missing, and the `CS0246`s are consequences of it. |

Without `EmitCompilerGeneratedFiles`, the same question is answerable from code:
`typeof(JauntyQ.Generated.JauntyQShapeGuard)` compiles if and only if the
generator ran.

Note that a generator which **throws** is also reported by Roslyn as a warning
(`CS8785`) rather than an error, which is why the build can go on and leave you
with nothing but `CS0246`s. `JNT0001` exists to end that silence, but it can only
fire for a throw JauntyQ catches, if the generator failed to load, it never got
far enough to report anything, and the shape guard's absence is the tell.

## `<JauntyQAutoCrud>` or `<JauntyQDialect>` seems ignored

The property must reach the analyzer as a `CompilerVisibleProperty`. The
package ships this registration; if you consume via a project reference in a
repository without `Directory.Build.props`, add:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="JauntyQAutoCrud" />
  <CompilerVisibleProperty Include="JauntyQDialect" />
</ItemGroup>
```

## Build fails after a schema or migration change

The build validates every query against the effective (post-migration) schema, this is intended. A `JNT2001`/`JNT2002` after a migration means a query still
references a table/column the migration removed or renamed; fix the query or the
migration together. See [diagnostics](diagnostics.md) for each code.

## `JNT9002: table already exists` on a migration

`db/migrations/` is for **pending** migrations only. Once a migration is
deployed and you re-pull the snapshot, the snapshot already embodies it, so
re-applying its `CREATE` fails. Archive the migration file out of
`db/migrations/`.

## `JNT9003` in a DDL project

`db/ddl/*.sql` files are present with no snapshot, but `JauntyQDialect` is unset
or not one of `sqlserver`/`postgres`/`mysql`/`sqlite`. Add it:

```xml
<PropertyGroup><JauntyQDialect>sqlite</JauntyQDialect></PropertyGroup>
```

## `JNT4003: parameter type could not be inferred`

The parser could not derive a parameter's type from its use (common with
subqueries and expressions). Declare it with
[`-- @params`](directives.md#-params); the error message includes the exact
line to add.

## `JNT5001` / `JNT5002` on a literal

A string or numeric literal in your SQL cannot fit the target column
(length/precision/range). This is a compile-time value-safety check, the value
would truncate or overflow at runtime. Shorten the literal, or widen the column
(and re-pull/re-migrate the schema).

## A query runs but reads the wrong column, or the shape guard throws

The database has drifted from the snapshot (a column renamed or reordered in
production without a re-pull). Run `jauntyq schema verify` to see the drift, then
`jauntyq schema pull` to refresh the snapshot and rebuild. See the
[CLI reference](cli.md).

## `jauntyq schema pull` fails or leaks nothing useful

Errors are anonymized by default so connection-string secrets never reach logs.
Set `JAUNTYQ_VERBOSE=1` to see the full exception while debugging locally.

## MySQL `BulkInsert` throws at runtime

`MySqlBulkCopy` needs `LOCAL INFILE` on both sides: `AllowLoadLocalInfile=true`
in the connection string, and `local_infile` enabled on the server. See the
[bulk insert guide](../03-guides/bulk-insert.md).

## Native AOT publish warnings

JauntyQ's generated code and `JauntyQ.Runtime` are trim/AOT-safe. Remaining
warnings almost always come from the **database provider**; use an AOT-friendly
provider stack (e.g. `Microsoft.Data.Sqlite.Core` +
`SQLitePCLRaw.bundle_e_sqlite3`).

## Suppressing a performance warning (`JNT8xxx`)

The `JNT8xxx` codes are advisory (the query still compiles). If a scan is
intentional, suppress per-project in `.editorconfig`:

```ini
dotnet_diagnostic.JNT8004.severity = none
```

## FAQ

**Does JauntyQ connect to my database at build time?** No. Only the `jauntyq`
CLI connects. The generator reads the committed snapshot.

**Is it a DbContext / ORM with change tracking?** No. `JauntyDb` is a stateless
router: no tracking, no caching, no unit of work. `new` one per request.

**Can I still write raw SQL?** That is the whole model, every query is a `.sql`
file. Auto-CRUD is a convenience layered on top and any synthetic is overridden
by a same-named file.

**Does it support MariaDB?** Yes, via the `mysql` dialect. See
[dialect differences](dialects.md).

**Where do I see exact generated signatures?** Build and inspect the emitted
`.g.cs` files, or read the [API overview](api-overview.md).
