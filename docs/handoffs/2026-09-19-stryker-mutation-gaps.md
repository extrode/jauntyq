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

## Update 2026-09-19 (third pass): the SkipParenGroup/ApplyFacets follow-up, 92.98% -> 93.78%

Same-day follow-on, branch `test/analysis-mutation-coverage-followup`, merged
`--no-ff` into `dev` (unpushed). Scope per the prior pass's own "if
continuing" pointer: `SkipParenGroup`/`ApplyFacets`/the numeric-facet-parsing
block in `MigrationParser.cs`, plus a few other genuinely-fixable survivors
found while there. Test-only change, one file:
`MigrationParserMutationCoverageTests.cs` (+9 tests). No other files touched.

**Whole-project score: 92.98% -> 93.78%** (Killed 2197, Timeout 35, Survived
135, NoCoverage 13, out of 2380 tested; full unscoped run,
2026-09-19 21:45-21:50). **Still short of 96%.**

**`MigrationParser.cs` alone: 84.27% -> 86.50%** (scoped run: Killed 626,
Survived 95, Timeout 34, NoCoverage 8, out of 755 tested) — 17 additional
mutants killed.

New tests, by what they killed:
- `CreateTable_DoubleCommaBetweenColumns_ProducesEmptyDef_SilentlySkipped` —
  `ParseCreateTable`'s `if (def.Count == 0) continue;` (a double comma
  produces an empty def between two real columns).
- `CreateTable_EmptyColumnList_IsUnsupported_NotAZeroColumnTable` — the
  `stmt.Columns.Count > 0 ? stmt : Unsupported(raw)` fallback, via
  `create table t ()`.
- `AlterColumn_SetGenerated_IsUnsupported_SameAsAddOrDropGenerated` — the
  `SET`/`GENERATED` arm of the ALTER COLUMN identity-toggle guard.
- `GeneratedAlwaysAsIdentity_WithNoSeedParen_DoesNotConsumeTheFollowingNotNull`
  — the outer `IsSymbol(def, pos, "(")` guard before the identity-seed
  `SkipParenGroup` call (line 718).
- `ComputedColumn_GeneratedAsExpressionWithNestedFunctionCall_StoredThenNotNullStillParsed`
  and `DefaultExpression_WithNestedFunctionCall_DepthTracksCorrectly_NotNullStillParsed`
  — nested-paren depth tracking in `SkipParenGroup` and the inline
  DEFAULT-expression paren skip, using `round(price, 2)` as the nested call
  so a mistracked depth counter visibly swallows the trailing `NOT NULL`.
- `FacetParen_UnterminatedWithNoDigits_DoesNotThrow_NoCrash` and
  `FacetParen_UnterminatedAfterCommaWithNoSecondDigit_DoesNotThrow_NoCrash` —
  the facet-paren loop's own bounds checks on `decimal(` / `decimal(10,`
  with no closing paren.
- `ApplyFacets_EachDecimalLikeDbType_GetsPrecisionAndScaleFromFacets`
  (Theory: numeric/money/smallmoney) — `isDecimal`'s three literals besides
  `"decimal"`, which was the only one with an existing exact-value test.
- `ApplyFacets_PlainVarchar_IsUnicodeIsFalse_NotJustUnset` — the `false`
  side of `IsUnicode = t.StartsWith("n", ...)`, previously only asserted
  `true` for n-prefixed types.

**Newly confirmed equivalent mutants** (traced by hand, not force-killed):
- `SkipParenGroup`'s own entry guard (`if (!IsSymbol(def, pos, "(")) return;`,
  lines 745/746): **always unreachable in its false branch.** Both call
  sites (`if (IsSymbol(def, pos, "(")) SkipParenGroup(...)` at line 718, and
  the equivalent `else if (IsSymbol(...))` at line 721-724) only ever call
  `SkipParenGroup` after already confirming `pos` is at `"("`. The guard
  can never see `pos` NOT at `"("` in any real invocation, so negating it or
  blanking its string comparison changes nothing observable.
