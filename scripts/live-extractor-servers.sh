#!/usr/bin/env bash
# Provisions the throwaway servers that tests/Extrode.JauntyQ.Schema.Extraction.Tests' live-extractor tests
# need, and prints the environment variables that point at them.
#
#   ./scripts/live-extractor-servers.sh          # start (idempotent) and print exports
#   eval "$(./scripts/live-extractor-servers.sh --quiet)" && dotnet test tests/Extrode.JauntyQ.Schema.Extraction.Tests
#
# WHY THIS EXISTS
#
# Measured 2026-08-29: 29 of the 209 tests in tests/Extrode.JauntyQ.Schema.Extraction.Tests had never run.
# They are gated on JAUNTYQ_TEST_{POSTGRES,MYSQL,SQLSERVER}_CONNECTION, nothing
# in the repository ever sets one, and the skip is soft — so PostgresExtractor
# and MySqlExtractor were covered against a live server by exactly nothing on
# any machine. Unlike the sample suites, tests/Extrode.JauntyQ.Schema.Extraction.Tests does not reference
# Testcontainers (deliberately: it connects to an already-running server), which
# is why the servers have to come from somewhere else. This is that somewhere.
#
# With Postgres and MySQL up, 25 of the 29 run. Result 2026-08-29:
# 209 passed, 0 skipped, 0 failed — the suite's first zero-skip run.
#
# The fixtures create their own uniquely-named throwaway tables and, per repo
# convention, never DROP them. These containers are the disposal mechanism:
# delete the container and the leftovers go with it.
#
# SQL SERVER IS NOT COVERED HERE. Its four tests want the Northwind sample
# schema, not a throwaway table, and applying schema.sqlserver.sql through
# sqlcmd mis-splits its GO batches — samples/Extrode.JauntyQ.Northwind.Tests applies the
# same file through SqlClient and succeeds. Point
# JAUNTYQ_TEST_SQLSERVER_CONNECTION at a Northwind provisioned that way if you
# want those four; without it they skip, which is a sanctioned skip.
#
# The credentials below are deliberately fixed and deliberately weak: these are
# throwaway containers on a loopback port with no data worth protecting, and a
# generated password would have to be stored somewhere to be useful. Override
# any of the four via the environment if a port is already taken.

set -euo pipefail

PG_PORT="${JAUNTYQ_PROBE_PG_PORT:-55432}"
MY_PORT="${JAUNTYQ_PROBE_MYSQL_PORT:-53306}"
PG_NAME="${JAUNTYQ_PROBE_PG_NAME:-jauntyq-live-pg}"
MY_NAME="${JAUNTYQ_PROBE_MYSQL_NAME:-jauntyq-live-mysql}"

QUIET=0
[[ "${1:-}" == "--quiet" ]] && QUIET=1

say() { [[ $QUIET -eq 1 ]] || echo "$@" >&2; }

# `docker start` on a stopped container, `docker run` on a missing one: running
# this twice must not fail, because the natural use is to run it before every
# session without remembering what happened last time.
ensure() {
  local name="$1"; shift
  if docker inspect "$name" >/dev/null 2>&1; then
    docker start "$name" >/dev/null
    say "  $name — already exists, started"
  else
    docker run -d --name "$name" "$@" >/dev/null
    say "  $name — created"
  fi
}

say "Starting live-extractor servers:"

ensure "$PG_NAME" \
  -e POSTGRES_PASSWORD=probe -e POSTGRES_DB=jauntyq_live \
  -p "${PG_PORT}:5432" postgres:16

ensure "$MY_NAME" \
  -e MYSQL_ROOT_PASSWORD=probe -e MYSQL_DATABASE=jauntyq_live \
  -p "${MY_PORT}:3306" mysql:8.0

# Both images accept TCP connections well before they accept queries, so the
# wait is on a real command rather than on the port. Without it the fixtures
# see a refused connection — which, since 2026-08-29, FAILS the run rather than
# skipping it, so an impatient script here reads as a broken extractor.
say "Waiting for both to accept queries:"
for _ in $(seq 1 60); do
  pg_up=0; my_up=0
  docker exec "$PG_NAME" pg_isready -U postgres >/dev/null 2>&1 && pg_up=1
  docker exec "$MY_NAME" mysqladmin ping -uroot -pprobe >/dev/null 2>&1 && my_up=1
  [[ $pg_up -eq 1 && $my_up -eq 1 ]] && break
  sleep 2
done

if [[ ${pg_up:-0} -ne 1 || ${my_up:-0} -ne 1 ]]; then
  echo "Timed out after 120s (postgres=${pg_up:-0} mysql=${my_up:-0})." >&2
  echo "Not printing exports: setting them against a server that is down now fails the run." >&2
  exit 1
fi
say "  both ready"
say ""

echo "export JAUNTYQ_TEST_POSTGRES_CONNECTION='Host=localhost;Port=${PG_PORT};Database=jauntyq_live;Username=postgres;Password=probe'"
echo "export JAUNTYQ_TEST_MYSQL_CONNECTION='Server=localhost;Port=${MY_PORT};Database=jauntyq_live;Uid=root;Pwd=probe;AllowUserVariables=true'"
