# Produced 2026-10-01 after the Stryker disable audit. Not run by the agent: the user runs it.
# Removes worktrees whose commit is already reachable from dev and that have no uncommitted
# changes, then deletes local branches fully merged into dev (git branch -d, never -D).
# Dry run by default; -Execute acts.
param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo

$current = (git rev-parse --abbrev-ref HEAD).Trim()
if ($current -ne 'dev') {
    Write-Host "refuse: run from dev (on '$current')."
    exit 1
}
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="

Write-Host "`n1. Worktrees merged into dev and clean"
$paths = @()
foreach ($line in (git worktree list --porcelain)) {
    if ($line -like 'worktree *') { $paths += $line.Substring(9) }
}
foreach ($path in $paths) {
    if ((Resolve-Path $path).Path -eq (Resolve-Path $repo).Path) { continue }
    if (-not (Test-Path $path)) { Write-Host "skip (gone): $path"; continue }
    $head = (git -C $path rev-parse HEAD).Trim()
    git merge-base --is-ancestor $head dev 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Host "keep (commits not in dev): $path"; continue }
    if (git -C $path status --porcelain) { Write-Host "keep (uncommitted changes): $path"; continue }
    if ($Execute) { git worktree remove $path; Write-Host "removed: $path" }
    else { Write-Host "would remove: $path" }
}
git worktree prune

Write-Host "`n2. Local branches fully merged into dev"
$keep = @('main', 'dev', $current)
foreach ($b in (git for-each-ref --format='%(refname:short)' refs/heads)) {
    if ($keep -contains $b) { continue }
    git merge-base --is-ancestor $b dev 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Host "keep (not merged): $b"; continue }
    if ($Execute) { git branch -d $b | Out-Null; Write-Host "deleted: $b" }
    else { Write-Host "would delete: $b" }
}

Write-Host "`nRemaining worktrees:"
git worktree list
Write-Host "Remaining branches: $((git for-each-ref refs/heads | Measure-Object).Count)"
