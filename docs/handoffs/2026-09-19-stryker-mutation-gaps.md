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

## Update 2026-09-19: Analysis's gap closed

Item 1 below is resolved. JauntyQ's own session picked this up same-day,
branch `test/analysis-mutation-coverage`, merged `--no-ff` into `dev`
(unpushed). Chose "write real tests" over jaunty's mutate-glob narrowing
(see item 3) — the score below is honest, whole-project, no scope games.

**Extrode.JauntyQ.Analysis: 32.52% -> 81.51%**, now clearing its own
configured thresholds (80 high / 65 low) for the first time. Three moves,
verified after each with a local Stryker rerun (~3-4 min each, single-project
scope — full CI-scale runs were NOT repeated locally, see the "no long local
runs" constraint):

1. **Exhaustive reserved-word coverage for `DialectReservedWords.cs`**
   (32.52% -> 55.97%): the existing test suite spot-checked ~40 words per
   dialect; added `[Theory]`/`[InlineData]` asserting every word in each of
   the four dialects' shipped lists (99/253/180/58 words) through the public
   `IsReservedInDialect` API, generated programmatically from the source
   HashSets so every string-literal mutation has an independent assertion
   that would catch it. Plus direct tests for `RequiresQuotingForCase`,
   which had zero tests of its own (only exercised incidentally through
   `AutoCrud`). 556/558 survived mutants killed.
2. **Moved `MigrationParserTests.cs` from `Extrode.JauntyQ.Generator.Tests`
   to `Extrode.JauntyQ.Analysis.Tests`** (55.97% -> 70.88%): this was the
   real finding. 71 test methods (1439 lines) thoroughly covering
   `MigrationParser` existed all along, just in the wrong project — they had
   no dependency on `Extrode.JauntyQ.Generator` at all. Since the nightly
   mutation job only measures `Extrode.JauntyQ.Analysis.Tests` against
   `Extrode.JauntyQ.Analysis`, this entire suite was invisible to Stryker.
   Verified both projects still build and pass after the move.
3. **Same pattern for `DialectMapperTests.cs`** (70.88% -> 81.51%): 27 test
   methods, same misplacement, one truly-unused `using
   Extrode.JauntyQ.Generator;` removed in the move. Checked every other
   Generator.Tests file that references Analysis types
   (`AutoCrudTests.cs`, `UnmappedColumnTypeTests.cs`, etc.) — those
   genuinely use `CodeEmitter`/`JauntyQGenerator` and are correctly placed;
   no further misplaced-test blind spots found.

Verified via a full `dotnet build -c Release` + `dotnet test` solution-wide
run before merging: one unrelated failure (`Extrode.JauntyQ.Sakila.MySql.Tests`
net8.0 run, Testcontainers "Failed to start mysqld daemon" under concurrent
Docker load) confirmed transient by rerunning that project alone in isolation
(22/22 passed) — not caused by this change, nothing else touched MySQL/Docker.

**Remaining in `Extrode.JauntyQ.Analysis` (not attempted — genuine parser
logic, not mechanical enumeration):** `MigrationParser.cs` (163 survived + 8
NoCoverage), `AutoCrud.cs` (80 + 29), `Impact/ImpactClassifier.cs` (16 + 23),
`Migrations/SchemaSimulator.cs` (27 + 5), `DialectMapper.cs` (21 + 11),
`Impact/ReferencedObjects.cs` (16 + 7), `UpsertKeyResolver.cs` (11 + 7). Each
fix here needs a crafted DDL/schema fixture per surviving mutant, not a
generated enumeration — comparable effort to item 1 above, done carefully,
per file. Worth checking first whether any of these have a similarly
misplaced test suite elsewhere in `tests/` before writing anything new.

## Open items for JauntyQ's own session to pick up

Not started, not scoped in detail:

2. **6 of 8 src projects have no mutation testing at all**, including
   Generator (13,107 LOC — the single largest project in the repo) and
   Schema.Extraction (2,699 LOC). Adding configs for these is straightforward
   (mirror the existing two configs) but scope/cost of a first run is unknown
   — Generator in particular could be a long run given its size.
3. **Decide whether to adopt jaunty's `mutate`-glob narrowing pattern here**,
   or keep whole-project scope.** Not adopted this pass (see above) — whole-
   project scope was kept deliberately, since narrowing only reports on the
   narrowed subfolder and jaunty's own recent work found "covered elsewhere"
   mutants that were falsely reported as uncovered purely because of scope,
   not because tests were missing.
4. **No mutation-score tracking over time** — if this becomes an ongoing
   quality signal, worth a place to record scores per run (a doc, or reading
   them back from CI artifacts) rather than only ever seeing the latest number
   live in a workflow log.
5. **`Extrode.JauntyQ.SqlParser` still sits at 71.00%**, below its own 80/65
   thresholds, untouched this pass (scope was Analysis only) — same kind of
   triage (survived/nocoverage breakdown, check for misplaced tests
   elsewhere in `tests/` first) likely applies.