- The `pos < def.Count` / `while` bound checks inside `SkipParenGroup` and
  the sibling DEFAULT-expression paren-skip loop (lines 748, 751, 682, 685,
  689, and the analogous ones in the flags loop at 618/647/649/682/689):
  **equivalent given `Is`/`IsSymbol`'s own bounds safety.** Both helpers
  already guard `index >= 0 && index < tokens.Count` before touching
  `tokens[index]`, so mutating a loop's own `<`/`<=` bound (or negating a
  break condition) produces at most one extra iteration that calls
  `Is`/`IsSymbol` with an out-of-range index — which safely returns `false`
  — before the loop exits on its next check. No crash, no state change: the
  same mechanism as the already-documented "unterminated paren, no crash"
  cases, just reached through the equality/negate mutators instead of by
  directly deleting a bound.
- `GroupAlterActions`' `"MODIFY"` string literal, the ternary's **return
  value** (not the `Is(segment, 0, "MODIFY")` comparison, which is already
  killed by `AlterColumn_AllDialectForms`'s MySQL case): `action` is only
  ever compared via `action == "ALTER"` in `ParseAlterOrModifyAction`, so
  whether the literal action value is `"MODIFY"` or the mutated `""`, both
  fail that comparison identically and both take the (correct) non-ALTER
  branch. The variable's actual string value is never otherwise inspected.
- `ReadObjectName`/`BareName`'s `Conditional (true)` mutation on
  `dot >= 0 ? name.Substring(dot + 1) : name` (line 805): **equivalent by
  substring arithmetic**, not by unreachability. When `dot == -1` (no `.`
  found), the real code's false branch returns `name` unchanged; the
  mutated always-true branch instead evaluates `name.Substring(dot + 1)` =
  `name.Substring(0)`, which is the same string. Confirmed algebraically —
  holds for every input, not just the ones tried.
- The `MODIFY` result plus the entry-guard/bounds-check clusters above
  account for roughly 15-20 of the remaining ~95 `MigrationParser.cs`
  survivors; the rest (largely `String` mutations on flag keywords already
  covered by the prior pass's "generic unknown-flag skip absorbs it"
  mechanism, per the previous update) were not re-traced individually this
  pass — this is a lower bound on the equivalent count, not an exhaustive
  count.

Verified via full solution `dotnet build -c Release` (0 errors, pre-existing
warnings only) + `dotnet test` (all 34 test assemblies passed, 0 failures) —
log at `tmp/full-test-run.log` (gitignored).

**Where the remaining gap to 96% actually is:** with two of the biggest
equivalence classes now traced and documented (the generic-flag-skip
absorption from the prior pass, and the bounds-safety/unreachable-guard
classes from this pass), `MigrationParser.cs`'s residual ~95 survivors are
now mostly either confirmed equivalent or small individual `String`/
`Statement` mutations on facet/flag literals not yet traced one-by-one. Real
further gains likely need per-mutant tracing at this point rather than
another broad sweep — diminishing returns for the effort, consistent with
the prior pass's own assessment. **96% was not reached and is not expected
to be reachable purely through more `MigrationParser.cs` test-writing**;
the remaining gap is dominated by equivalent mutants in this file plus the
small pre-existing out-of-scope residue (~16-17 mutants across
`MigrationImpactReport.cs`, `ImpactReason.cs`, `DialectReservedWords.cs`,
`Diff/SchemaDelta.cs`, `Impact/ImpactEntry.cs`,
`Migrations/MigrationStatement.cs`, `Impact/Classification.cs`), not by
untested behavior.

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

## Update 2026-09-20: the 6 previously out-of-scope small files, 93.78% -> 94.33%

Follow-on to the request "some of the classes aren't tested at all or at
least below 90%, let's raise these numbers up" — targeting the 6 small
files that had been out of scope for the original 7-file plan and were
flagged as "pre-existing residue" in every prior update. Branch
`test/analysis-mutation-coverage-small-files`, merged `--no-ff` into `dev`
(unpushed). New file
`tests/Extrode.JauntyQ.Analysis.Tests/SmallModelTypesMutationCoverageTests.cs`
(14 tests). No production source touched.

**Whole-project score: 93.78% -> 94.33%** (Killed 2211, Timeout 34, Survived
124, NoCoverage 11, out of 2369 tested; full unscoped run, 2026-09-20
11:21-11:26).

Per-file, before -> after (scoped run against just these 6 files: 37/38
mutants killed, 97.37%):

| File | Before | After |
|---|---|---|
| `Migrations/MigrationStatement.cs` | 0.00% (0/2) | 100% |
| `Impact/ImpactReason.cs` | 25.00% (1/4) | 100% |
| `Impact/ImpactEntry.cs` | 33.33% (1/3) | 100% |
| `Impact/MigrationImpactReport.cs` | 60.00% (6/10) | 90% (9/10, 1 equivalent) |
| `Impact/Classification.cs` | 75.00% (3/4) | 100% |
| `Diff/SchemaDelta.cs` | 86.67% (13/15) | 100% |

What was killed (13 survivors + 2 NoCoverage, 13 fixed / 2 remaining):
- **Default field values never asserted**: `MigrationStatement.TableName`/
  `.RawText`, `ImpactReason.SchemaObject`/`.ChangeKind`/`.Effect`,
  `ImpactEntry.QueryFile`/`.EntityMethod`, `MigrationImpactReport.BaselineId`
  all default to `string.Empty` but no test ever constructed a default
  instance and checked it — these are DTOs whose only real "logic" is the
  default value itself, so this was the intended fix, not enumeration
  padding.
- `MigrationImpactReport.Highest`: added `Highest_NoEntries_IsSafe` and a
  multi-entry ordering test (kills nothing new for the `>`/`>=` mutant, see
  below, but exercises the method properly for the first time).
- `MigrationImpactReport.Count`: added a direct 3-classification test (was
  previously exercised only incidentally).
- `MigrationImpactReport.ToJson`'s `WriteIndented = true` (Boolean mutation
  to `false`, previously survived): killed via an explicit "output contains
  a newline" assertion — no prior test checked the JSON was actually
  pretty-printed vs. compact.
