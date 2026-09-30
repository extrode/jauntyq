#!/usr/bin/env bash
# Opt-in mutation testing on top of a normal test run. Off by default -- a
# whole-assembly Stryker run takes minutes, so it's never implied by a plain
# test run, only requested explicitly with --mutate.
#
# Usage:
#   scripts/mutate.sh <analysis|sqlparser>                    # unit tests only
#   scripts/mutate.sh <analysis|sqlparser> --mutate            # + whole-assembly mutation run
#   scripts/mutate.sh <analysis|sqlparser> --mutate --file X.cs  # + mutation run scoped to one file
#
# The unit tests always run first and must pass before Stryker starts --
# mutation testing a red suite just reports every mutant as "survived"
# because nothing was verifying behavior to begin with.
#
# File-scoped runs (--file) are the fast path (seconds-to-low-minutes) meant
# for the write-a-test-then-check-it-lands loop. Whole-assembly runs
# (--mutate with no --file) can take several minutes -- background it
# (see the delegate skill / macbook offload) rather than blocking on it here.
set -euo pipefail

cd "$(dirname "$0")/.."

usage() {
  echo "Usage: $0 <analysis|sqlparser> [--mutate] [--file <pattern>]" >&2
  exit 2
}

[[ $# -ge 1 ]] || usage
TARGET="$1"; shift

case "$TARGET" in
  analysis)  TEST_DIR="tests/Extrode.JauntyQ.Analysis.Tests" ;;
  sqlparser) TEST_DIR="tests/Extrode.JauntyQ.SqlParser.Tests" ;;
  *) echo "unknown target: $TARGET (expected 'analysis' or 'sqlparser')" >&2; usage ;;
esac

MUTATE=0
FILE_PATTERN=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mutate) MUTATE=1; shift ;;
    --file) FILE_PATTERN="${2:-}"; [[ -n "$FILE_PATTERN" ]] || usage; shift 2 ;;
    *) echo "unknown arg: $1" >&2; usage ;;
  esac
done

echo "==> dotnet test $TEST_DIR"
dotnet test "$TEST_DIR"

if [[ "$MUTATE" == 0 ]]; then
  exit 0
fi

echo
if [[ -n "$FILE_PATTERN" ]]; then
  echo "==> dotnet stryker --mutate \"**/$FILE_PATTERN\" (scoped)"
  (cd "$TEST_DIR" && dotnet stryker --mutate "**/$FILE_PATTERN" --reporter progress --reporter json)
else
  echo "==> dotnet stryker (whole assembly -- this can take several minutes)"
  (cd "$TEST_DIR" && dotnet stryker --reporter progress --reporter json)
fi
