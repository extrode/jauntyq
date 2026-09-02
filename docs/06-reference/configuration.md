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
</ItemGroup>
```

The single `db\**\*.sql` glob covers queries, migrations, and DDL, they are
distinguished by folder, not by separate globs. The third glob is optional and
only needed if you use [the acceptance
sidecar](#the-acceptance-sidecar-acceptjson).

## How files are classified

Classification is by **path segment** (case-insensitive, `/` and `\` both
match):

| Kind | Recognised by | Role |
|---|---|---|
| **Schema snapshot** | file ends in `.schema.json` (convention `db/schema/*.schema.json`) | Authoritative schema when present. |
| **Acceptance sidecar** | file ends in `.accept.json` (convention `db/schema/jaunty.accept.json`) | Accepts unindexed scans in **generated** queries. Optional. |
| **DDL** | a `ddl` path segment (convention `db/ddl/*.sql`) | Builds the base schema **only when no snapshot exists**. Requires `JauntyQDialect`. |
| **Migration** | a `migrations` path segment (convention `db/migrations/*.sql`) | Applied on top of the base schema, in filename order. |
| **Query** | any other `.sql` under `db/` (convention `db/tables/<Entity>/<Method>.sql`) | Parsed, validated, and emitted as a typed method. |

Any path component literally named `ddl` or `migrations` triggers that
classification, regardless of depth.

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

`samples/JauntyQ.Sakila.Sqlite.Tests/db/schema/jaunty.accept.json` is a worked
example: the SQLite port of Pagila declares its foreign keys without secondary
indexes, and its 17 entries take the resulting `JNT8004` count to zero.

## Entity and method naming

- **Entity name** = the folder under `db/tables/` (e.g. `Products` →
  `db.Products`, generated class `Products`).
- **Method name** = the `.sql` file name without extension (e.g.
  `GetByCategory.sql` → `GetByCategory`).
- A hand-written file whose name matches an auto-CRUD method **overrides** the
  synthetic one; hand-written always wins.

## Consumer project template

Package consumers:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="JauntyQ.Generator" Version="0.1.0" PrivateAssets="all" />
    <PackageReference Include="JauntyQ.Runtime" Version="0.1.0" />
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
    <ProjectReference Include="..\..\src\JauntyQ.Generator\JauntyQ.Generator.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="..\..\src\JauntyQ.Runtime\JauntyQ.Runtime.csproj" />
  </ItemGroup>
```

For DDL-as-schema-source projects, add `<JauntyQDialect>` and omit the
`.schema.json` glob.