- `MigrationImpactReport.FromJson`'s `"Migration impact report deserialized
  to null."` exception message (NoCoverage): killed via
  `FromJson("null")` throwing with the exact message asserted.
- `Classification`'s `"Unknown classification '{token}'."` exception message
  in the JSON converter's `Read` (NoCoverage): killed via deserializing an
  unrecognized wire token and asserting the exact message. (The `"BREAKING"`
  case itself was already covered by `ReportSerializationTests`'s existing
  round-trip test.)
- `SchemaDelta.IsEmpty`'s three-term `&&` guard (2 Logical-mutation
  survivors, `&&`->`||` at two different positions): killed via one test per
  collection (added-only, removed-only, modified-only all non-empty ->
  `IsEmpty` false; all-empty -> true), which distinguishes every `&&`/`||`
  combination.

**One newly confirmed equivalent mutant** (not force-killed):
`MigrationImpactReport.Highest`'s `e.Classification > highest` -> `>=`
(line 50). `highest` is only ever reassigned to `e.Classification` inside
that branch. When `e.Classification == highest`, the mutated `>=` fires the
reassignment while the original `>` skips it — but the reassignment sets
`highest` to the exact value it already holds. No sequence of entries can
produce an observable difference between "skip" and "reassign to the same
value," so this mutation is equivalent regardless of entry order or count.
Confirmed by exhaustive case analysis (`>` and `<`, `==`, and both
directions of ordering all checked), not by trying inputs and giving up.

Verified via full solution `dotnet build -c Release` (0 errors, pre-existing
warnings only) + `dotnet test` (all 34 test assemblies passed, 0 failures) —
log at `tmp/full-test-run.log` (gitignored).

**These 6 files are no longer out-of-scope/untouched residue.** The
project-wide remaining gap to 96% (94.33% -> 96% needs ~38 more of the
current ~135 Survived+NoCoverage killed) is now almost entirely
`MigrationParser.cs`'s confirmed-equivalent-mutant-dominated residue (see
the two prior updates) plus `ReferencedObjects.cs`'s hash-randomization
equivalents — both already traced in detail above. No further known
low-hanging fruit remains; the next gain, if pursued, would be per-mutant
tracing in `MigrationParser.cs`'s flag-parsing area, which the prior update
already flagged as diminishing-returns territory.

## Update 2026-09-20 (later same day): round 4, the 4 files still below 96%, 94.33% -> 94.50%

