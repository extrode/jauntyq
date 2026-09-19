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

## Update 2026-09-19 (later same day): the 7-file targeted pass, 81.51% -> 92.98%

Same-day follow-on session, branch `test/analysis-mutation-coverage-96`,
merged `--no-ff` into `dev` (unpushed). Worked the 7 files listed above in
largest-gap-first order, test-only changes throughout (no production source
touched), each file verified with a scoped `--mutate "**/<File>.cs"` Stryker
run before commit.

**Whole-project score: 81.51% -> 92.98%** (Killed 2179, Timeout 34, Survived
154, NoCoverage 13, out of 2380 tested; full unscoped run, 2026-09-19
20:17-20:21). **Short of the 96% target** — see "Why 96% wasn't reached"
below.

Per-file scores (scoped runs, `Extrode.JauntyQ.Analysis.Tests` only):

| File | Before | After | Notes |
|---|---|---|---|
| `AutoCrud.cs` | — | 97.9% (140 killed / 2 survived / 1 NoCoverage) | |
| `Impact/ImpactClassifier.cs` | — | 100% (66/0/0) | |
| `Migrations/SchemaSimulator.cs` | — | 94.2% (194/12/0) | |
| `DialectMapper.cs` | — | 98.7% (308/4/0) | |
| `Impact/ReferencedObjects.cs` | — | 86.5% (45/5/2) | |
| `UpsertKeyResolver.cs` | — | 96.2% (77/3/0) | |
| `Migrations/MigrationParser.cs` | 79.4% (114 killed / 148 survived+NoCov, from an earlier partial attempt) | 84.0% (641/114/8) | see below — the real blocker |

**MigrationParser.cs is the whole gap.** It alone carries 122 of the ~171
remaining Survived+NoCoverage mutants project-wide (the other 6 files are
each down to single digits, several at 100%). New file
`tests/Extrode.JauntyQ.Analysis.Tests/MigrationParserMutationCoverageTests.cs`
adds ~20 tests covering: the ALTER COLUMN SET/DROP-NOT-NULL and identity-
toggle guard chains under operator-precedence-changing `&&`->`||` mutations
(these needed an `isAlterColumn=false` MODIFY-form probe to distinguish —
naive same-shape SQL didn't move the needle because the guard's later terms
were already true for realistic input), ADD CONSTRAINT keyword literals
(UNIQUE/FOREIGN/CHECK), WITH-TIME-ZONE partial-word-match false positives,
MySQL UNSIGNED without ZEROFILL still reaching the trailing NOT NULL flag,
GENERATED BY (missing DEFAULT) AS IDENTITY falling through to the standalone
IDENTITY flag, the default-nullable field initializer, the computed-column-
shorthand Keyword-vs-"AS" guard, unterminated `IDENTITY(...)` paren behavior
(documents that it greedily swallows the rest of the definition, including a
trailing NOT NULL, when no closing paren exists), GroupAlterActions'
malformed-first-clause return, multi-level dotted table names, and two
unterminated-paren-list boundary checks (`SplitTopLevel`,
`ReadParenNameList`) that would otherwise throw `IndexOutOfRangeException`.

**Why 96% wasn't reached, and why most of the remaining ~114 survivors in
MigrationParser.cs are genuine equivalent mutants, not untested gaps:** the
flags-parsing loop in `ParseColumnDef` (lines ~617-730) has a single
`pos++; // unknown flag — skip token` fallback at its end that runs for any
token not matched by an earlier `if`. This makes almost every "does this
specific flag-check correctly consume N tokens" mutation unobservable:
whether a flag (PERSISTED, ZEROFILL when absent, STORED/VIRTUAL, BY/DEFAULT
when the peer term is already false) is skipped by its own dedicated branch
or falls through one token at a time to the generic skip, the end state
(IsNullable/IsIdentity/IsComputed/DbType) comes out identical either way —
only the position advances differently mid-loop, which nothing observes.
Roughly two dozen of the remaining survivors were traced by hand through
this exact mechanism and confirmed equivalent this way (documented inline
via the `Is()`/`IsSymbol()` bounds-safe-on-negative-index guards and the
`SqlTokenizer.Keywords` set, which is what makes e.g. `SET`/`BY`/`GENERATED`
tokenize predictably). A handful of others (`Is(tokens,index,"")`-style
string-to-empty mutations on ZEROFILL, the `Columns.Count > 0` ternary in
`ParseAddAction` whose false branch is dead code since `defs` is never
empty, the dead-field-initializer default in the computed-column path, the
`IsSymbol` index `>= 0` boundary since it's never called with a literal 0)
were confirmed equivalent by the same technique in earlier session work on
this file and still apply. No count of "true equivalent mutants" across the
whole file was exhaustively finalized — the remaining survivor list still
has real facet-parsing/boundary-condition territory (varchar/decimal facet
edge cases, `SkipParenGroup`'s own paren-depth arithmetic, `ApplyFacets`)
that wasn't individually traced this pass; treat the 84.0% file score as a
mix of "confirmed equivalent" and "not yet attempted," not "everything left
is equivalent."

One bounded additional pass (2 more boundary tests, committed separately)
was done per the session's own scope rule against re-opening the other 6
files or touching anything outside these 7 -- it moved the file's local
score slightly but not enough to close the project-wide gap alone.

Verified via full solution `dotnet build -c Release` (0 errors, pre-existing
warnings only) + `dotnet test` (34/34 test assemblies passed, 0 failures) —
log at `tmp/full-test-run.log` (gitignored).

**If continuing this line of work:** the highest-value next step is
`SkipParenGroup`/`ApplyFacets`/the numeric-facet-parsing block (~558-600),
which is NOT protected by the generic-flag-skip equivalence pattern above
(it runs before the flags loop) and so its boundary mutations are more
likely to be genuinely killable with the right crafted facet input.

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
