# Build artifacts cleanup

Written 2026-09-16 during a machine-wide disk-space audit. This project was carrying
**3.87 GB** across 86 build/tool-output directories (bin, obj, node_modules, dist,
and similar -- see `build-artifacts-cleanup-2026-09-16.ps1` in this same folder for the exact
list and sizes at the time this was written).

## What this is

`build-artifacts-cleanup-2026-09-16.ps1` deletes exactly the directories listed inside it. It:

- **Does nothing by default.** Bare invocation is a dry run.
- Deletes only with `-e` / `--execute`.
- Re-checks every path against `git ls-files` at run time before deleting it, and refuses
  (leaves alone) anything git tracks -- even if it's on the list below, even if this note is
  stale. A stale list makes the script a no-op on those entries, not a data-loss risk.
- Reports "already gone" instead of erroring on anything already cleaned up by hand.

Run it from PowerShell, from anywhere (it resolves its own repo root):

```
pwsh -NoProfile scripts/cleanup/build-artifacts-cleanup-2026-09-16.ps1        # dry run
pwsh -NoProfile scripts/cleanup/build-artifacts-cleanup-2026-09-16.ps1 -e     # delete
```

## Keep this updated

**If the build/artifact layout changes -- new output directories, moved paths, renamed or removed
projects inside this repo -- update the list inside the `.ps1`, not just this note.** The script
is the source of truth for what gets deleted; this file just explains why it exists. A stale
script isn't dangerous (the git-tracked check protects committed content either way), but it will
silently stop reclaiming space as new artifact directories appear that aren't on its list.

## What was on the list as of 2026-09-16

