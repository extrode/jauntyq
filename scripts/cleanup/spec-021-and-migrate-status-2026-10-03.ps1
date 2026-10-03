# Produced 2026-10-03 by the spec 021 (tenant scoping), migrate status and
# coverage work. Not run by the agent: the user runs it.
# 1. Deletes the merged branches of that work in jauntyq and jauntyq-premium
#    with git branch -d (never -D; git refuses an unmerged branch).
# 2. With -DeleteScratch as well, deletes the untracked scratch the work left
#    in tmp/ (mutation lists, edit helpers, the sample-diff output, a bundle).
# Dry run by default; -Execute (or -e) acts.
#   pwsh -NoProfile scripts/cleanup/spec-021-and-migrate-status-2026-10-03.ps1
#   pwsh -NoProfile scripts/cleanup/spec-021-and-migrate-status-2026-10-03.ps1 -e
#   pwsh -NoProfile scripts/cleanup/spec-021-and-migrate-status-2026-10-03.ps1 -e -DeleteScratch
param([Alias('e')][switch]$Execute, [switch]$DeleteScratch)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (git rev-parse --show-toplevel).Trim()
$premium = Join-Path (Split-Path $repo -Parent) 'jauntyq-premium'
Set-Location $repo

foreach ($r in @($repo, $premium)) {
    if (-not (Test-Path $r)) { continue }
    $current = (git -C $r rev-parse --abbrev-ref HEAD).Trim()
    if ($current -ne 'dev') {
        Write-Host "refuse: run with $r on dev (on '$current')."
        exit 1
    }
}
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="
$failed = $false

function Remove-Branches([string]$r, [string[]]$targets) {
    foreach ($target in $targets) {
        git -C $r rev-parse --verify --quiet "refs/heads/$target" > $null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "skip:  $target no longer exists"
        } elseif ($Execute) {
            git -C $r branch -d $target
            if ($LASTEXITCODE -ne 0) { Write-Host "  refused: $target is not merged. Left alone."; $script:failed = $true }
        } else {
            Write-Host "would delete: $target"
        }
    }
}

Write-Host "`n1a. Merged branches in jauntyq"
Remove-Branches $repo @(
    'feat/migrate-status',
    'fix/usage-lists-every-premium-verb',
    'docs/coverage-2026-10-03',
    'feat/021-scope-refusal',
    'feat/021-scoped-auto-crud',
    'docs/021-tenant-scoping')

Write-Host "`n1b. Merged branches in jauntyq-premium"
if (Test-Path $premium) {
    Remove-Branches $premium @(
        'chore/status-refresh-2026-10-03',
        'docs/021-plan-and-tasks',
        'docs/021-phase-a-done',
        'docs/021-phase-b-done',
        'docs/021-done')
} else {
    Write-Host "skip:  $premium does not exist"
}

Write-Host "`n2. Untracked scratch in tmp/"
$scratch = @(
    'tmp/check-body.txt', 'tmp/dev-ec0af88.bundle', 'tmp/mutate-021.py',
    'tmp/edit_bulk.py', 'tmp/edit_bulk2.py', 'tmp/edit_part4.py', 'tmp/edit_part4b.py',
    'tmp/edit_t4.py', 'tmp/edit_t6.py', 'tmp/edit_t7.py',
    'tmp/m-t1.json', 'tmp/m-t2.json', 'tmp/m-t3.json', 'tmp/m-t3b.json', 'tmp/m-t4.json',
    'tmp/m-t4.out', 'tmp/m-t4b.json', 'tmp/m-t5.json', 'tmp/m-t5b.json', 'tmp/m-t6.json',
    'tmp/m-t6b.json', 'tmp/m-t7.json', 'tmp/m-t8.json', 'tmp/m-t8b.json', 'tmp/m-t8c.json',
    'tmp/t12-build.sh', 'tmp/t12')
foreach ($rel in $scratch) {
    $path = Join-Path $repo $rel
    if (-not (Test-Path $path)) {
        Write-Host "skip:  $rel no longer exists"
        continue
    }
    git ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $rel is tracked by git. Propose it as a commit instead. Left alone."
        $failed = $true
        continue
    }
    if ($Execute -and $DeleteScratch) {
        Remove-Item -Recurse -Force $path
        Write-Host "deleted: $rel"
    } elseif ($Execute) {
        Write-Host "keep:  $rel (pass -DeleteScratch to delete untracked files)"
    } else {
        Write-Host "would delete (with -DeleteScratch): $rel"
    }
}

Write-Host "`nRemaining branches:"
git branch --format='  jauntyq: %(refname:short)'
if (Test-Path $premium) { git -C $premium branch --format='  premium: %(refname:short)' }
if ($Execute -and $failed) { exit 1 }
