# 001: the history was filtered before the first public release

Date: 2026-09-02
Status: accepted

## Context

JauntyQ was developed from March 2026 in a repository that also held the paid
tooling, internal working notes, audit records and review reports. The core was
made public with release 0.5.0 in September 2026.

## Decision

The public repository carries the core's real history, from the first commit
on 2026-03-11, with the authorship and dates it was written with. Before the
first push the history was filtered so that it contains only the paths that
are public today. Commit messages were edited where they named something that
did not come along, and a few subjects were rewritten for the same reason.

## Consequences

- Some files' histories begin at their first public revision rather than at
  their first revision. A `git log --follow` that ends earlier than expected
  is this, not a lost file.
- A handful of commits reference issue numbers, review rounds or reports that
  are not in this repository. The code they describe is.
- The free command-line tool, the build files at the repository root and the
  documentation were added in the commits that prepared the public release
  rather than filtered from the older history.
- Tags start at `v0.5.0`. Earlier versions were released from the original
  repository and are not re-tagged here.
