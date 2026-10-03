# Produced 2026-10-03 by the name-collision audit, the pre-release audit
# fixes, the fable verification and the 0.7.0 cut (which replaced a local,
# never-pushed 0.6.2 cut). Not run by the agent: the user runs it.
# 1. Removes the two verification worktrees with git worktree remove (never
#    --force; git refuses a worktree with changes).
# 2. Deletes the merged branches of that work with git branch -d (never -D;
#    git refuses an unmerged branch).
# 3. With -DeleteScratch as well, deletes the untracked scratch the work left
#    in tmp/ (audit findings and diffs, probes, edit helpers, test logs).
# 4. With -DeleteBackups as well, deletes the backups taken before the 0.6.2
#    cut was undone: tags backup/pre-0.7.0-main and backup/pre-0.7.0-dev and
#    tmp/pre-0.7.0-backup.bundle. With chore/release-0.6.2 they are the only
#    copies of that cut outside the reflog, so they are off by default.
#    chore/release-0.6.2 is never deleted here: it is not merged (its commits
#    were cherry-picked onto chore/release-0.7.0), and -d would refuse it.
# Dry run by default; -Execute (or -e) acts.
#   pwsh -NoProfile scripts/cleanup/release-0-7-0-2026-10-03.ps1
#   pwsh -NoProfile scripts/cleanup/release-0-7-0-2026-10-03.ps1 -e
#   pwsh -NoProfile scripts/cleanup/release-0-7-0-2026-10-03.ps1 -e -DeleteScratch
#   pwsh -NoProfile scripts/cleanup/release-0-7-0-2026-10-03.ps1 -e -DeleteBackups
param([Alias('e')][switch]$Execute, [switch]$DeleteScratch, [switch]$DeleteBackups)
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
    'fix/qualified-quoted-create-table',
    'fix/scope-file-unreadable-is-error',
    'fix/scope-proof-region-ends',
    'fix/upsert-update-branch-scope',
    'fix/binding-scope-unknown-relation',
    'fix/impact-walks-exists-expressions',
    'fix/inline-enum-explicit-charset',
    'chore/release-0.7.0')
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
    'tmp/zdump.txt', 'tmp/zscratch-analysis-dump', 'tmp/test-all-0.6.2.log',
    'tmp/fable-verify-070.md', 'tmp/verify-070-changelog.diff', 'tmp/verify-070-commits.txt',
    'tmp/verify-070-prompt.md', 'tmp/verify-070-src.diff', 'tmp/test-all-0.7.0.log')
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

Write-Host "`n4. Pre-0.7.0 backups"
foreach ($tag in @('backup/pre-0.7.0-main', 'backup/pre-0.7.0-dev')) {
    git rev-parse --verify --quiet "refs/tags/$tag" > $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "skip:  tag $tag no longer exists"
    } elseif ($Execute -and $DeleteBackups) {
        git tag -d $tag
        if ($LASTEXITCODE -ne 0) { Write-Host "  refused: tag $tag. Left alone."; $failed = $true }
    } elseif ($Execute) {
        Write-Host "keep:  tag $tag (pass -DeleteBackups to delete)"
    } else {
        Write-Host "would delete (with -DeleteBackups): tag $tag"
    }
}
$bundle = 'tmp/pre-0.7.0-backup.bundle'
$bundlePath = Join-Path $repo $bundle
if (-not (Test-Path $bundlePath)) {
    Write-Host "skip:  $bundle no longer exists"
} else {
    git ls-files --error-unmatch -- $bundle > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $bundle is tracked by git. Propose it as a commit instead. Left alone."
        $failed = $true
    } elseif ($Execute -and $DeleteBackups) {
        Remove-Item -Force $bundlePath
        Write-Host "deleted: $bundle"
    } elseif ($Execute) {
        Write-Host "keep:  $bundle (pass -DeleteBackups to delete)"
    } else {
        Write-Host "would delete (with -DeleteBackups): $bundle"
    }
}
git rev-parse --verify --quiet refs/heads/chore/release-0.6.2 > $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "note:  chore/release-0.6.2 is kept. It is not merged, so this script does not delete it."
}

Write-Host "`nRemaining worktrees:"
git worktree list
Write-Host "`nRemaining branches:"
git branch --format='  %(refname:short)'
if ($Execute -and $failed) { exit 1 }
