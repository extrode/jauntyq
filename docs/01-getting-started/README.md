# Getting started

This section covers installing JauntyQ, the requirements it has on your
project, and the minimum wiring to get generated code out of a build. Once you
are set up, work through the [first-hour tutorial](../02-learn/README.md) and
the [feature guides](../03-guides/README.md).

## Requirements

- **.NET SDK 8.0 or later.** The generator is a Roslyn incremental source
  generator and runs inside the compiler; your project targets any framework
  Roslyn supports (net8.0+ recommended).
- **A database provider package** appropriate to your dialect, referenced by
  the consuming project, JauntyQ emits ADO.NET calls but does not bundle a
  driver:
  - SQL Server / Azure SQL: `Microsoft.Data.SqlClient`
  - PostgreSQL: `Npgsql`
  - MySQL / MariaDB: `MySqlConnector`
  - SQLite: `Microsoft.Data.Sqlite`
- **A schema source**, either a committed `.schema.json` snapshot pulled with
  the `jauntyq` CLI, or checked-in DDL (see
  [DDL as schema source](../03-guides/ddl-as-schema-source.md)).

The `Extrode.JauntyQ.Runtime` package is `IsAotCompatible`; the generated code uses no
reflection and no runtime SQL parsing, so it is Native AOT and trim compatible
provided your database provider is too.

## Install the packages

The core packages are on NuGet.org. Reference both:

```xml
<ItemGroup>
  <PackageReference Include="Extrode.JauntyQ.Generator" Version="0.5.1" PrivateAssets="all" />
  <PackageReference Include="Extrode.JauntyQ.Runtime" Version="0.5.1" />
</ItemGroup>
```

`Extrode.JauntyQ.Generator` installs as a Roslyn analyzer automatically; the package
ships the MSBuild props that register `JauntyQDialect` and `JauntyQAutoCrud`,
so no `OutputItemType="Analyzer"` or `<CompilerVisibleProperty>` entries are
needed when consuming the package. `Extrode.JauntyQ.Runtime` carries the handful of
types the generated code depends on and must be a normal (non-private)
reference.

### Consuming from source (in-repo)

If you build against the repository directly rather than the package, reference
the projects instead, the generator as an analyzer, the runtime as a normal
reference:

```xml
<ItemGroup>
  <ProjectReference Include="..\Extrode.JauntyQ.Generator\Extrode.JauntyQ.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\Extrode.JauntyQ.Runtime\Extrode.JauntyQ.Runtime.csproj" />
</ItemGroup>
```

## Point the generator at your files

The generator reads your SQL and schema through MSBuild `AdditionalFiles`. The
standard glob picks up every `.sql` file under `db/` (queries, migrations, and
DDL alike, they are classified by folder) plus the snapshot:

```xml
<ItemGroup>
  <AdditionalFiles Include="db\**\*.sql" />
  <AdditionalFiles Include="db\schema\*.schema.json" />
</ItemGroup>
```

The full set of properties, globs, and folder conventions is in the
[configuration reference](../06-reference/configuration.md).

## The `db/` folder layout

```
db/
  schema/
    jaunty.schema.json      committed snapshot (authoritative schema source)
  tables/
    Products/
      GetById.sql           one file per query; file name = method name
      GetByCategory.sql
  migrations/
    0001_add_column.sql     pending DDL, applied on top of the snapshot
```

Folder name under `tables/` = entity name = generated accessor class
(`db.Products`). File name (without `.sql`) = method name. Auto-CRUD methods
(`GetAll`, `GetById`, `Insert`, `Update`, `Delete`, `Upsert`, `BulkInsert`) are
synthesised for every table unless a same-named `.sql` file overrides them.

## First build

1. Pull a snapshot (see the [CLI reference](../06-reference/cli.md)). The CLI ships as a
   .NET global tool, `dotnet tool install --global Extrode.JauntyQ.Cli` installs it as `jauntyq` (the premium verbs come with `Extrode.JauntyQ.Cli.Premium`, same command).
   Working inside a clone of this repository, the equivalent without installing is:

   ```bash
   dotnet run --project src/Extrode.JauntyQ.Cli -f net8.0 -- schema pull --provider sqlserver \
     --connection-env MYDB_CONN \
     --output db/schema/jaunty.schema.json
   ```

2. Add a query file, e.g. `db/tables/Products/GetById.sql`:

   ```sql
   -- @first
   SELECT ProductId, ProductName, UnitPrice
   FROM Products
   WHERE ProductId = @ProductId
   ```

3. Build. Then use the generated surface:

   ```csharp
   using var conn = new SqlConnection(connectionString);
   var db = new JauntyDb(conn);
   Product? p = db.Products.GetById(42);
   ```

If a query does not match the schema, the build fails with a `JNTxxxx`
diagnostic rather than compiling. If something does not generate as expected,
see [troubleshooting](../06-reference/troubleshooting.md).
