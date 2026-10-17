#!/usr/bin/env bash
# Produced by chore/regen-sqlserver-seed (2026-10-17). The merged branch and the re-downloaded
# canonical instnwnd.sql plus the fixture test log.
#
#   scripts/cleanup/regen-sqlserver-seed.sh                            # dry run
#   scripts/cleanup/regen-sqlserver-seed.sh -e                         # delete the merged branch
#   scripts/cleanup/regen-sqlserver-seed.sh -e --delete-scratch        # also tmp/seed-src
set -euo pipefail

execute=0; delete_scratch=0
for a in "$@"; do
  case "$a" in
    -e|--execute) execute=1 ;;
    --delete-scratch) delete_scratch=1 ;;
    *) printf 'unknown flag: %s\n' "$a" >&2; exit 2 ;;
  esac
done

repo="$(cd "$(dirname "$0")/../.." && pwd -P)"
cur="$(git -C "$repo" rev-parse --abbrev-ref HEAD)"
if [ "$cur" != "dev" ]; then
  printf 'refuse: on branch %s, expected dev.\n' "$cur"; exit 1
fi
failed=0

echo "== 1. merged branch"
branches=(chore/regen-sqlserver-seed)
for b in "${branches[@]}"; do
  if ! git -C "$repo" rev-parse --verify --quiet "refs/heads/$b" >/dev/null; then
    printf 'skip:  branch %s no longer exists\n' "$b"
  elif [ $execute -eq 1 ]; then
    git -C "$repo" branch -d "$b" || { echo "  refused: $b is not merged. Left alone."; failed=1; }
  else
    printf 'would: git branch -d %s\n' "$b"
  fi
done

echo "== 2. scratch"
for rel in tmp/seed-src; do
  if [ ! -e "$repo/$rel" ]; then
    printf 'skip:  %s is gone\n' "$rel"; continue
  fi
  if [ -n "$(git -C "$repo" ls-files -- "$rel")" ]; then
    printf 'refuse: %s holds tracked files. Propose it as a commit instead. Left alone.\n' "$rel"
    failed=1; continue
  fi
  if [ $execute -eq 1 ] && [ $delete_scratch -eq 1 ]; then
    rm -rf -- "${repo:?}/$rel" && printf 'deleted: %s\n' "$rel"
  else
    printf 'would delete: %s  (needs -e --delete-scratch)\n' "$rel"
  fi
done

echo "== remaining"
for b in "${branches[@]}"; do git -C "$repo" branch --list "$b"; done
[ -e "$repo/tmp/seed-src" ] && echo "tmp/seed-src"
exit $failed
