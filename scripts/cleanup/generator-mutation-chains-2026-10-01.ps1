# Produced 2026-10-01 by the generator mutation-survivor work (Stryker chains 3-11 on mb1,
# group A/B/C fixes, v0.6.1 pack check). Not run by the agent: the user runs it, after the
# group C branch has merged into dev.
#   pwsh -NoProfile scripts/cleanup/generator-mutation-chains-2026-10-01.ps1            # dry run
#   pwsh -NoProfile scripts/cleanup/generator-mutation-chains-2026-10-01.ps1 -e         # worktree + mb1
#   pwsh -NoProfile scripts/cleanup/generator-mutation-chains-2026-10-01.ps1 -e -DeleteScratch
# -Execute removes the v0.6.1-pack worktree and the chain files under mb1's /tmp.
# -DeleteScratch additionally deletes the untracked scratch under tmp/ listed in section 2.
param([switch]$Execute, [switch]$DeleteScratch)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (git rev-parse --show-toplevel).Trim()
Set-Location $repo
$ssh = 'C:\Windows\System32\OpenSSH\ssh.exe'
$failed = $false

$current = (git rev-parse --abbrev-ref HEAD).Trim()
if ($current -ne 'dev') {
    Write-Host "refuse: run from dev (on '$current')."
    exit 1
}
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY RUN' }
Write-Host "== $mode =="

Write-Host "`n1. Worktree .worktrees/v0.6.1-pack"
$wt = Join-Path $repo '.worktrees/v0.6.1-pack'
if (-not (Test-Path $wt)) {
    Write-Host 'skip: already gone'
} else {
    $head = (git -C $wt rev-parse HEAD).Trim()
    if (-not (git branch --contains $head)) {
        Write-Host "keep: $head is not on any branch"; $failed = $true
    } elseif (git -C $wt status --porcelain) {
        Write-Host 'keep: uncommitted changes'; $failed = $true
    } elseif ($Execute) {
        git worktree remove $wt
        if ($LASTEXITCODE -ne 0) { Write-Host '  refused by git. Left alone.'; $failed = $true }
        else { Write-Host "removed: $wt" }
    } else { Write-Host "would remove: $wt" }
}
if ($Execute) { git worktree prune }

Write-Host "`n2. Untracked scratch under tmp/ (needs -DeleteScratch)"
$scratch = @(
    'tmp/chain3.sh', 'tmp/chain5.sh', 'tmp/chain6.sh', 'tmp/chain7.sh', 'tmp/chain8.sh',
    'tmp/chain9.sh', 'tmp/chain10.sh', 'tmp/chain11.sh',
    'tmp/x2.bundle', 'tmp/x3.bundle', 'tmp/x4.bundle', 'tmp/x5.bundle', 'tmp/x6.bundle',
    'tmp/x9.bundle', 'tmp/x10.bundle', 'tmp/x11.bundle', 'tmp/x12.bundle', 'tmp/x13.bundle',
    'tmp/x14.bundle', 'tmp/final.bundle', 'tmp/final2.bundle',
    'tmp/mb1', 'tmp/mut', 'tmp/mut3', 'tmp/dbg', 'tmp/cov', 'tmp/trx',
    'tmp/surv.js', 'tmp/mutapply.py'
)
foreach ($rel in $scratch) {
    if (-not (Test-Path $rel)) { Write-Host "skip:  $rel already gone"; continue }
    git ls-files --error-unmatch -- $rel > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "refuse: $rel is tracked by git. Propose it as a commit instead. Left alone."
        $failed = $true
        continue
    }
    if ($Execute -and $DeleteScratch) { Remove-Item -Recurse -Force $rel; Write-Host "deleted: $rel" }
    else { Write-Host "would delete: $rel" }
}

Write-Host "`n3. mb1 /tmp chain files"
$mb1Files = '/tmp/chain10.sh /tmp/chain11.sh /tmp/x13.bundle /tmp/x14.bundle'
$busy = & $ssh mb1.local 'pgrep -f "chain1[01].sh|Stryker.CLI" >/dev/null && echo busy || echo idle'
if ($LASTEXITCODE -ne 0) {
    Write-Host 'skip: mb1.local unreachable'; $failed = $true
} elseif ($busy -eq 'busy') {
    Write-Host 'keep: a chain or Stryker run is still going on mb1'; $failed = $true
} elseif ($Execute) {
    & $ssh mb1.local "rm -f $mb1Files; ls $mb1Files 2>&1"
} else {
    & $ssh mb1.local "ls -l $mb1Files 2>&1"
}

Write-Host "`nRemaining worktrees:"
git worktree list
Write-Host "tmp/ entries: $((Get-ChildItem tmp | Measure-Object).Count)"
if ($Execute -and $failed) { exit 1 }
