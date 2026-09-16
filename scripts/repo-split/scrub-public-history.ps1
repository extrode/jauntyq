# Rewrites jauntyq's git history to remove:
#   - the two commit messages naming the old company as "Extrode LLC" /
#     the old GitHub org "github.com/extrode/jauntyq" (replaced with Extrode)
#   - premium/paid-tier test-suite names briefly committed to scripts/coverage.sh
#     before this repo's first public push
#   - a dangling internal-doc path reference in the same early commit
#
# Author/committer identity (Syed Beparey <syed@beparey.com>) is left
# untouched on purpose -- it is the maintainer's own verified identity, not
# a leak, and is not scrubbed by this script.
#
# Usage:
#   .\scrub-public-history.ps1                    dry run (default, no changes)
#   .\scrub-public-history.ps1 -Execute           rewrite local history
#   .\scrub-public-history.ps1 -Execute -ForcePush
#                                                  rewrite, then force-push dev

param(
    [switch]$Execute,
    [switch]$ForcePush
)

$Here = $PSScriptRoot
$RepoRoot = (Resolve-Path (Join-Path $Here "..\..")).Path
$OriginUrl = "https://github.com/extrode/jauntyq.git"
$ExpectedDevSha = "8a670317f00c0083f0beb7b4763c70085fdb1f97"

function Ok    { param($m) Write-Output "ok:    $m" }
function SkipMsg { param($m) Write-Output "skip:  $m" }
function Doing { param($m) Write-Output "do:    $m" }
function Dry   { param($m) Write-Output "dry:   $m" }
function Bad   { param($m) Write-Output "FAIL:  $m" }

Set-Location $RepoRoot

$status = git status --porcelain
if ($status) {
    Bad "working tree is not clean; commit or stash before running this"
    exit 1
}

$AlreadyRan = Join-Path $RepoRoot ".git\filter-repo\already_ran"

if (-not $Execute) {
    Dry "previewing rewrite (git filter-repo --dry-run); no changes will be made"
    # --force is required even for --dry-run: filter-repo refuses to run at all
    # (dry-run or not) unless the repo is a fresh clone or --force is given.
    Remove-Item -Force -ErrorAction SilentlyContinue $AlreadyRan
    git filter-repo --force --dry-run `
        --replace-message (Join-Path $Here "scrub-message-rules.txt") `
        --replace-text (Join-Path $Here "scrub-text-rules.txt")
    Dry "review .git/filter-repo/ for the analysis; re-run with -Execute to apply"
    exit 0
}

$tmpDir = Join-Path $RepoRoot "tmp"
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$stamp = Get-Date -Format "yyyyMMddTHHmmss"
$backup = Join-Path $tmpDir "pre-scrub-backup-$stamp.bundle"
Doing "backing up full history to $backup before rewriting"
git bundle create $backup --all
Ok "backup written: $backup"

Doing "rewriting history (git filter-repo --force)"
Remove-Item -Force -ErrorAction SilentlyContinue $AlreadyRan
git filter-repo --force `
    --replace-message (Join-Path $Here "scrub-message-rules.txt") `
    --replace-text (Join-Path $Here "scrub-text-rules.txt")
Ok "history rewritten"

Doing "re-adding origin remote (filter-repo removes it as a safety measure)"
git remote remove origin 2>$null
git remote add origin $OriginUrl
Ok "origin set to $OriginUrl"

if (-not $ForcePush) {
    SkipMsg "not pushing (pass -ForcePush to force-push dev over origin)"
    exit 0
}

Doing "force-pushing dev to origin (lease pinned to the known pre-rewrite tip)"
git push origin dev "--force-with-lease=dev:$ExpectedDevSha"
Ok "pushed"