- `src/Extrode.JauntyQ.Cli/bin` (337 MB)
- `benchmarks/Extrode.JauntyQ.Benchmarks/bin` (288 MB)
- `tests/Extrode.JauntyQ.Generator.Tests/bin` (237 MB)
- `samples/Extrode.JauntyQ.Conduit.Differential.Tests/bin` (236 MB)
- `samples/Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests/bin` (200 MB)
- `tests/Extrode.JauntyQ.Schema.Extraction.Tests/bin` (199 MB)
- `tests/Extrode.JauntyQ.Cli.Tests/bin` (181 MB)
- `samples/Extrode.JauntyQ.Conduit.Sqlite.Tests/bin` (172 MB)
- `samples/Extrode.JauntyQ.Sakila.Sqlite.Tests/bin` (150 MB)
- `samples/Extrode.JauntyQ.Chinook.Sqlite.Tests/bin` (148 MB)
- `samples/Extrode.JauntyQ.Sqlite.Tests/bin` (145 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.Sqlite.Tests/bin` (145 MB)
- `samples/Extrode.JauntyQ.DdlSchema.Sqlite.Tests/bin` (144 MB)
- `samples/Extrode.JauntyQ.Sample/bin` (135 MB)
- `samples/Extrode.JauntyQ.Aot.Smoke/bin` (135 MB)
- `dist` (94 MB)
- `samples/Extrode.JauntyQ.Conduit.SqlServer.Tests/bin` (81 MB)
- `samples/Extrode.JauntyQ.Pagila.Postgres.Tests/bin` (75 MB)
- `samples/Extrode.JauntyQ.Sakila.SqlServer.Tests/bin` (63 MB)
- `samples/Extrode.JauntyQ.Northwind.Tests/bin` (63 MB)
- `samples/Extrode.JauntyQ.Conduit.Postgres.Tests/bin` (60 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.SqlServer.Tests/bin` (59 MB)
- `samples/Extrode.JauntyQ.Conduit.MySql.Tests/bin` (59 MB)
- `samples/Extrode.JauntyQ.Conduit.MariaDb.Tests/bin` (59 MB)
- `samples/Extrode.JauntyQ.Sakila.Postgres.Tests/bin` (39 MB)
- `samples/Extrode.JauntyQ.Sakila.MySql.Tests/bin` (38 MB)
- `samples/Extrode.JauntyQ.Sakila.MariaDb.Tests/bin` (38 MB)
- `samples/Extrode.JauntyQ.Postgres.Tests/bin` (33 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.Postgres.Tests/bin` (33 MB)
- `samples/Extrode.JauntyQ.MySql.Tests/bin` (32 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.MySql.Tests/bin` (32 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.MariaDb.Tests/bin` (32 MB)
- `samples/Extrode.JauntyQ.Packaged.Smoke/bin` (25 MB)
- `samples/Extrode.JauntyQ.Pagila.Postgres.Tests/obj` (24 MB)
- `tests/Extrode.JauntyQ.Analysis.Tests/bin` (13 MB)
- `tests/Extrode.JauntyQ.SqlParser.Tests/bin` (12 MB)
- `tests/Extrode.JauntyQ.Generator.Tests/obj` (7 MB)
- `samples/Extrode.JauntyQ.Sakila.SqlServer.Tests/obj` (7 MB)
- `samples/Extrode.JauntyQ.Sakila.MySql.Tests/obj` (7 MB)
- `samples/Extrode.JauntyQ.Sakila.MariaDb.Tests/obj` (7 MB)
- `samples/Extrode.JauntyQ.Sakila.Postgres.Tests/obj` (7 MB)
- `samples/Extrode.JauntyQ.Northwind.Tests/obj` (6 MB)
- `samples/Extrode.JauntyQ.Sakila.Sqlite.Tests/obj` (6 MB)
- `samples/Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Conduit.SqlServer.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Conduit.MySql.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Conduit.MariaDb.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Conduit.Postgres.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Chinook.Sqlite.Tests/obj` (5 MB)
- `samples/Extrode.JauntyQ.Conduit.Sqlite.Tests/obj` (5 MB)
- `tools/Extrode.JauntyQ.Fuzz/bin` (3 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.SqlServer.Tests/obj` (3 MB)
- `tests/Extrode.JauntyQ.Schema.Extraction.Tests/obj` (3 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.MySql.Tests/obj` (3 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.MariaDb.Tests/obj` (3 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.Sqlite.Tests/obj` (3 MB)
- `samples/Extrode.JauntyQ.EShopOnWeb.Postgres.Tests/obj` (3 MB)
- `samples/Extrode.JauntyQ.Sqlite.Tests/obj` (2 MB)
- `samples/Extrode.JauntyQ.Conduit.Differential.Tests/obj` (2 MB)
- `src/Extrode.JauntyQ.Generator/obj` (2 MB)
- `src/Extrode.JauntyQ.Analysis/bin` (2 MB)
- `samples/Extrode.JauntyQ.MySql.Tests/obj` (2 MB)
- `samples/Extrode.JauntyQ.Postgres.Tests/obj` (2 MB)
- `src/Extrode.JauntyQ.Generator/bin` (2 MB)
- `src/Extrode.JauntyQ.Cli.Core/bin` (2 MB)
- `src/Extrode.JauntyQ.Schema/obj` (1 MB)
- `tests/Extrode.JauntyQ.SqlParser.Tests/obj` (1 MB)
- `tests/Extrode.JauntyQ.Cli.Tests/obj` (1 MB)
- `samples/Extrode.JauntyQ.Sample/obj` (1 MB)
- `src/Extrode.JauntyQ.Analysis/obj` (1 MB)
- `benchmarks/Extrode.JauntyQ.Benchmarks/obj` (1 MB)
- `tests/Extrode.JauntyQ.Analysis.Tests/obj` (1 MB)
- `samples/Extrode.JauntyQ.DdlSchema.Sqlite.Tests/obj` (1 MB)
- `src/Extrode.JauntyQ.SqlParser/obj` (1 MB)
- `src/Extrode.JauntyQ.Schema/bin` (1 MB)
- `src/Extrode.JauntyQ.Cli/obj` (1 MB)
- `samples/Extrode.JauntyQ.Aot.Smoke/obj` (1 MB)
- `src/Extrode.JauntyQ.Schema.Extraction/bin` (1 MB)
- `src/Extrode.JauntyQ.Schema.Extraction/obj` (1 MB)
- `src/Extrode.JauntyQ.SqlParser/bin` (1 MB)
- `tools/Extrode.JauntyQ.Fuzz/obj` (1 MB)
- `src/Extrode.JauntyQ.Cli.Core/obj` (1 MB)
- `samples/Extrode.JauntyQ.Packaged.Smoke/obj` (0 MB)
- `src/Extrode.JauntyQ.Runtime/obj` (0 MB)
- `src/Extrode.JauntyQ.Runtime/bin` (0 MB)
- `src/Extrode.JauntyQ.Generator/build` (0 MB)
