# Configuration reference (MSBuild & file conventions)

Everything the generator reads is either an MSBuild property or an
`AdditionalFiles` item classified by its path. There are exactly two properties
and one set of folder conventions.

## MSBuild properties

| Property | Values | Default | Effect |
|---|---|---|---|
| `JauntyQDialect` | `sqlserver`, `postgres`, `mysql`, `sqlite` (case-insensitive) | unset | Declares the target dialect **for DDL-as-schema-source mode** (no `.schema.json` present). Ignored when a snapshot exists, because the snapshot already carries its dialect. Missing or invalid while `db/ddl/*.sql` files are present raises `JNT9003`. |
| `JauntyQAutoCrud` | `true` / `false` | `true` | Whether the generator synthesises CRUD methods (`GetAll`/`GetById`/`Insert`/`Update`/`Delete`/`Upsert`/`BulkInsert`) for every table. Set `false` to emit nothing but your hand-written queries. |

```xml
<PropertyGroup>
  <JauntyQDialect>sqlite</JauntyQDialect>   <!-- only for DDL mode -->
  <JauntyQAutoCrud>false</JauntyQAutoCrud>   <!-- optional -->
</PropertyGroup>
```

Both are surfaced to the analyzer through `CompilerVisibleProperty`
registrations. When you consume JauntyQ as a package, the shipped MSBuild props
(`build/` and `buildTransitive/`) register them automatically, you do **not**
add `<CompilerVisibleProperty>` yourself. In-repo builds get the same
registration from `Directory.Build.props`.

No other analyzer-config keys (`build_metadata.*`, `.editorconfig` values) are
read by the generator, apart from the standard
`dotnet_diagnostic.<code>.severity` mechanism you can use to tune or suppress
individual `JNTxxxx` diagnostics.

## AdditionalFiles

The generator consumes these `AdditionalFiles` globs:

```xml
<ItemGroup>
  <AdditionalFiles Include="db\**\*.sql" />
  <AdditionalFiles Include="db\schema\*.schema.json" />
  <AdditionalFiles Include="db\schema\*.accept.json" />
  <AdditionalFiles Include="db\schema\*.scope.json" />
</ItemGroup>
```

