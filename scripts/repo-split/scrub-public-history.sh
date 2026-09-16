#!/usr/bin/env bash
set -euo pipefail

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
#   ./scrub-public-history.sh                    dry run (default, no changes)
#   ./scrub-public-history.sh --execute          rewrite local history
#   ./scrub-public-history.sh --execute --force-push
#                                                 rewrite, then force-push dev

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ORIGIN_URL="https://github.com/extrode/jauntyq.git"
EXPECTED_DEV_SHA="8a670317f00c0083f0beb7b4763c70085fdb1f97"

EXECUTE=0
FORCE_PUSH=0
for arg in "$@"; do
  case "$arg" in
    --execute|-e) EXECUTE=1 ;;
    --force-push) FORCE_PUSH=1 ;;
    *) echo "unknown flag: $arg" >&2; exit 1 ;;
  esac
done

ok()    { printf 'ok:    %s\n' "$1"; }
skip()  { printf 'skip:  %s\n' "$1"; }
doing() { printf 'do:    %s\n' "$1"; }
dry()   { printf 'dry:   %s\n' "$1"; }
bad()   { printf 'FAIL:  %s\n' "$1" >&2; }

cd "$REPO_ROOT"

if [[ -n "$(git status --porcelain)" ]]; then
  bad "working tree is not clean; commit or stash before running this"
  exit 1
fi

if [[ "$EXECUTE" -eq 0 ]]; then
  dry "previewing rewrite (git filter-repo --dry-run); no changes will be made"
  git filter-repo --dry-run \
    --replace-message "$HERE/scrub-message-rules.txt" \
    --replace-text "$HERE/scrub-text-rules.txt"
  dry "review .git/filter-repo/ for the analysis; re-run with --execute to apply"
  exit 0
fi

mkdir -p "$REPO_ROOT/tmp"
BACKUP="$REPO_ROOT/tmp/pre-scrub-backup-$(date +%Y%m%dT%H%M%S).bundle"
doing "backing up full history to $BACKUP before rewriting"
git bundle create "$BACKUP" --all
ok "backup written: $BACKUP"

doing "rewriting history (git filter-repo --force)"
git filter-repo --force \
  --replace-message "$HERE/scrub-message-rules.txt" \
  --replace-text "$HERE/scrub-text-rules.txt"
ok "history rewritten"

doing "re-adding origin remote (filter-repo removes it as a safety measure)"
git remote remove origin 2>/dev/null || true
git remote add origin "$ORIGIN_URL"
ok "origin set to $ORIGIN_URL"

if [[ "$FORCE_PUSH" -eq 0 ]]; then
  skip "not pushing (pass --force-push to force-push dev over origin)"
  exit 0
fi

doing "force-pushing dev to origin (lease pinned to the known pre-rewrite tip)"
git push origin dev "--force-with-lease=dev:${EXPECTED_DEV_SHA}"
ok "pushed"
