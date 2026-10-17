#!/usr/bin/env bash
# Produced by feat/sqlserver-seed-csharp (2026-10-17). The merged branch, the local parity scratch
# (instnwnd.sql download, case outputs, bundle), and the copy the wp1 parity run left in wp1's ~/tmp.
#
#   scripts/cleanup/sqlserver-seed-csharp.sh                            # dry run
#   scripts/cleanup/sqlserver-seed-csharp.sh -e                         # delete the merged branch
#   scripts/cleanup/sqlserver-seed-csharp.sh -e --delete-scratch        # also tmp/port
#   scripts/cleanup/sqlserver-seed-csharp.sh -e --delete-wp1-files      # also wp1's ~/tmp/jauntyq-seed (over ssh)
set -euo pipefail

execute=0; delete_scratch=0; delete_wp1=0
for a in "$@"; do
  case "$a" in
    -e|--execute) execute=1 ;;
    --delete-scratch) delete_scratch=1 ;;
    --delete-wp1-files) delete_wp1=1 ;;
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
branches=(feat/sqlserver-seed-csharp)
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
for rel in tmp/port; do
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

echo "== 3. files on wp1 (~/tmp)"
wp1_files=('tmp\jauntyq-seed')
for w in "${wp1_files[@]}"; do
  if [ $execute -eq 1 ] && [ $delete_wp1 -eq 1 ]; then
    if ssh -o BatchMode=yes wp1 "Remove-Item -LiteralPath \$HOME\\$w -Recurse -Force -ErrorAction Stop" 2>/dev/null; then
      printf 'deleted: wp1:%s\n' "$w"
    else
      printf 'failed or gone: wp1:%s\n' "$w"; failed=1
    fi
  else
    printf 'would delete: wp1:%s  (needs -e --delete-wp1-files)\n' "$w"
  fi
done

echo "== remaining"
for b in "${branches[@]}"; do git -C "$repo" branch --list "$b"; done
[ -e "$repo/tmp/port" ] && echo "tmp/port"
exit $failed
