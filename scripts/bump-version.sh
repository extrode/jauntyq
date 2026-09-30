#!/usr/bin/env bash
# Bumps the package version across the repo for a release.
#
# Usage: scripts/bump-version.sh <from_version> <to_version>
# Example: scripts/bump-version.sh 0.5.0 0.5.1
#
# Updates:
#   - Directory.Build.props: the <Version> element (the single source of
#     truth release.yml's tag guard reads).
#   - README.md and every docs/**/*.md: PackageReference lines of the form
#     Version="<from_version>" in install-example XML, since those are the
#     only "current version" claims outside Directory.Build.props.
#
# Deliberately NOT touched:
#   - CHANGELOG.md. Version bumps are narrative (what changed, dated
#     headings) and get written by hand alongside this script, not
#     find-and-replaced.
#   - Any prose mentioning a version as a historical fact (e.g. "public
#     since 0.5.0", "tags start at v0.5.0") -- those describe the past and
#     must not track the current version. The Version="..." XML-attribute
#     pattern this script matches does not appear in that kind of prose, so
#     it is naturally excluded; do not broaden the pattern to a bare version
#     string search.
#
# Run it, review the diff, commit by hand -- this script does not commit.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ $# -ne 2 ]; then
  echo "usage: scripts/bump-version.sh <from_version> <to_version>" >&2
  exit 1
fi

from=$1
to=$2

if [ "$from" = "$to" ]; then
  echo "bump-version.sh: from and to are both $from, nothing to do" >&2
  exit 1
fi

escaped_from=$(printf '%s' "$from" | sed 's/[.[\*^$/]/\\&/g')

changed=0

# Directory.Build.props: only the literal <Version>...</Version> element,
# not any other property that happens to contain a version-shaped string.
if grep -q "<Version>$escaped_from</Version>" Directory.Build.props; then
  sed -i "s/<Version>$escaped_from<\/Version>/<Version>$to<\/Version>/" Directory.Build.props
  echo "updated: Directory.Build.props"
  changed=$((changed + 1))
else
  echo "skip: Directory.Build.props does not contain <Version>$from</Version>"
fi

# Install-example PackageReference lines, wherever they live under docs/ or
# at the repo root. Matches only the Version="..." XML attribute, so
# CHANGELOG.md's "## [$from]" headings and historical prose never match.
while IFS= read -r file; do
  sed -i "s/Version=\"$escaped_from\"/Version=\"$to\"/g" "$file"
  echo "updated: $file"
  changed=$((changed + 1))
done < <(grep -rl "Version=\"$from\"" README.md docs 2>/dev/null || true)

if [ "$changed" -eq 0 ]; then
  echo "bump-version.sh: found nothing to update for $from -> $to" >&2
  exit 1
fi

echo "Done. Review with 'git diff', then commit by hand."