The single `db\**\*.sql` glob covers queries, migrations, and DDL, they are
distinguished by folder, not by separate globs. The last two globs are optional,
needed only if you use [the acceptance
sidecar](#the-acceptance-sidecar-acceptjson) or [the scope
sidecar](#the-scope-sidecar-scopejson).

## How files are classified

Classification is by **path segment** (case-insensitive, `/` and `\` both
match):

| Kind | Recognized by | Role |
|---|---|---|
| **Schema snapshot** | file ends in `.schema.json` (convention `db/schema/*.schema.json`) | Authoritative schema when present. |
| **Acceptance sidecar** | file ends in `.accept.json` (convention `db/schema/jaunty.accept.json`) | Accepts unindexed scans in **generated** queries. Optional. |
| **Scope sidecar** | file ends in `.scope.json` (convention `db/schema/jaunty.scope.json`) | Declares tables scoped by a column (tenant scoping). Optional. |
| **DDL** | a `ddl` path segment (convention `db/ddl/*.sql`) | Builds the base schema **only when no snapshot exists**. Requires `JauntyQDialect`. |
| **Migration** | a `migrations` path segment (convention `db/migrations/*.sql`) | Applied on top of the base schema, in filename order. |
| **Query** | any other `.sql` under `db/` (convention `db/tables/<Entity>/<Method>.sql`) | Parsed, validated, and emitted as a typed method. |

Any path component literally named `ddl` or `migrations` triggers that
classification, regardless of depth.

JauntyQ never applies migrations to a database. The
[migration tracking contract](migration-tracking-contract.md) says what your
own runner needs to do to agree with it.

## Schema-source precedence

1. If a `.schema.json` snapshot is present, it is authoritative and any
   `db/ddl/*.sql` files are **ignored** (no merge, no error).
2. Otherwise, if `db/ddl/*.sql` files are present, they build the base schema
   (requires `JauntyQDialect`).
3. Migrations, if any, are always simulated on top of the result of step 1 or
   2, in filename order.

See [DDL as schema source](../03-guides/ddl-as-schema-source.md) for the DDL
path and [dialect differences](dialects.md) for what each dialect emits.

## The acceptance sidecar (`*.accept.json`)

Auto-CRUD synthesizes a `GetBy<Fk>` loader for every foreign-key column, and on
engines that do not index foreign keys automatically (SQLite, unlike InnoDB)
those loaders scan, so they raise `JNT8004`. A synthesized query has no file,
so it cannot carry [`-- @allow-unindexed`](directives.md#-allow-unindexed). The
sidecar is where a generated scan is accepted instead:

```json
{
  "allowUnindexed": [
    { "table": "address", "column": "city_id", "reason": "upstream schema, the index is not ours to add" }
  ]
}
```

| Rule | Detail |
|---|---|
| **Per column** | `table` and `column` name one column. There is no wildcard and no table-level form; each accepted scan is a decision someone made. |
| **Reason mandatory** | Same contract as the directive. A blank reason is `JNT6003` and the entry accepts nothing. |
| **Generated queries only** | A hand-written query filtering an accepted column still raises `JNT8004`. It has a file; the file carries the directive. |
| **One file** | More than one `*.accept.json` is `JNT6003`; the ordinal-lowest path wins and the others are ignored entirely, they are not merged. |
| **Structural problems are `JNT6003`** | Unparseable JSON, a missing `table`/`column`/`reason`, a table or column the snapshot does not have, or the same column accepted twice (the first entry stays in force). Every one is a warning, and the entry is dropped. |
| **Dead entries are `JNT8012`** | An entry that suppressed nothing on a build that ran synthesis. Either an index now covers the column, delete the entry, or a hand-written `.sql` claimed that loader's slot, in which case the acceptance belongs in that file as `-- @allow-unindexed <reason>`. |
| **Off with auto-CRUD** | Under `<JauntyQAutoCrud>false</JauntyQAutoCrud>` no entry can be used, so the dead-entry sweep does not run. Structural problems are still reported. |

It suppresses `JNT8004` and nothing else. Table and column are matched
case-insensitively, as everywhere else an identifier is resolved against the
snapshot.

**Why a separate file rather than a field on the snapshot.** `jauntyq schema
pull` rewrites the snapshot wholesale, so an acceptance recorded there would be
erased by the next pull. The sidecar is hand-maintained and nothing generates
over it.

`samples/Extrode.JauntyQ.Sakila.Sqlite.Tests/db/schema/jaunty.accept.json` is a worked
example: the SQLite port of Pagila declares its foreign keys without secondary
indexes, and its 17 entries take the resulting `JNT8004` count to zero.

## The scope sidecar (`*.scope.json`)

Declares the tables that belong to a tenant (or any other owner) and the
column that says which. With it, a hand-written query that reaches one of
those tables without filtering on that column is refused, and auto-CRUD for
those tables takes the value first. The [tenant scoping
guide](../03-guides/tenant-scoping.md) covers what is checked and what is not.

```json
{
  "scopes": [
    { "table": "orders", "column": "tenant_id" },
    { "table": "order_lines", "column": "tenant_id" }
  ]
}
```

| Rule | Detail |
|---|---|
| **Explicit entries only** | Each entry names one table and one column. There is no wildcard and no "every table with a `tenant_id`" form. |
| **Several entries per table** | A table may be scoped by more than one column; each must be proven. |
| **Bindable columns only** | A column that is part of the primary key, an identity, computed, or a rowversion cannot be bound from a parameter. The entry is `JNT6004` and dropped. |
| **One file** | More than one `*.scope.json` is `JNT6004`; the ordinal-lowest path wins and the others are ignored entirely, they are not merged. |
| **Structural problems are `JNT6004`** | Unparseable JSON, the literal `null`, a top-level key other than `scopes` (a misspelt `"scope"` would otherwise scope nothing), `"scopes": null`, a `null` entry, a missing `table` or `column`, a table or column the snapshot does not have, or a repeated entry (the first stays in force). Each is a warning, the entry is dropped, and the message says the table is now unscoped by it. |
| **Independent of auto-CRUD** | Hand-written queries are checked whether or not `<JauntyQAutoCrud>` is on. |
| **Absent or empty** | Nothing is checked, and generated code is byte-identical to a project without the sidecar. A file with no entries (`{}` or `"scopes": []`) also raises `JNT6004`, since it is more likely a mistake than intended. |

Tables and columns are matched case-insensitively. Like the acceptance
sidecar, it is a separate hand-maintained file because `jauntyq schema pull`
rewrites the snapshot wholesale.

Diagnostics: `JNT4005` (a reach without its scope parameter, Error),
`JNT4006` (an `-- @unscoped` that accepts nothing), `JNT4007` (no scoped
Upsert on MySQL), `JNT6004` (problems in the file). See
[diagnostics](diagnostics.md).

## Entity and method naming

This mapping is a **stable contract**. Other tools (a scaffolder, a code
reviewer, a doc generator) may depend on it. Changing it would be a breaking
change and would be called out as one in the changelog. `NamingContractTests`
pins every rule below.

**The rule.** A query file's containing folder names the entity, and its file
name names the method:

| File | Generated call |
|---|---|
| `db/Widgets/GetAll.sql` | `db.Widgets.GetAll()` |
| `db/tables/Widgets/GetAll.sql` | `db.Widgets.GetAll()` |
| `db/Widgets/Admin/ListAll.sql` | `db.Widgets.ListAll()` |
| `db/Ping.sql` | `db.Queries.Ping()` |

- **The root.** JauntyQ finds the common folder above all query files (the
  folder two levels up from each file, shared by all of them). Paths are read
  relative to that root. Migration and DDL files are not query files and do
  not count.
- **Entity name** = the first folder below the root. A leading `tables` or
  `views` folder is skipped, so `db/tables/Widgets/` and `db/Widgets/` are the
  same entity. Folders deeper than the entity folder are ignored: they group
  files, they do not rename anything.
- **Catch-all.** A file directly in the root, with no entity folder, belongs to
  the entity `Queries`.
- **Method name** = the file name without `.sql`.
- **No case or plural changes.** Folder and file names are used exactly as
  written. `db/widgets/getAll.sql` gives `db.widgets.getAll()`.
- **Overrides.** A hand-written file whose entity and method match an auto-CRUD
  method replaces the generated one. Hand-written always wins.

**Legal names (`JNT2004`).** The entity and method names become a C# class and
method, so each must be a legal C# identifier: ASCII letters, digits and
underscores, not starting with a digit, and not a C# reserved keyword such as
`class` or `int`. `db/Widgets/2Fast.sql`, `db/Wid-gets/GetAll.sql` and
`db/class/GetAll.sql` are all `JNT2004`, and the file generates nothing until it
is renamed.

**Row types.** What a query returns is named from the same pair, in one of two
ways:

- **The table's own type.** A query that selects every column of one table, in
  the table's column order, with no result-shaping directives, returns the
  shared per-table type. Its name comes from the **table** name, not the folder:
  the table name in PascalCase, made singular by these rules, applied in order:
  - ends in `ies`: replace with `y` (`Categories` → `Category`)
  - ends in `xes`, `zes`, `ches`, `shes` or `sses`: drop `es` (`Boxes` → `Box`)
  - ends in `s` but not `ss`, `us` or `is`: drop `s` (`Widgets` → `Widget`)
  - otherwise unchanged.

  When the rules leave the name unchanged (`Status`, `Region`), the type is the
  name plus `Row` (`StatusRow`), so it never collides with the entity class.
- **A query-specific type.** Any other projection (a join, a subset of columns)
  returns a type nested in the entity and named for the method:
  `db/Widgets/GetNames.sql` returns `Widgets.Result.GetNames`.

## Consumer project template

Package consumers:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Extrode.JauntyQ.Generator" Version="0.1.0" PrivateAssets="all" />
    <PackageReference Include="Extrode.JauntyQ.Runtime" Version="0.1.0" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="db\**\*.sql" />
    <AdditionalFiles Include="db\schema\*.schema.json" />
  </ItemGroup>
</Project>
```

In-repo (project reference) consumers use the analyzer wiring instead:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Extrode.JauntyQ.Generator\Extrode.JauntyQ.Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\src\Extrode.JauntyQ.Runtime\Extrode.JauntyQ.Runtime.csproj" />
  </ItemGroup>
```

For DDL-as-schema-source projects, add `<JauntyQDialect>` and omit the
`.schema.json` glob.
