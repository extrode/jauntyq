# Produced 2026-10-03 by the name-collision audit, the pre-0.6.2 audit fixes
# and the 0.6.2 cut. Not run by the agent: the user runs it.
# 1. Removes the two verification worktrees with git worktree remove (never
#    --force; git refuses a worktree with changes).
# 2. Deletes the merged branches of that work with git branch -d (never -D;
#    git refuses an unmerged branch).
# 3. With -DeleteScratch as well, deletes the untracked scratch the work left
#    in tmp/ (audit findings and diffs, probes, edit helpers, test logs).
# Dry run by default; -Execute (or -e) acts.
#   pwsh -NoProfile scripts/cleanup/audit-fixes-and-0-6-2-2026-10-03.ps1
#   pwsh -NoProfile scripts/cleanup/audit-fixes-and-0-6-2-2026-10-03.ps1 -e
#   pwsh -NoProfile scripts/cleanup/audit-fixes-and-0-6-2-2026-10-03.ps1 -e -DeleteScratch
param([Alias('e')][switch]$Execute, [switch]$DeleteScratch)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo

$current = (git rev-parse --abbrev-ref HEAD).Trim()
if ($current -ne 'dev') {
    Write-Host "refuse: run from the main checkout on dev (on '$current')."
    exit 1
}
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="
$failed = $false

Write-Host "`n1. Verification worktrees"
foreach ($rel in @('.worktrees/verify-parser', '.worktrees/verify-scope')) {
    $path = Join-Path $repo $rel
    if (-not (Test-Path $path)) {
        Write-Host "skip:  $rel no longer exists"
    } elseif ($Execute) {
        git worktree remove $rel
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused: $rel has changes. Left alone."; $failed = $true }
    } else {
        Write-Host "would remove worktree: $rel"
    }
}

Write-Host "`n2. Merged branches"
$branches = @(
    'fix/name-collision-give-way',
    'fix/name-collision-refusals',
    'fix/name-collision-review',
    'fix/correlated-subquery-binding',
    'fix/scope-file-mistakes',
    'fix/scope-proof-before-limit',
    'fix/select-list-exists-scope',
    'fix/migration-explicit-charset',
    'fix/migration-mysql-enum-cleanup',
    'fix/migration-postgres-quoted-create',
    'fix/upsert-set-parameter-typing',
    'chore/release-0.6.2')
foreach ($target in $branches) {
    git rev-parse --verify --quiet "refs/heads/$target" > $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "skip:  $target no longer exists"
    } elseif ($Execute) {
        git branch -d $target
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused: $target is not merged or is checked out. Left alone."; $failed = $true }
    } else {
        Write-Host "would delete: $target"
    }
}

Write-Host "`n3. Untracked scratch in tmp/"
$scratch = @(
    'tmp/ZProbe061.cs', 'tmp/ZProbe061.cs.last',
    'tmp/audit-a1-scoping-findings.md', 'tmp/audit-a1-scoping.diff',
    'tmp/audit-a2-emitter-findings.md', 'tmp/audit-a2-emitter.diff',
    'tmp/audit-b-migrations-findings.md', 'tmp/audit-b-migrations.diff',
    'tmp/audit-c-parser-schema-findings.md', 'tmp/audit-c-parser-schema.diff',
    'tmp/audit-changelog-findings.md', 'tmp/audit-since-061.diff',
    'tmp/claims-migrations.md', 'tmp/claims-parser.md', 'tmp/claims-scope.md',
    'tmp/fable-verify-fixes.md', 'tmp/review-fixes.diff',
    'tmp/mutate-giveway.py', 'tmp/mutate-refusals.py',
    'tmp/name-collision-probe-before.json', 'tmp/name-collision-probe.json', 'tmp/name-collision-summary.md',
    'tmp/review-namecoll-emitter.diff', 'tmp/review-namecoll-findings-emitter.md',
    'tmp/review-namecoll-findings-gen.md', 'tmp/review-namecoll-findings.md',
    'tmp/review-namecoll-gen.diff', 'tmp/review-namecoll.diff',
    'tmp/zdump.txt', 'tmp/zscratch-analysis-dump', 'tmp/test-all-0.6.2.log')
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

Write-Host "`nRemaining worktrees:"
git worktree list
Write-Host "`nRemaining branches:"
git branch --format='  %(refname:short)'
if ($Execute -and $failed) { exit 1 }