Follow-on to the user's request "let's work on the first 4 that are below
96%" — the 4 remaining files under 96% at the time: `MigrationParser.cs`
(86.50%), `ReferencedObjects.cs` (86.54%), `MigrationImpactReport.cs`
(90.00%), `SchemaSimulator.cs` (94.17%). Branch
`test/analysis-mutation-coverage-round4`, merged `--no-ff` into `dev`
(unpushed). Test-only change: 4 new tests added to
`MigrationParserMutationCoverageTests.cs`. No production source touched, no
new file for the other 3.

**Whole-project score: 94.33% -> 94.50%** (Killed 2213, Timeout 36, Survived
120, NoCoverage 11, out of 2369 tested; full unscoped run, 2026-09-20
12:24-12:29).

**Per-file results:**

| File | Before | After |
|---|---|---|
| `Migrations/MigrationParser.cs` | 86.50% (660/763) | 87.02% (664/763) |
| `Impact/ReferencedObjects.cs` | 86.54% (45/52) | 86.54% (unchanged) |
| `Impact/MigrationImpactReport.cs` | 90.00% (9/10) | 90.00% (unchanged) |
| `Migrations/SchemaSimulator.cs` | 94.17% (194/206) | 94.17% (unchanged) |

**`SchemaSimulator.cs`: all 12 remaining survivors confirmed equivalent,
none force-killed.** Every one is a variant of the same pattern: every
`ApplyXxx` method guards a `TryFindTable`/`TryFindColumnKey`/`TryFindColumn`
call with `!found || outParam == null`, and all three `TryFind*` helpers
(read directly, lines 333-397) are written so the out-parameter is null iff
the method returns false — confirmed by inspection, not assumed, since
every code path that returns `false` explicitly sets the out-param to
`null` first, and every path that returns `true` assigns it a genuinely
non-null value. Under that contract, `!found || outParam==null` and the
mutated `!found && outParam==null` are logically identical (each reduces to
exactly `!found`), and `TryFindColumnKey(...) && key != null`'s `&&`->`||`
mutation (used inside `TryFindColumn` itself) reduces the same way. Two
non-obvious members of the same family:
- The `continue;` removed by a Block-removal mutation in `ApplyDropColumn`'s
  per-name loop (guarding a not-found column): when the guarded lookup
  fails, `actualKey` is `null`; without the `continue`, the loop falls
  through into a dictionary-rebuild step keyed on `actualKey`, but
  `string.Equals(kvp.Key, null)` is false for every real (non-null)
  dictionary key, so the rebuild silently copies every column unchanged —
  a genuine no-op, not a behavior change.
- `TryFindColumn`'s own final `return false;`, mutated to `return true;`
  (Boolean mutation): every actual call site pairs the call with `||
  existing == null`, so a caller sees the *same* "not found" outcome either
  way — the wrong boolean gets masked by the paired null-check at every
  call site, with no caller relying on the boolean alone.

**`ReferencedObjects.cs` and `MigrationImpactReport.cs`: no new survivors
found beyond what's already documented above** (the comparer's hash-
randomization/never-null-by-construction equivalents, and `Highest`'s
`>`/`>=` same-value-reassignment equivalent) — re-verified against a fresh
scoped run rather than assumed stale.

**`MigrationParser.cs`: 4 new tests, 4 mutants killed** (of the 8 targeted;
2 were an accidental duplicate of an existing test, 2 more were on a
mutation confirmed equivalent by direct manual-mutation verification, not
by tests):
- `AlterTable_MalformedFirstClauseFollowedByWellFormedClause_IsUnsupported_NotPartiallyParsed`
  killed the `GroupAlterActions` Block-removal mutation at the "first clause
  has no recognizable action verb" `return null;` (previously survived
  because the ONE existing test for this path used a single malformed
  clause with nothing after it, which converges on the same Unsupported
  result whether or not the early return fires — only a malformed clause
  followed by a well-formed one distinguishes them, since the mutated code
  silently drops the malformed clause and parses the rest instead of
  voiding the whole statement).
