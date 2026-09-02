#!/usr/bin/env bash
#
# Builds a consumer against the PACKED JauntyQ, not against ProjectReferences.
#
# Why this exists. Every other project in this repo -- all 27 under samples/ --
# references the generator with <ProjectReference OutputItemType="Analyzer">. No
# build here has ever exercised the packed artifact: the nuspec, the
# analyzers/dotnet/cs layout, the bundled System.Text.Json closure, or the
# build/JauntyQ.Generator.props registration that supplies JauntyQAutoCrud and
# JauntyQDialect to an external consumer. So a green JauntyQ build says nothing
# about whether consumers compile, which is what a consumer reported on
# 2026-08-30.
#
# It also closes the "fail loudly" half of that report's incremental-drop ask. A
# generator that fails to load or throws emits no source and Roslyn reports only
# a WARNING; the smoke project sets TreatWarningsAsErrors, so that becomes a
# failed job here instead of forty spurious CS0246s in someone else's repo.
#
# Everything it writes lives under dist/, which is gitignored, and it restores
# into its own packages folder rather than the machine's global one -- so a
# re-pack of the same version is actually picked up, and nothing outside dist/
# is touched.
#
# Usage: scripts/packaged-consumer-smoke.sh
# Exit 0 only when the packed generator produced code that compiled AND ran.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SMOKE="$ROOT/samples/JauntyQ.Packaged.Smoke"
FEED="$ROOT/dist/packaged-smoke-feed"
PKGS="$ROOT/dist/packaged-smoke-packages"

# Both paths are created by this script and by nothing else. Recreating them is
# the point: a stale .nupkg in the feed, or a stale extraction in the packages
# folder, would let the smoke pass against a build that is no longer the one on
# disk -- the very skew it exists to detect.
rm -rf "$FEED" "$PKGS"
mkdir -p "$FEED"

echo "[1/4] pack"
dotnet pack "$ROOT/JauntyQ.slnx" -c Release -o "$FEED" -v q --nologo

ls "$FEED"/JauntyQ.Generator.*.nupkg >/dev/null 2>&1 || {
  echo "ERROR: pack produced no JauntyQ.Generator .nupkg; there is nothing to smoke-test." >&2
  exit 1
}
echo "       packed: $(cd "$FEED" && ls *.nupkg | tr '\n' ' ')"

echo "[2/4] restore (feed-only, own packages folder)"
dotnet restore "$SMOKE/JauntyQ.Packaged.Smoke.csproj" --packages "$PKGS" -v q --nologo

echo "[3/4] build (warnings are errors, so a silent generator fails here)"
dotnet build "$SMOKE/JauntyQ.Packaged.Smoke.csproj" -c Release --no-restore -v q --nologo

echo "[4/4] run"
dotnet run --project "$SMOKE/JauntyQ.Packaged.Smoke.csproj" -c Release --no-build

echo "packaged-consumer-smoke: OK"
