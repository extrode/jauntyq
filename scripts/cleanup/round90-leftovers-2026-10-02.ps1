# Produced 2026-10-02 by the round-90 leftovers work (fix/round90-leftovers,
# fix/snapshot-explicit-null). Not run by the agent: the user runs it.
# Removes the .worktrees/probe worktree, then deletes the two merged branches
# with git branch -d (never -D; git refuses an unmerged branch).
# Dry run by default; -Execute (or -e) acts.
#   pwsh -NoProfile scripts/cleanup/round90-leftovers-2026-10-02.ps1
#   pwsh -NoProfile scripts/cleanup/round90-leftovers-2026-10-02.ps1 -e
param([Alias('e')][switch]$Execute)
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

Write-Host "`n1. Worktree .worktrees/probe"
$rel = '.worktrees/probe'
$path = Join-Path $repo $rel
$registered = (git worktree list --porcelain) -contains "worktree $($path -replace '\\', '/')"
if (-not (Test-Path $path) -and -not $registered) {
    Write-Host "skip:  $rel no longer exists"
} else {
    git ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $rel is tracked by git. Propose it as a commit instead. Left alone."
        $failed = $true
    } elseif (Test-Path $path) {
        $head = (git -C $path rev-parse HEAD).Trim()
        git merge-base --is-ancestor $head dev 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "keep:  $rel has commits not in dev. Left alone."
            $failed = $true
        } elseif (git -C $path status --porcelain) {
            Write-Host "keep:  $rel has uncommitted changes. Left alone."
            $failed = $true
        } elseif ($Execute) {
            git worktree remove $path
            if ($LASTEXITCODE -ne 0) { Write-Host '  refused by git. Left alone.'; $failed = $true }
            else { Write-Host "removed: $rel" }
        } else {
            Write-Host "would remove: $rel"
        }
    }
}
if ($Execute) { git worktree prune }

Write-Host "`n2. Merged branches"
foreach ($b in @('fix/round90-leftovers', 'fix/snapshot-explicit-null')) {
    git rev-parse --verify --quiet "refs/heads/$b" > $null
    if ($LASTEXITCODE -ne 0) { Write-Host "skip:  branch $b no longer exists"; continue }
    if ($Execute) {
        git branch -d $b
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused: $b not merged or still checked out. Left alone."; $failed = $true }
    } else {
        Write-Host "would run: git branch -d $b"
    }
}

Write-Host "`nRemaining worktrees:"
git worktree list
if ($Execute -and $failed) { exit 1 }
