#!/usr/bin/env bash
# Builds the docs site using the shared Docs tool (sibling repo: ../docs).
#
# Output: dist/docs-site, which is COMMITTED, not gitignored. .gitignore has
# /dist/* followed by !/dist/docs-site/, so the site is tracked and the rest of
# dist/ is not. Regenerating without committing the result leaves a published
# site that disagrees with the markdown it was built from.
set -euo pipefail
cd "$(dirname "$0")/.."
tool="../docs/src/Docs"
if [ ! -d "$tool" ]; then
  echo "build-docs.sh: the shared Docs tool is not part of this repository; expected a sibling checkout at $tool" >&2
  exit 1
fi
dotnet run --project "$tool" -- docs dist/docs-site --site-name jauntyq
echo "jauntyq.extrode.com" > dist/docs-site/CNAME
