#!/usr/bin/env pwsh
# build-artifacts-cleanup-2026-09-16.ps1
#
# Written 2026-09-16 during a machine-wide disk-space audit that found this
# project carrying 3.87 GB of build/tool artifacts (bin, obj, node_modules,
# dist, and similar directories -- see the list below). All of these are
# produced by the project's own build/install step and are safe to delete;
# each one is still re-checked against git at run time before being touched,
# so nothing tracked is ever removed even if this list goes stale.
#
# If your build layout changes (new artifact dirs, moved output paths,
# renamed projects), UPDATE THE LIST BELOW rather than leaving this stale --
# that is the whole point of keeping this script instead of doing this by
# hand once.
#
# Dry run is the default. --execute/-e authorises deletion.

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Execute = $false
foreach ($a in $args) {
  switch ($a) {
    '--execute' { $Execute = $true }
    '-e'        { $Execute = $true }
    default {
      [Console]::Error.WriteLine("unknown argument: $a")
      [Console]::Error.WriteLine('usage: build-artifacts-cleanup-2026-09-16.ps1 [--execute|-e]')
      exit 2
    }
  }
}

$RepoRoot = (git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if (-not $RepoRoot) {
  [Console]::Error.WriteLine('REFUSED: could not resolve the repo root from this script''s location.')
  exit 1
}
$RepoRoot = $RepoRoot.Trim() -replace '/', '\'

$Failed = $false
$script:Listed  = 0
$script:Removed = 0

function Mb($bytes) { '{0:N0} MB' -f ($bytes / 1MB) }

function Size($path) {
  if (-not (Test-Path -LiteralPath $path)) { return 0 }
  $item = Get-Item -LiteralPath $path
  if ($item.PSIsContainer) {
    return (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
  }
  return $item.Length
}

function Drop($rel, $why) {
  $path = Join-Path $RepoRoot $rel
  if (-not (Test-Path -LiteralPath $path)) {
    Write-Host "   - $rel"
    Write-Host '     already gone'
    return
  }

  $gitRel = $rel -replace '\\', '/'
  git -C $RepoRoot ls-files --error-unmatch -- $gitRel *> $null
  if ($LASTEXITCODE -eq 0) {
    Write-Host "   - $rel"
    Write-Host "     REFUSED: tracked by git. Propose it as a commit instead. Left alone."
    $script:Failed = $true
    return
  }

  $bytes = Size $path
  Write-Host "   - $rel  ($(Mb $bytes))"
  Write-Host "     $why"
  $script:Listed += $bytes
  if ($Execute) {
    try {
      Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
      Write-Host '     removed'
      $script:Removed += $bytes
    } catch {
      Write-Host "     FAILED: $($_.Exception.Message)"
      $script:Failed = $true
    }
  }
}

Write-Host ''
Write-Host "extrode.com/jauntyq build-artifacts cleanup 2026-09-16   mode: $(if ($Execute) { 'EXECUTE' } else { 'dry run' })"
Write-Host ''

Drop 'src/Extrode.JauntyQ.Cli/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'benchmarks/Extrode.JauntyQ.Benchmarks/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Generator.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Differential.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Schema.Extraction.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Cli.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Chinook.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.DdlSchema.Sqlite.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sample/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Aot.Smoke/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.SqlServer.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Pagila.Postgres.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.SqlServer.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Northwind.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Postgres.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.SqlServer.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.MySql.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.MariaDb.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.Postgres.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.MySql.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.MariaDb.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Postgres.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.Postgres.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.MySql.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.MySql.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.MariaDb.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Packaged.Smoke/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Pagila.Postgres.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Analysis.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.SqlParser.Tests/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Generator.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.SqlServer.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.MySql.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.MariaDb.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.Postgres.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Northwind.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sakila.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.AdventureWorksLite.SqlServer.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.SqlServer.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.MySql.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.MariaDb.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Postgres.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Chinook.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tools/Extrode.JauntyQ.Fuzz/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.SqlServer.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Schema.Extraction.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.MySql.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.MariaDb.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.EShopOnWeb.Postgres.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Conduit.Differential.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Generator/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Analysis/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.MySql.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Postgres.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Generator/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Cli.Core/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Schema/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.SqlParser.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Cli.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Sample/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Analysis/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'benchmarks/Extrode.JauntyQ.Benchmarks/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'tests/Extrode.JauntyQ.Analysis.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.DdlSchema.Sqlite.Tests/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.SqlParser/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Schema/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Cli/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Aot.Smoke/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Schema.Extraction/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Schema.Extraction/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.SqlParser/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'tools/Extrode.JauntyQ.Fuzz/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Cli.Core/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'samples/Extrode.JauntyQ.Packaged.Smoke/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Runtime/obj' "MSBuild intermediate output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Runtime/bin' "MSBuild output; 'dotnet build' regenerates it."
Drop 'src/Extrode.JauntyQ.Generator/build' "build output; regenerated by the project's build step."

Write-Host '--------'
if ($Execute) {
  Write-Host "Reclaimed: $(Mb $script:Removed)"
} else {
  Write-Host "Would reclaim: $(Mb $script:Listed)"
  Write-Host 'Pass --execute (or -e) to actually delete.'
}
Write-Host ''

if ($Execute -and $Failed) {
  Write-Host 'Finished with errors: see the refused steps above.'
  exit 1
}
exit 0
