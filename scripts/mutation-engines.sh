#!/usr/bin/env bash
# Starts one long-lived server per engine for a Stryker run over
# tests/Extrode.JauntyQ.Schema.Extraction.Tests, and prints the
# JAUNTYQ_TEST_ENGINE_* variables that point EngineContainers at them.
#
#   eval "$(./scripts/mutation-engines.sh --quiet)"
#   (cd tests/Extrode.JauntyQ.Schema.Extraction.Tests && dotnet-stryker --concurrency 4)
#   ./scripts/mutation-engines.sh --stop
#
# WHY THIS EXISTS
#
# Stryker runs the tests once per mutant, and without these variables every
# run starts its own engine containers and removes them at the end. Measured
# 2026-09-24: mid-run on mb1 no engine container was older than 22 s, and
# locally a container lived 4-19 s per engine against under 1 s of tests. With
# the variables set, the Postgres mutation-coverage class went from 21 s to 8 s
# wall and started no container.
#
# Fixtures still get a fresh database each, named with a per-process token, and
# drop it when the run ends, so isolation is the same as with containers. A run
# Stryker kills on timeout leaves its databases behind; --stop takes them with
# the containers.
#
# The credentials are fixed and weak on purpose: throwaway containers on
# loopback ports, nothing worth protecting. Image pins match EngineContainers.

set -euo pipefail

# Git Bash on Windows rewrites /opt/... in docker exec arguments into a Windows
# path, so the sqlcmd readiness check never finds the tool. No effect elsewhere.
export MSYS_NO_PATHCONV=1

PG_PORT="${JAUNTYQ_MUT_PG_PORT:-56432}"
MY_PORT="${JAUNTYQ_MUT_MYSQL_PORT:-56306}"
MA_PORT="${JAUNTYQ_MUT_MARIADB_PORT:-56307}"
MS_PORT="${JAUNTYQ_MUT_MSSQL_PORT:-51433}"
PASSWORD='JauntyQ-mut1'
NAMES=(jauntyq-mut-pg jauntyq-mut-mysql jauntyq-mut-mariadb jauntyq-mut-mssql)

QUIET=0
case "${1:-}" in
  --quiet) QUIET=1 ;;
  --stop)
    docker rm -f "${NAMES[@]}" >/dev/null 2>&1 || true
    echo "Removed ${NAMES[*]}" >&2
    exit 0 ;;
  "") ;;
  *) echo "Usage: $0 [--quiet|--stop]" >&2; exit 2 ;;
esac

say() { [[ $QUIET -eq 1 ]] || echo "$@" >&2; }

ensure() {
  local name="$1"; shift
  if docker inspect "$name" >/dev/null 2>&1; then
    docker start "$name" >/dev/null
    say "  $name: already exists, started"
  else
    docker run -d --name "$name" "$@" >/dev/null
    say "  $name: created"
  fi
}

say "Starting mutation engines:"
ensure jauntyq-mut-pg -e POSTGRES_PASSWORD="$PASSWORD" -p "${PG_PORT}:5432" postgres:16-alpine
ensure jauntyq-mut-mysql -e MYSQL_ROOT_PASSWORD="$PASSWORD" -p "${MY_PORT}:3306" mysql:8.0
ensure jauntyq-mut-mariadb -e MARIADB_ROOT_PASSWORD="$PASSWORD" -p "${MA_PORT}:3306" mariadb:11
ensure jauntyq-mut-mssql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$PASSWORD" -p "${MS_PORT}:1433" \
  mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04

mssql_ready() {
  local tool
  for tool in /opt/mssql-tools18/bin/sqlcmd /opt/mssql-tools/bin/sqlcmd; do
    docker exec jauntyq-mut-mssql "$tool" -C -S localhost -U sa -P "$PASSWORD" -Q 'SELECT 1' >/dev/null 2>&1 && return 0
  done
  return 1
}

# The engines accept TCP well before they accept queries, so wait on a query.
say "Waiting for all four to accept queries:"
for _ in $(seq 1 90); do
  up=0
  docker exec jauntyq-mut-pg pg_isready -U postgres >/dev/null 2>&1 && up=$((up + 1))
  docker exec jauntyq-mut-mysql mysqladmin ping -uroot -p"$PASSWORD" --silent >/dev/null 2>&1 && up=$((up + 1))
  docker exec jauntyq-mut-mariadb mariadb-admin ping -uroot -p"$PASSWORD" --silent >/dev/null 2>&1 && up=$((up + 1))
  mssql_ready && up=$((up + 1))
  [[ $up -eq 4 ]] && break
  sleep 2
done

if [[ $up -ne 4 ]]; then
  echo "Timed out after 180s with $up of 4 engines ready. Not printing exports." >&2
  exit 1
fi
say "  all ready"

echo "export JAUNTYQ_TEST_ENGINE_POSTGRES='Host=localhost;Port=${PG_PORT};Database=postgres;Username=postgres;Password=${PASSWORD}'"
echo "export JAUNTYQ_TEST_ENGINE_MYSQL='Server=localhost;Port=${MY_PORT};Database=mysql;Uid=root;Pwd=${PASSWORD}'"
echo "export JAUNTYQ_TEST_ENGINE_MARIADB='Server=localhost;Port=${MA_PORT};Database=mysql;Uid=root;Pwd=${PASSWORD}'"
echo "export JAUNTYQ_TEST_ENGINE_SQLSERVER='Server=localhost,${MS_PORT};Database=master;User Id=sa;Password=${PASSWORD};TrustServerCertificate=True'"
