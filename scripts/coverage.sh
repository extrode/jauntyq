#!/usr/bin/env bash
#
# Collects code coverage across the offline test suites (core + SQLite samples),
# merges the per-project cobertura reports, and prints a per-assembly summary.
#
# By default the Testcontainers matrix (Postgres/MySQL/MariaDB/SQL Server) is
# excluded — it needs Docker. Pass --with-docker to fold it in: the core live
# tests (LiveContractTests, LiveExplainTests, SequenceExtractorTests — SQL Server
# + Postgres) soft-skip without Docker and start running, and the per-engine
# sample suites (self-provisioning via Testcontainers) are appended so the
# MySQL/MariaDB provider arms are covered too. First run pulls the DB images.
#
# Requirements: `reportgenerator` global tool
#   dotnet tool install --global dotnet-reportgenerator-globaltool
#
# Usage: scripts/coverage.sh [--html] [--with-docker]
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${COVERAGE_OUT:-$REPO/artifacts/coverage}"
RUNSETTINGS="$REPO/coverage.runsettings"

HTML=0; WITH_DOCKER=0
for arg in "$@"; do
  case "$arg" in
    --html) HTML=1 ;;
    --with-docker) WITH_DOCKER=1 ;;
    *) echo "unknown arg: $arg" >&2; exit 2 ;;
  esac
done

rm -rf "$OUT"; mkdir -p "$OUT"

SUITES=(
  tests/JauntyQ.Analysis.Tests
  tests/JauntyQ.Cli.Tests
  [REDACTED]
  [REDACTED]
  tests/JauntyQ.Generator.Tests
  [REDACTED]
  [REDACTED]
  [REDACTED]
  tests/JauntyQ.SqlParser.Tests
  tests/JauntyQ.Schema.Extraction.Tests
  samples/JauntyQ.Sqlite.Tests
  samples/JauntyQ.Northwind.Tests
  samples/JauntyQ.Sakila.Sqlite.Tests
  samples/JauntyQ.EShopOnWeb.Sqlite.Tests
  samples/JauntyQ.DdlSchema.Sqlite.Tests
)

# Self-provisioning Testcontainers suites (Docker required). The core suites in
# SUITES already contain Docker-gated live tests that activate when Docker is up;
# these add the MySQL/MariaDB engine arms and the consumer runtime paths.
DOCKER_SUITES=(
  samples/JauntyQ.Postgres.Tests
  samples/JauntyQ.MySql.Tests
  samples/JauntyQ.Sakila.Postgres.Tests
  samples/JauntyQ.Sakila.MySql.Tests
  samples/JauntyQ.Sakila.MariaDb.Tests
  samples/JauntyQ.Sakila.SqlServer.Tests
  samples/JauntyQ.EShopOnWeb.Postgres.Tests
  samples/JauntyQ.EShopOnWeb.MySql.Tests
  samples/JauntyQ.EShopOnWeb.MariaDb.Tests
  samples/JauntyQ.EShopOnWeb.SqlServer.Tests
  samples/JauntyQ.Conduit.Postgres.Tests
  samples/JauntyQ.Conduit.MySql.Tests
  samples/JauntyQ.Conduit.MariaDb.Tests
  samples/JauntyQ.Conduit.SqlServer.Tests
  samples/JauntyQ.AdventureWorksLite.SqlServer.Tests
)

if [[ "$WITH_DOCKER" == 1 ]]; then
  if ! docker info >/dev/null 2>&1; then
    echo "ERROR: --with-docker given but the Docker daemon is not reachable." >&2
    exit 1
  fi
  SUITES+=("${DOCKER_SUITES[@]}")
fi

run_suite() {  # $1 = suite path
  dotnet test "$REPO/$1" --collect:"XPlat Code Coverage" --settings "$RUNSETTINGS" \
    --results-directory "$OUT/$(basename "$1")" --nologo -v q
}

FAILED=()
for t in "${SUITES[@]}"; do
  echo "==> $t"
  # Don't let one failing/flaky suite abort the whole run. Testcontainers suites
  # can flake under parallel load (a DB container that loses the CPU race to an
  # emulated SQL Server, say), so retry a failure once before recording it; the
  # report is still merged from everything collected.
  if ! run_suite "$t"; then
    echo "   retrying $t once..."
    rm -rf "$OUT/$(basename "$t")"
    run_suite "$t" || { echo "SUITE FAILED: $t"; FAILED+=("$t"); }
  fi
done

REPORTTYPES="TextSummary"
[[ "$HTML" == 1 ]] && REPORTTYPES="TextSummary;Html"

reportgenerator \
  -reports:"$OUT/**/coverage.cobertura.xml" \
  -targetdir:"$OUT/report" \
  -reporttypes:"$REPORTTYPES"

echo
cat "$OUT/report/Summary.txt"

if [[ ${#FAILED[@]} -gt 0 ]]; then
  echo
  echo "ERROR: ${#FAILED[@]} suite(s) failed BOTH attempts (coverage still merged above):"
  printf '  - %s\n' "${FAILED[@]}"
  # Fail the script so callers/CI don't read a genuine test failure as success.
  # The retry-once above already absorbs flakes; reaching here means a suite
  # failed twice, which is a real failure, not noise.
  exit 1
fi
