# Cleanup for the 2026-09-24 /code-review run: ~/.claude/tools/html/build.js wrote its hub page and
# theme assets into docs/ alongside the tmp/ report. build.js regenerates them on every run, so
# nothing here is lost. Each target is checked against git at run time; tracked paths are refused.
#
# Dry run by default. --execute / -e acts. Deleting the untracked files is irreversible and needs
# --delete-untracked on top of --execute.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$Execute         = $false
$DeleteUntracked = $false

foreach ($a in $args) {
  switch ($a) {
    '--execute'          { $Execute = $true }
    '-e'                 { $Execute = $true }
    '--delete-untracked' { $DeleteUntracked = $true }
    default {
      [Console]::Error.WriteLine("unknown flag: $a")
      [Console]::Error.WriteLine('usage: code-review-html-output.ps1 [--execute|-e] [--delete-untracked]')
      exit 2
    }
  }
}

$repo = (git rev-parse --show-toplevel)
if (-not $repo) { Write-Error 'Not inside a git repository.'; exit 1 }
Set-Location $repo
$Failed = $false

if ($Execute) { Write-Host '=== EXECUTING ===' } else { Write-Host '=== DRY RUN (pass --execute to act) ===' }
Write-Host ''

# --- 1. build.js hub and theme assets -----------------------------------------
Write-Host '1. build.js hub and theme assets (--delete-untracked)'
$targets = @(
  'docs/index.html',
  'docs/assets/LICENSE-fonts.txt',
  'docs/assets/artifact.css',
  'docs/assets/artifact.js',
  'docs/assets/fonts'
)
foreach ($rel in $targets) {
  if (-not (Test-Path $rel)) { Write-Host "   skip: $rel (already gone)"; continue }
  $tracked = git ls-files -- $rel
  if ($tracked) {
    Write-Host "   refuse: $rel is tracked by git. Propose it as a commit instead. Left alone."
    $Failed = $true
    continue
  }
  Write-Host "   $rel"
  if ($Execute -and $DeleteUntracked) {
    Remove-Item -Recurse -Force -- $rel
  } elseif ($Execute) {
    Write-Host '   needs --delete-untracked as well; left alone'
  }
}
Write-Host ''

Write-Host 'Remaining:'
foreach ($rel in $targets) { if (Test-Path $rel) { Write-Host "   $rel" } }
Write-Host ''

if (-not $Execute) {
  Write-Host 'Dry run: nothing was changed.'
  exit 0
}
if ($Failed) {
  Write-Host 'Finished with errors: see the refused steps above.'
  exit 1
}
Write-Host 'Done.'
exit 0
