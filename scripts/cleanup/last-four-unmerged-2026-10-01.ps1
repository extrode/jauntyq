# Produced 2026-10-01. Not run by the agent: the user runs it.
# Removes the 2 worktrees and 4 branches left over after stale-worktrees-and-branches-2026-10-01.ps1.
# Their commits are not in dev; all four were judged superseded (see the report in the session).
# Dry run by default. -Execute removes the worktrees. Deleting the branches discards unmerged commits
# and needs -DiscardUnmerged as well (git branch -D).
param([switch]$Execute, [switch]$DiscardUnmerged)
$ErrorActionPreference = 'Stop'
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo

$worktrees = @(
    '.claude/worktrees/agent-ac2244e3416376250',
    '.worktrees/chore-mutation-runner'
)
$branches = @(
    'chore/mutation-runner',
    'temp-merge',
    'test/analysis-near-target-cleanup',
    'test/migrationparser-per-mutant-pass'
)
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="

Write-Host "`n1. Worktrees"
foreach ($w in $worktrees) {
    if (-not (Test-Path $w)) { Write-Host "skip (gone): $w"; continue }
    if (git -C $w status --porcelain) { Write-Host "keep (uncommitted changes): $w"; continue }
    if ($Execute) { git worktree remove $w; Write-Host "removed: $w" }
    else { Write-Host "would remove: $w" }
}
git worktree prune

Write-Host "`n2. Branches (unmerged, git branch -D)"
foreach ($b in $branches) {
    git rev-parse --verify --quiet "refs/heads/$b" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "skip (gone): $b"; continue }
    if ($Execute -and $DiscardUnmerged) { git branch -D $b | Out-Null; Write-Host "deleted: $b" }
    elseif ($Execute) { Write-Host "kept (pass -DiscardUnmerged to delete): $b" }
    else { Write-Host "would delete: $b" }
}
git worktree list
