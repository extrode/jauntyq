#!/usr/bin/env bash
# Full-solution test run with bounded Docker concurrency. Added 2026-07-30 so
# that a green full-solution run means everything ran.
#
# Why this exists rather than a bare `dotnet test JauntyQ.slnx`:
#
# MSBuild runs the test projects in parallel, defaulting to the CPU count. On a
# 16-CPU / 32 GB host that put a measured peak of 34 Testcontainers instances on
# the Docker daemon at once, many of them SQL Server at ~2 GB. Past that point
# container bring-up starts losing races, and a lost race used to be reported as
# a "Docker unavailable" SKIP -- so the run went green without having run. Two
# runs on the same commit on 2026-07-30 skipped 66 and 11 of the same 2960 tests,
# both calling themselves successful.
#
# Two caps are in play. Intra-assembly parallelism is bounded per project by an
# xunit.runner.json (tests/Extrode.JauntyQ.Schema.Extraction.Tests caps collections at 4; Conduit.SqlServer
# serializes entirely). This script supplies the other half: how many test
# assemblies run at once.
#
#   ./scripts/test-all.sh              # bounded run, the default
#   ./scripts/test-all.sh 8            # override the assembly-parallelism cap
#   JOBS=2 ./scripts/test-all.sh       # same, via the environment
#
# Anything after the cap is forwarded to dotnet test:
#
#   ./scripts/test-all.sh 4 --filter "FullyQualifiedName~Northwind"
#
# The run is always logged to trx and always audited afterward by
# scripts/skip-audit.js, which fails it when tests were skipped for a reason the
# repo has not sanctioned. Both halves are needed and neither is optional:
#
#   * Without trx, a skip records no reason ANYWHERE. Measured 2026-08-17: a
#     bare run skipped 101 tests across three assemblies, exited 0, and left
#     nothing behind to say why.
#   * Without the audit, `dotnet test` reports that same run as a success.
#
set -euo pipefail

JOBS="${JOBS:-4}"
if [[ "${1:-}" =~ ^[0-9]+$ ]]; then
  JOBS="$1"
  shift
fi

cd "$(dirname "$0")/.."

# Probed BEFORE the run, because it decides how a "Docker unavailable" skip is
# read afterward: with no daemon it is the correct outcome, with a daemon up it
# means FixtureGate misclassified a real bring-up failure.
DOCKER_REACHABLE=true
if ! docker info >/dev/null 2>&1; then
  DOCKER_REACHABLE=false
  echo "warning: Docker is not reachable; the container-backed suites will skip." >&2
fi

# A timestamped subdirectory per run. Results are never deleted here -- the
# previous run's trx is the only evidence left when an intermittent failure
# happens to be caught, and artifacts/ is gitignored.
RESULTS="artifacts/test-results/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RESULTS"

echo "Running JauntyQ.slnx with at most $JOBS test assemblies in parallel."
echo "Results: $RESULTS"

TEST_STATUS=0
dotnet test JauntyQ.slnx -c Release "-m:$JOBS" \
  --logger trx --results-directory "$RESULTS" "$@" || TEST_STATUS=$?

echo ""
AUDIT_STATUS=0
deno run -A scripts/skip-audit.js \
  --results "$RESULTS" --docker-reachable "$DOCKER_REACHABLE" || AUDIT_STATUS=$?

# A failing test outranks an unexplained skip in what it tells you, so report it
# first -- but either one fails the run.
if [[ $TEST_STATUS -ne 0 ]]; then
  exit $TEST_STATUS
fi
exit $AUDIT_STATUS
