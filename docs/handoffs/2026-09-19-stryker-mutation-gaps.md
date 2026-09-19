# Stryker mutation testing: current state and gaps

Handoff from a cross-repo probe run out of the `jaunty` session (jaunty had just
closed its own Stryker NoCoverage gaps and the user asked to check whether
JauntyQ was set up the same way). No code changed in this repo — investigation
only, findings below, JauntyQ's own session picks it up from here.

## What exists today

Two of JauntyQ's 8 `src/` projects have Stryker configured and running nightly:

| Project | LOC | `stryker-config.json` | In CI matrix (`nightly.yml` `mutation` job) |
|---|---|---|---|
| `Extrode.JauntyQ.SqlParser` | 4,016 | yes | yes |
| `Extrode.JauntyQ.Analysis` | 4,340 | yes | yes |
| `Extrode.JauntyQ.Generator` | 13,107 | no | no |
| `Extrode.JauntyQ.Schema.Extraction` | 2,699 | no | no |
| `Extrode.JauntyQ.Schema` | 1,111 | no | no |
| `Extrode.JauntyQ.Cli.Core` | 766 | no | no |
| `Extrode.JauntyQ.Runtime` | 234 | no | no |
| `Extrode.JauntyQ.Cli` | 154 | no | no |

Both configs (`tests/Extrode.JauntyQ.SqlParser.Tests/stryker-config.json`,
`tests/Extrode.JauntyQ.Analysis.Tests/stryker-config.json`) mutate the **whole
project** — no `mutate` glob narrowing, unlike jaunty's configs (see Comparison
below). Thresholds `high: 80, low: 65, break: 0` in both, but `break: 0` plus
the workflow's own comment ("Scores are the artifact, not a gate: no threshold
fails the job") means neither threshold can fail the run.

`.github/workflows/nightly.yml` job `mutation` (matrix over the two
`*.Tests` projects, `fail-fast: false`) installs `dotnet-stryker` fresh and
runs `dotnet-stryker` from each test project's directory, uploading
`StrykerOutput/` as an artifact regardless of outcome. Runs on `dev` and
`main` schedules; no PR-time run.

## Actual scores (run 35322907143, 2026-09-18, branch `main`, 56m7s, success)

| Project | Mutants created | Skipped (CompileError / NoCoverage / Ignored) | Tested | Killed | Survived | Timeout | Score |
|---|---|---|---|---|---|---|---|
| SqlParser | 3,475 | 643 (320 / 61 / 262) | 2,832 | 1,898 | 778 | 156 | **71.00%** |
| Analysis | 2,757 | 955 (129 / 578 / 248) | 1,802 | 758 | 1,028 | 16 | **32.52%** |

Both below their own configured thresholds (80 high / 65 low), non-blocking so
CI stays green. **Analysis is the real problem**: 1,028 Survived out of 1,802
tested (57%) plus 578 NoCoverage (21% of all mutants created) — most mutations
in that project aren't caught by anything. SqlParser at 71% is closer but still
short of its own 80/65 bar.

No mutation-score history is tracked anywhere in `docs/` or `work/` in this
repo — this run's numbers aren't recorded anywhere but this file and the raw
CI artifact (which is not retained long-term). Full raw log saved locally at
`tmp/nightly-run-35322907143.log` (gitignored, not committed).

## Comparison: how jaunty does it differently

Jaunty (sibling repo, `C:\home\code\extrode.com\jaunty`) also runs Stryker on
2 of its 10 `src/` projects, at a 96.19%/96.22% baseline — but that number is
against a much narrower slice than it looks:

| Jaunty project | LOC | Stryker config | Actual `mutate` scope |
|---|---|---|---|
| `Extrode.Jaunty` | 48,471 | yes | only `**/Internals/Parameters/**`, `**/Dialects/**` |
| `Extrode.Jaunty.Fluent` | 21,792 | yes | only `**/Expressions/**` |
| (8 other projects) | — | no | — |

Jaunty's high score is a narrow-scope score (a few thousand lines out of
70k+), not a whole-project score. JauntyQ's two configured projects, by
contrast, have no `mutate` key at all — they mutate everything, which is why
the honest numbers (71%/32.5%) look worse: they're not being flattered by
scope-narrowing the way jaunty's are.

Both repos share: nightly-only cadence, `fail-fast: false`, non-gating score,
fresh `dotnet-stryker` install per run, JSON+progress reporters, artifact
upload regardless of outcome.

## Open items for JauntyQ's own session to pick up

Not started, not scoped in detail — for the next session working in this repo
to decide and size:

1. **Analysis project's 32.52% score is the priority.** 578 NoCoverage + 1,028
   Survived out of 2,757 total mutants is a large real gap, not a scope
   artifact like jaunty's numbers were. Worth a NoCoverage/Survived breakdown
   pass (same shape as the jaunty session just did) before writing any tests,
   to separate "genuinely untested code" from "mutants that don't matter."
2. **6 of 8 src projects have no mutation testing at all**, including
   Generator (13,107 LOC — the single largest project in the repo) and
   Schema.Extraction (2,699 LOC). Adding configs for these is straightforward
   (mirror the existing two configs) but scope/cost of a first run is unknown
   — Generator in particular could be a long run given its size.
3. **Decide whether to adopt jaunty's `mutate`-glob narrowing pattern here**,
   or keep whole-project scope. Narrowing makes runs faster and scores higher
   but only reports on the narrowed subfolder — jaunty's own recent work
   found "covered elsewhere" mutants that were falsely reported as
   uncovered purely because of scope, not because tests were missing.
4. **No mutation-score tracking over time** — if this becomes an ongoing
   quality signal, worth a place to record scores per run (a doc, or reading
   them back from CI artifacts) rather than only ever seeing the latest number
   live in a workflow log.

No fixes attempted, no branch created, no files touched in this repo as part
of this handoff.