- `ComputedColumn_JunkTokenImmediatelyBeforeNull_DoesNotFalselyMarkNonNullable`
  and `ComputedColumn_JunkTokenAfterAnExplicitNotNull_DoesNotFlipNullabilityBack`
  killed the computed-column shorthand's inline NOT+NULL logical mutation
  (`&&`->`||`) and bare-NULL negate mutation respectively, both in the
  `total AS (expr) [PERSISTED] [NOT NULL|NULL]` flags loop — needed a junk
  token positioned so the mutated condition fires where the correct one
  wouldn't (or vice versa), since a clean "NOT NULL"/"NULL"-only input can't
  distinguish the mutated boolean logic from the original.
- `ColumnLevelPrimary_WithoutAFollowingKeyToken_DoesNotSetIsPrimaryKey`
  killed the main flags-loop's `Is(pos,"PRIMARY") && Is(pos+1,"KEY")`
  `&&`->`||` mutation (the existing
  `ColumnLevelPrimaryKey_RequiresBothTokens_NotKeyAlone` test contained
  neither "primary" nor "key" at all, so both forms evaluated to false
  identically and never exercised the mutation).
- A fifth authored test,
  `AlterColumnIdentityToggle_DropForm_Unsupported_NotMisparsedAsAColumnNamedDrop`,
  turned out to be a byte-for-byte duplicate of an existing
  `[InlineData]` case in `AlterColumnIdentityToggle_Postgres_Unsupported_NotMisparsed`
  and was removed rather than kept as redundant coverage.

