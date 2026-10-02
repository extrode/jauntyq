# Produced 2026-10-02 after the emitter mutation run and the coverage work (99% line, nine
# never-run methods covered). Not run by the agent: the user runs it.
# Removes the two helper worktrees that work created, then deletes its local branches with
# git branch -d, which refuses any branch whose commits are not in dev.
# Remote branch run/mutation-emitter is not touched: deleting it is a push.
#
#   pwsh -NoProfile scripts/cleanup/coverage-and-emitter-mutation-2026-10-02.ps1      # dry run
#   pwsh -NoProfile scripts/cleanup/coverage-and-emitter-mutation-2026-10-02.ps1 -e   # apply
param([switch]$Execute)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo

$current = (git rev-parse --abbrev-ref HEAD).Trim()
if ($current -ne 'dev') {
    Write-Host "refuse: run from dev (on '$current')."
    exit 1
}
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="
$failed = $false

Write-Host "`n1. Helper worktrees"
$worktrees = @(
    '.claude/worktrees/agent-a15593e8bdf3f1d6b',
    '.claude/worktrees/agent-aa26a386dcf7c40b8'
)
foreach ($rel in $worktrees) {
    $path = Join-Path $repo $rel
    if (-not (Test-Path $path)) { Write-Host "skip:  $rel is already gone"; continue }
    git -C $repo ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $rel is tracked by git. Propose it as a commit instead. Left alone."
        $failed = $true
        continue
    }
    if (git -C $path status --porcelain) {
        Write-Host "keep:  $rel has uncommitted changes. Left alone."
        $failed = $true
        continue
    }
    if ($Execute) {
        git worktree remove $path
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused by git worktree remove. Left alone."; $failed = $true }
        else { Write-Host "removed: $rel" }
    } else { Write-Host "would remove: $rel" }
}
if ($Execute) { git worktree prune }

Write-Host "`n2. Branches from this work"
$branches = @(
    'chore/emitter-mutation-on-blacksmith',
    'chore/mutation-emitter-push-trigger',
    'ci/mutation-emitter-push-trigger',
    'docs/emitter-mutation-confirmed',
    'docs/emitter-mutation-counts',
    'docs/emitter-mutation-tally',
    'docs/q7-and-runner-contract',
    'test/emitter-mutation-gaps',
    'test/emitter-mutation-survivors',
    'test/coverage-count-shared-source-once',
    'fix/coverage-keep-schema-copy',
    'docs/coverage-2026-10-02',
    'test/cover-remaining-methods'
)
foreach ($b in $branches) {
    git rev-parse --verify --quiet "refs/heads/$b" > $null
    if ($LASTEXITCODE -ne 0) { Write-Host "skip:  branch $b no longer exists"; continue }
    if ($Execute) {
        git branch -d $b
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused: $b is not merged or still checked out. Left alone."; $failed = $true }
    } else { Write-Host "would delete: $b" }
}

Write-Host "`nRemaining worktrees:"
git worktree list
Write-Host "Remaining branches: $((git for-each-ref refs/heads | Measure-Object).Count)"
if ($Execute -and $failed) { exit 1 }