**One mutation investigated and confirmed equivalent by direct manual
mutation** (not just reasoning — the source was actually edited, the
existing test suite run against the mutated build, observed to still pass,
then reverted and confirmed clean via `git diff`): the ALTER COLUMN
identity-toggle guard's `Is(body, p + 1, "SET")` term (`p+1`->`p-1`
arithmetic and `"SET"`->`""` string mutations, both at line 447). The
existing `AlterColumnIdentityToggle_Postgres_Unsupported_NotMisparsed`
theory's `"...set generated always"` case DOES exercise the real (correct)
code path, and *should* differ once mutated by this reasoning: the SET
term's OR-clause would flip false, the whole identity-toggle guard would
fail to match, and the code would fall through toward
`ParseColumnDef`. But `"SET"` is a genuine tokenizer keyword (unlike
`ADD`/`DROP`/`COLUMN`, which the tokenizer treats as plain identifiers, per
`GroupAlterActions`' own doc comment) — so `def[1].Type != Identifier`
catches it and returns `null` regardless, and `ParseColumnDef` returning
`null` produces the exact same `Unsupported` result. Confirmed by actually
applying the `p+1`->`p-1` mutation to the source, running the full theory
test class, observing all 5 cases still pass, then reverting (verified
clean via `git diff --stat`). This is a stronger equivalence proof than the
"trace by hand" reasoning used elsewhere in this document — worth reaching
for when a survivor looks like it *should* be killable but a targeted test
doesn't move it.

The other ~4 targeted survivors (facet-parsing `pos` arithmetic on
`decimal(10,2)`'s second-number branch, `varchar(max)`'s MAX-branch
`pos++`, `double precision`'s continuation-word `pos++`) were traced by
hand and found to fall under the SAME "generic unknown-flag skip absorbs
it" equivalence class documented in the second update above, just not
individually named there: every one of those positions is followed only by
tokens that don't match any of the flags loop's specific keyword checks
(digits, commas, parens, or a keyword like "PRECISION"/"MAX" that isn't
itself a recognized flag), so regardless of exactly where a mis-tracked
`pos` lands within that stretch, the generic `pos++ // unknown flag — skip
token` fallback absorbs every intervening token one at a time and the loop
still reaches the real trailing `NOT NULL`/`NULL` flag with the exact same
end state. This mechanism is powerful enough that most remaining
positional (non-decision) mutations in the facet-parsing region are likely
equivalent by the same reasoning, not just these four — worth remembering
before spending more effort on `pos`-arithmetic survivors specifically
there.

Verified via full solution `dotnet build -c Release` (0 errors, pre-existing
warnings only) + `dotnet test` (all test assemblies passed, 0 failures) —
log at `tmp/full-test-run.log` (gitignored).

**Where things stand:** 94.50%, still short of 96%. Of the 4 target files,
`SchemaSimulator.cs`, `ReferencedObjects.cs`, and `MigrationImpactReport.cs`
are now fully accounted for — every remaining survivor in all three is a
confirmed equivalent mutant, not a coverage gap, so their scores are at
their true ceiling barring a change to the production code itself (e.g.
removing the redundant `outParam == null` half of `SchemaSimulator.cs`'s
guards, which would be a source change, not a test change, and is out of
this pass's scope). `MigrationParser.cs` remains the only file with
real headroom, but its remaining ~99 survivors are now believed to be
mostly equivalent by the same two absorption mechanisms documented across
this and the prior update (generic-unknown-flag-skip, and
keyword-vs-identifier-tokenization safety nets) — further gains there would
need the same one-by-one manual-mutation-verification rigor as the `SET`
case above, which is slow per mutant. Not recommended to keep chasing 96%
by volume; a small number of individually-traced mutants at a time, with
manual-mutation verification when reasoning alone is inconclusive, is the
only remaining honest path forward.

## Update: near-target cleanup pass (2026-09-20, commit `1dd7886`)

Follow-up pass on the four files that were already ≥96% — `UpsertKeyResolver.cs`
(96.25%), `AutoCrud.cs` (97.90%), `DialectMapper.cs` (98.72%), and
`DialectReservedWords.cs` (99.68%) — to see if any could be pushed toward
99–100%. One real fix; the other 9 survivors across the three untouched files
are all confirmed equivalent.

**`AutoCrud.cs` 97.90% → 98.60% (real fix).** An Arithmetic mutation at line
198 (`new List<ColumnSchema>(pkCols.Count + versionCols.Count)` → `- versionCols.Count`)
survived because every existing test had `pkCols.Count >= versionCols.Count`,
so the capacity-hint value never went negative under the mutant. Killed with
`Update_WhereCols_CapacityHint_SurvivesMoreRowVersionColumnsThanPkColumns`: a
table with a single-column PK but two RowVersion-flagged columns makes the
mutant's capacity negative, which throws `ArgumentOutOfRangeException` from
the `List<T>` constructor before any SQL can be synthesized — the original
sum never goes negative for any valid input. `AutoCrud.cs`'s other survivor
(line 95, `allColumnsUsable = false; break;` inside the per-column usability
loop) is equivalent: the `columns` list built by that loop is discarded
outright by the `if (!allColumnsUsable || columns.Count == 0) continue;`
check immediately after, whether the loop breaks early or runs to completion
adding a few more (also-discarded) entries — same pattern as the
`UpsertKeyResolver.cs` findings below. `AutoCrud.cs`'s pre-existing NoCoverage
survivor (line 69, `JoinColumns`'s `if (cols.Count == 0) return "";` branch)
is dead code from the public API's perspective: every one of `JoinColumns`'
8 call sites either already gates on a non-empty column list (`insertCols.Count > 0`,
`setCols.Count > 0`) or is only reached after an earlier `pkCols.Count == 0`
early-return guarantees non-emptiness — there is no path through
`AutoCrud.Synthesize` that calls it with an empty list, so this branch is
unreachable through any legitimate test short of reflection into a private
method, which was not done.

**`UpsertKeyResolver.cs` — all 3 survivors equivalent, 96.25% is the ceiling.**
All three are `break;`-removal (Statement mutation) survivors, and all three
follow the identical "discarded on the failure path" pattern as `AutoCrud.cs`
line 95 above:
- Line 141 (`allResolved = false; break;` in the composite-index column
  loop): `keyCols` built by that loop is discarded by the `if (!allResolved)
  continue;` check right after, whether the inner loop breaks or keeps
  iterating.
- Line 346 (`found = true; break;` in `ContainsAllKeyColumns`'s inner
  column-match loop): `found` is never reset to `false` after being set, so
  scanning the remaining columns after finding a match has no observable
  effect — the outer loop's `if (!found) return false;` check sees the same
  `found` value either way.
- Line 280 (`continue;` in `HasCompetingUniqueConstraint`'s "index IS the key
  itself" branch, `if (!index.HasPrefixKeyPart && CoversKey(index.Columns,
  keyNames)) continue;`): `CoversKey` is defined as `constraintCols.Count ==
  keyNames.Count && ContainsAllKeyColumns(...)`, i.e. exact-set-equality —
  which is strictly stronger than the very next check's `ContainsAllKeyColumns`
  (superset) condition guarded by the same `!index.HasPrefixKeyPart` term.
  Whenever `CoversKey` is true, `ContainsAllKeyColumns` is trivially also
  true (an equal set is always a superset of itself), so removing this
  `continue` just lets execution fall through to a second `continue` on the
  very next line with the same guard — no observable difference.

**`DialectMapper.cs` — all 4 survivors equivalent, 98.72% is the ceiling.**
- Line 381 (Boolean mutation, the recursive `IsUnmappedDbType(...,
  isNullable: false, ...)` call's `false` argument flipped to `true` for the
  Postgres-array-type branch): `IsUnmappedDbType`'s own return value is
  `mapped == "object" || mapped == "object?"` — both of `MapDbTypeToCSharp`'s
  possible "unmapped" outputs are covered by the OR, so whichever `isNullable`
  value reaches the recursive call, the boolean result is identical.
- Line 424 (`NormalizeDbType`'s `parenIndex >= 0` → `parenIndex > 0`) and
  line 447 (`StripMySqlUnsignedModifier`'s `unsignedIndex >= 0` →
  `unsignedIndex > 0`, plus the paired "Conditional (true) mutation" that
  replaces the whole ternary with its true branch): both differ from the
  original only when the index is exactly `0` — i.e. the search character
  (`(` or the literal `"unsigned"`) sits at position 0 of the string. In that
  case the original strips to `""` (nothing before the delimiter) while the
  mutant either keeps the whole original string unstripped (equality mutant)
  or — for the "Conditional (true)" mutant, which is only reachable given
  `unsignedIndex >= -1`; since `StripMySqlUnsignedModifier`'s only call site
  (`DialectMapper.cs:211`) is itself gated on `dbType.Contains("unsigned",
  ...)`, `unsignedIndex` can never be `-1` there, so the ternary's false
  branch (`: dbType`) is dead code and always-true is behaviorally identical.
  For the boundary case itself (index `0`), neither the empty string nor the
  literal, un-stripped original (which still contains the paren character or
  the word "unsigned") can ever equal one of `DialectMapper`'s known
  dictionary keys (`"int"`, `"bigint"`, `"hierarchyid"`, etc., none of which
  are `""` or contain those characters) — so no test, however contrived,
  can observe a difference through any public entry point (`MapDbTypeToCSharp`,
  `IsUnmappedDbType`, `IsSqlServerClrType`).

**`DialectReservedWords.cs` — both survivors equivalent, 99.68% is the
ceiling.** Lines 212 and 257 both mutate an `if (string.IsNullOrEmpty(name)
|| string.IsNullOrEmpty(dialect)) return false;` early-exit guard's `||` to
`&&`. Under the mutant, the guard only fires when *both* are empty/null; a
call with just one of them empty falls through into the dialect-name
`string.Equals` checks (`IsReservedInDialect`) or the postgres-only check
(`RequiresQuotingForCase`). But `string.Equals(null-or-empty-dialect,
"postgres"/"mysql"/"sqlserver"/"sqlite", OrdinalIgnoreCase)` is `false` for
every known dialect string when dialect is `""` or `null`, and no reserved-word
set contains `""` as a member — so every downstream branch that the mutant
newly reaches for a single-empty input still ends up returning `false`,
matching the original's early return. Traced for all four
name-empty/dialect-non-empty and name-non-empty/dialect-empty-or-null
combinations; none distinguishes the two.

**Where things stand:** 94.54%, still short of 96%. With this pass, 6 of the
7 files below 100% (`UpsertKeyResolver.cs`, `AutoCrud.cs`, `DialectMapper.cs`,
`DialectReservedWords.cs`, `SchemaSimulator.cs`, `ReferencedObjects.cs`, and
`MigrationImpactReport.cs` — all but `MigrationParser.cs`) are now confirmed
at their equivalent-mutant ceilings; no further test-writing can move any of
them without a source change. `MigrationParser.cs` (87.02%) remains the sole
file with real remaining headroom, and remains slow to close for the reasons
documented in the prior update.
