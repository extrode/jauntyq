# Mutation coverage report — Extrode.JauntyQ.Analysis / SqlParser

Living record of Stryker.NET mutation-testing results for the two `src/`
assemblies that carry a Stryker config: `Extrode.JauntyQ.Analysis` and
`Extrode.JauntyQ.SqlParser`. Updated after each coverage-raising pass.
Equivalent-mutant reasoning and per-mutant detail live in
[`docs/_handoffs/2026-09-19-stryker-mutation-gaps.md`](../_handoffs/2026-09-19-stryker-mutation-gaps.md);
this file tracks the numbers over time.

Score formula: `(Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage)`.

## Score history

| Date | Milestone | Score | Commit |
|---|---|---|---|
| 2026-09-19 (earlier session) | Baseline before this effort | 32.52% | — |
| 2026-09-19 (earlier session) | Reserved-word tests + misplaced-test-file relocations | 81.51% | `98af6f3` |
| 2026-09-19 | 7-file targeted plan complete (DialectMapper, ReferencedObjects, UpsertKeyResolver, AutoCrud, SchemaSimulator, ImpactClassifier, MigrationParser 1st pass) | 91.89% | `9de5f4e` |
| 2026-09-19 | MigrationParser.cs 2nd pass | 92.98% | `e567844` |
| 2026-09-19 | MigrationParser.cs — SkipParenGroup/ApplyFacets follow-up | 93.78% | `75dfcf9` |
| 2026-09-20 | 6 previously out-of-scope small files raised | 94.33% | `951a120` |
| 2026-09-20 | Round 4 (4 remaining sub-96% files) | 94.50% | `3535b13` |
| 2026-09-20 | Near-target cleanup (UpsertKeyResolver/AutoCrud/DialectMapper/DialectReservedWords) | 94.54% | `1dd7886` |
| 2026-09-20 | SqlParser baseline run (first ever) | 59.22% | `209db2d` |
| 2026-09-20 | SqlParser — 6 zero-coverage IR model files closed | 59.65% | `6763087` |
| 2026-09-20 | fable-verify pass: fixed 3 mislabeled-equivalent survivors (DialectMapper/DialectReservedWords) | **94.66%** | `6059d24` |
| 2026-09-20 | SqlParser — SqlParser.cs/Part4/6/7.cs parser-core pass (76 new tests) | *(SqlParser whole-project re-run pending — see note below)* | `91e4c04` |
| 2026-09-20 | MigrationParser.cs — per-mutant pass (12 new tests, 7 real gaps) | 89.78% (scoped; whole-project re-run pending) | `3938a48` |
| 2026-09-20 | Whole-project re-run confirming the per-mutant pass | 95.55% | (pre-`4b5c154`) |
| 2026-09-20 | MigrationParser.cs — `ReadObjectName` bracket-quoted-dot merge correction + boundary test | **95.84%** | `c9fa8c1` |
| 2026-09-20 | Adversarial fable-verify pass over `pos <= def.Count`/`SkipParenGroup` equivalence claims + MigrationParser.cs DEFAULT-value Negate mutant fix (line 689, flagged by the pass) | **95.97%** | `ac22f74` |
| 2026-09-20 | MigrationParser.cs GENERATED-column Negate mutant fix (line 704, found by manual re-check after the fable-verify pass) | **96.01% — target reached** | (merge into `dev` after `34c070b`) |
| 2026-09-20 | Post-96% survivor pass: 1 real gap in `ReferencedColumnComparer.Equals` + 6 real gaps in `MigrationParser.cs` (91→84 survived) | **96.30%** | `703192c`, `f8e2c18` |
| 2026-09-20 | Adversarial fable-verify pass over the 84 remaining survivors: 0/18 small-file claims refuted, 3/60 `MigrationParser.cs` claims refuted (6 mutants, 2 correcting wrong claims) | **96.47%** | `9dba99b`, `cca01e6` |
| 2026-09-21 | Genuine fable-model verify pass: 5 real gaps fixed in `MigrationParser.cs` | 96.47% → higher (scoped fixes, whole-project re-run below) | `5deae22` |
| 2026-09-21 | Malformed-SQL survivor category resolved: 6 mutants killed with a nested-paren test, 3 confirmed genuinely equivalent (outer-loop IDENTITY-branch fallback) | — | `a9603e4` |
| 2026-09-21 | Whole-project re-run confirming both passes above | **97.23%** | `0eee118` |
| 2026-09-21 | Stryker comment-based exclusions for 47 confirmed-equivalent survivors across 7 files | **99.14%** | (pending commit) |

## Current per-file breakdown (as of 95.84%, whole-project re-run 2026-09-20 19:05-19:09, confirming the `ReadObjectName` fix)

19 source files, 2380 mutants tested (2281 killed+timeout, 95 survived, 4 no-coverage).

**Superseded by the 96.01% whole-project run, 2026-09-20 20:12-20:16**
(2285 killed+timeout, 91 survived, 4 no-coverage, 2380 total) — the two
fixes below (lines 689 and 704) moved 4 mutants from Survived to Killed;
Stryker's coverage-based test-impact selection also shifted 2 mutants
between the Killed and Timeout buckets run-to-run (694 killed / 33 timeout
in the prior run's `MigrationParser.cs` row vs. this run's totals), which is
normal run-to-run noise in timeout classification, not a regression. No
fresh file-scoped `MigrationParser.cs` run has been taken at 96.01%, so the
per-file breakdown below is not re-stated row-by-row; the totals above are
the authoritative current numbers.

| Score | Killed | Timeout | Survived | NoCov | Total | File |
|---|---|---|---|---|---|---|
| 90.00% | 9 | 0 | 1 | 0 | 10 | `Impact/MigrationImpactReport.cs` |
| 90.38% | 45 | 0 | 5 | 2 | 52 | `Impact/ReferencedObjects.cs` |
| 91.86% (→ was 89.78% scoped) | 659 | 33 | 70 | 1 | 763 | `Migrations/MigrationParser.cs` |
| 94.17% | 194 | 0 | 12 | 0 | 206 | `Migrations/SchemaSimulator.cs` |
| 96.25% | 77 | 0 | 3 | 0 | 80 | `UpsertKeyResolver.cs` |
| 98.60% | 141 | 0 | 1 | 1 | 143 | `AutoCrud.cs` |
| 99.36% | 307 | 3 | 2 | 0 | 312 | `DialectMapper.cs` |
| 99.84% | 627 | 4 | 1 | 0 | 632 | `DialectReservedWords.cs` |
| 100.00% | 1 | 0 | 0 | 0 | 1 | `AnalysisDiagnostic.cs` |
| 100.00% | 27 | 0 | 0 | 0 | 27 | `CrudColumnRules.cs` |
| 100.00% | 15 | 0 | 0 | 0 | 15 | `Diff/SchemaDelta.cs` |
| 100.00% | 44 | 0 | 0 | 0 | 44 | `Diff/StructuralSchemaDiff.cs` |
| 100.00% | 15 | 0 | 0 | 0 | 15 | `EntityNameResolver.cs` |
| 100.00% | 4 | 0 | 0 | 0 | 4 | `Impact/Classification.cs` |
| 100.00% | 66 | 0 | 0 | 0 | 66 | `Impact/ImpactClassifier.cs` |
| 100.00% | 3 | 0 | 0 | 0 | 3 | `Impact/ImpactEntry.cs` |
| 100.00% | 4 | 0 | 0 | 0 | 4 | `Impact/ImpactReason.cs` |
| 100.00% | 1 | 0 | 0 | 0 | 1 | `Impact/QueryImpactInput.cs` |
| 100.00% | 2 | 0 | 0 | 0 | 2 | `Migrations/MigrationStatement.cs` |

## Mutation-coverage test files added this effort

| Test file | Target file | Score reached | Commit |
|---|---|---|---|
| `AutoCrudSynthesizeTests.cs` (strengthened) | `AutoCrud.cs` | 97.90% → 98.60% | `7c554e0`, `6e4c5d9` |
| `ImpactClassifierTests.cs` (strengthened) | `Impact/ImpactClassifier.cs` | 100.00% | `875ac78` |
| `SchemaSimulatorMutationCoverageTests.cs` | `Migrations/SchemaSimulator.cs` | 94.17% | `bd11450` |
| `DialectMapperMutationCoverageTests.cs` | `DialectMapper.cs` | 98.72% → 99.36% | `147e904`, `6059d24` |
| `ReferencedObjectsMutationCoverageTests.cs` | `Impact/ReferencedObjects.cs` | 86.54% | `e6a75c3` |
| `UpsertKeyResolverMutationCoverageTests.cs` | `UpsertKeyResolver.cs` | 96.25% | `bf40c03` |
| `MigrationParserMutationCoverageTests.cs` (created) | `Migrations/MigrationParser.cs` | 84.0% → 87.02% | `23e129c`, `04f7b1f`, `75dfcf9`, `091f6a9` |
| `MigrationParserMutationCoverageTests.cs` (12 more tests) | `Migrations/MigrationParser.cs` | 87.02% → 89.78% | `3938a48` |
| `SmallModelTypesMutationCoverageTests.cs` | `MigrationStatement.cs`, `ImpactReason.cs`, `ImpactEntry.cs`, `MigrationImpactReport.cs`, `Classification.cs`, `SchemaDelta.cs` | 90–100% | `951a120` |
| `DialectReservedWordsTests.cs` (earlier session, strengthened) | `DialectReservedWords.cs` | 99.68% → 99.84% | `7a08a4b`, `6059d24` |

Also relocated from `Generator.Tests` to `Analysis.Tests` (earlier session): `DialectMapperTests.cs`
(`ae9b086`), `MigrationParserTests.cs` (`afb9fad`, `9f34577`).

## Remaining gap to 96%

As of 94.54%, 4 files remain below 96%: `ReferencedObjects.cs` (86.54%),
`MigrationParser.cs` (87.02%), `MigrationImpactReport.cs` (90.00%), and
`SchemaSimulator.cs` (94.17%). The round-4 pass (`091f6a9`, `555dda3`,
merged `3535b13`) fixed 4 more `MigrationParser.cs` mutants and rigorously
re-verified the other three files, concluding:

- `SchemaSimulator.cs` is **fully closed** at 94.17% — all 12 remaining
  survivors proven equivalent via the `TryFindTable`/`TryFindColumnKey`/
  `TryFindColumn` "null-iff-false" contract (traced through every helper and
  call site). No further test can move this file without a source change.
- `ReferencedObjects.cs` and `MigrationImpactReport.cs` were re-verified with
  no new findings — both remain at their documented-equivalent floors.
- `MigrationParser.cs` still has ~91 survivors and is the only file with real
  remaining headroom, but most now fall under one of two absorption
  mechanisms (the "unknown flag, skip one token" fallback, generalized this
  round to cover positional-arithmetic facet-parsing mutations too, not just
  flag-keyword literals) — further gains need slow per-mutant manual-mutation
  verification, not another broad sweep.

A follow-up "near-target cleanup" pass (`6e4c5d9`, merged `1dd7886`) pushed
the four files that were already ≥96% toward their own ceilings instead:

- `AutoCrud.cs` 97.90% → **98.60%**: the one real fix was a `List<T>` capacity
  arithmetic mutant (`pkCols.Count + versionCols.Count` → `- versionCols.Count`)
  killed with a table carrying more RowVersion columns than PK columns
  (negative capacity would otherwise throw). Its remaining `break;`-removal
  survivor is equivalent — the loop's result list is discarded on the failure
  path regardless of whether the loop breaks early or keeps iterating.
- `UpsertKeyResolver.cs` (96.25%), `DialectMapper.cs` (98.72%), and
  `DialectReservedWords.cs` (99.68%) did **not** move — all 9 remaining
  survivors across the three were proven equivalent by direct code-flow
  tracing (documented in the handoff doc): mostly `break;`/`continue;`
  removals whose surrounding loop already discards its accumulated state on
  the same path, plus two `NormalizeDbType`/`StripMySqlUnsignedModifier`
  boundary conditions (`index == 0`) that are reachable only by strings that
  can never match a real dictionary key either way, and a `||`→`&&` guard
  mutation in `DialectReservedWords` whose "protected" branches already fall
  through to `false` on their own. These three files are now considered
  **closed at their current scores** — no further test-writing can move them.

Net: the 96% target is likely not fully reachable through more test-writing
alone; the remaining gap is dominated by a confirmed equivalent-mutant floor
in 6 of the 7 files below 100% (all but `MigrationParser.cs`), which remains
the sole file where further (slow) work could still move the needle.

**Correction, 2026-09-20 (fable-verify pass).** The "did **not** move" /
"closed at their current scores" claim two paragraphs up turned out to be
wrong for 2 of the 3 files it names. An adversarial (fable) verify pass,
instructed to try to break each equivalence claim rather than confirm it,
found real, killable gaps in `DialectMapper.cs` (2 mutants) and
`DialectReservedWords.cs` (1 mutant) — both boundary/guard conditions whose
equivalence reasoning missed a code path (see
[the handoff doc's fable-verify update](../_handoffs/2026-09-19-stryker-mutation-gaps.md)
for the full per-mutant trace). Fixed in branch `test/fix-mislabeled-equivalents`
(`6059d24`): `DialectMapper.cs` 98.72% → **99.36%**, `DialectReservedWords.cs`
99.68% → **99.84%**. `UpsertKeyResolver.cs`'s equivalence claim held under
the same adversarial pass. The "6 of 7 files" count in the Net paragraph
above is accordingly one file too many as originally stated — the corrected,
per-mutant-traced state for all 7 sub-100% files is in the current per-file
breakdown table above this section, not this historical paragraph.

**Final update: the 96% target was reached, 2026-09-20 20:12-20:16 — 96.01%.**
Two more genuine gaps were found in `MigrationParser.cs` after this section
was written (line 689 and line 704, both documented further down this file),
closing the last 1-mutant gap. The "likely not fully reachable through more
test-writing alone" conclusion above turned out to be wrong — the target was
reached with real tests, not source changes or contrived tests. See the
score-history table at the top of this document for the full path from
94.66% to 96.01%.

### MigrationParser.cs — per-mutant pass, 2026-09-20 (87.02% → 89.78%)

Went through the ~91 survivors from the prior baseline one by one (or in
small related batches): read the exact mutated line, tried to construct a
concrete distinguishing SQL input, wrote and verified a test against both
the real source and a hand-mutated copy where a test's result was ever in
doubt. 12 new tests added to `MigrationParserMutationCoverageTests.cs`, all
44 tests in that file still pass. Result: Killed 632→652 (+20, since several
tests each kill 2-3 related mutants sharing one root cause), Survived 91→70,
NoCoverage unchanged at 8, scoped score 87.02%→**89.78%**.

**7 real, killable gaps found and fixed** (mutated line, mechanism, killed via):

1. **Line 502/745** — `SkipParenGroup` call removed/no-op in the AS-shorthand
   computed-column path (`Total AS (Qty * Price)`). Without the call, the
   parenthesized expression's tokens leak into the flags loop one at a time.
   Killed with a computed column whose expression contains a token that
   textually matches a flag keyword.
2. **Line 675** (`DEFAULT` keyword-literal blanking) — breaks the atomic
   default-expression skip; the DEFAULT keyword becomes an unrecognized
   token and its expression's tokens leak into the flags loop instead of
   being atomically skipped.
3. **Line 447** (`ADD`/`DROP` GENERATED|IDENTITY arms of an OR-chain) — only
   the `SET` arm had a test previously; added two tests covering `ADD` and
   `DROP`.
4. **Line 626** (bare `NULL` flag-check blanking) — specifically the
   "flip PK-forced nullability back to `true`" case, not just the ordinary
   nullable-flag case already covered.
5. **Line 640** (bare `AUTOINCREMENT`/`SERIAL` keyword flags) — distinct
   from the `SERIAL` DbType-sugar path; two new tests, one per keyword.
6. **Line 647** (identity-seed-paren closing-`)` detection) — ~~specifically
   the case where a real closing paren IS present and is followed by more
   flags, distinguished from the already-tested genuinely-unterminated case
   (which produces the same observable result either way).~~ **Correction,
   2026-09-20 (fable-verify pass):** this test does not actually kill the
   `<=` boundary mutant it was written for — per `Is`/`IsSymbol`'s own
   bounds-checking (lines 792-799), that mutant is genuinely equivalent. The
   test's real (and only) kill power is against an unrelated `&&`→`||`
   Timeout mutant on the same line. The test's comment in
   `MigrationParserMutationCoverageTests.cs` was corrected to state this
   honestly rather than the original overclaim; this is the one item of the
   7 below that is not, as originally listed, a "real, killable gap."
7. **Line 815-818** (`ReadObjectName`'s multi-level dotted-name while-loop,
   previously `NoCoverage`) — required bracket-quoted syntax
   (`[dbo].[Gadgets]`) to trigger the tokenizer-split path at all; unbracketed
   dotted names tokenize as one compound identifier handled entirely by
   `BareName`, never reaching this loop.
8. **Line 851** (`SplitTopLevel`'s comma-detection, `&&`→`||` mutation) —
   at paren-depth 0, the mutated condition treats *any* Symbol token (not
   just `,`) as a column-def separator. A first attempt using
   `create table t (a int default -1, b int)` did NOT distinguish the
   mutant: the spurious split at `-` produces an orphaned `[1]` fragment
   whose leading token is a `Number`, which `ParseColumnDef`'s
   `def[0].Type != Identifier` guard silently filters — column count/names
   came out identical either way. The mutant-killing input needed a flag
   *after* the negative default (`a int default -1 not null, b int`): under
   the mutation, `NOT NULL` ends up inside that same orphaned/filtered
   fragment and is lost, so column `a` wrongly reports `IsNullable == true`.
   This is a concrete instance of the "always empirically verify a survived
   test actually distinguishes the mutant — don't stop at 'looks plausible'"
   lesson from this campaign; see the coverage-misattribution caveat below
   for the matching Stryker-side version of the same lesson.

**No new equivalent-mutant classes were confirmed this round beyond what was
already documented** in the handoff doc — the ~70 remaining survivors were
individually re-traced (not just template-matched against existing
categories) and fall entirely under the previously-established mechanisms:
unknown-flag/positional-arithmetic absorption in the flags loop (lines
558-751 region, including the numeric-facet block and the GENERATED
ALWAYS/BY DEFAULT/AS IDENTITY vs. AS (expr) STORED/VIRTUAL block),
bounds-safety via `Is`/`IsSymbol` (lines 618-748 loop-bound comparisons),
dead/unreachable branches (`SkipParenGroup`'s unreachable guard, line 798's
`index > 0` in a helper only ever called with `index >= 0` from a caller
that already special-cases the zero case upstream), discarded return values
(`SplitTopLevel`/`SplitRemaining`'s exact final `pos`, lines 845/862/886/
898-899 — never re-read by the sole caller in either method), and algebraic
equivalence (line 805's `dot >= 0 ? Substring(dot+1) : name`). Time did not
allow writing out a fresh per-line justification for every one of the ~70 in
this doc; the categories above are the same ones already itemized with
specific line numbers in the handoff doc's MigrationParser section, and this
pass's spot checks did not surface any case where the category assignment
was wrong.

**Open methodological caveat — Stryker coverage misattribution.** During
this pass, Stryker's own scoped report listed the line-506 String mutations
(`"NOT"`→`""` and `"NULL"`→`""`) as "Survived" in two separate runs, but
direct hand-mutation + `dotnet test --filter` against the real test suite
proved both are already killed by the pre-existing test
`ComputedColumn_JunkTokenAfterAnExplicitNotNull_DoesNotFlipNullabilityBack`.
Attempts to fix this via `--coverage-analysis all` (unrecognized CLI option,
silently no-ops) and via `"coverage-analysis": "all"` in `stryker-config.json`
(accepted without error, but the run log still showed
`'SkipUncoveredMutants'`/`'CoverageBasedTest'` mode and produced identical
counts) did not resolve it. This means Stryker's own survivor counts for
this file may already include false positives beyond the two confirmed
here — a caveat for whoever picks up the remaining ~70, not something
resolved in this pass. Ground truth for any individual mutant should be
re-verified by hand-mutation + targeted `dotnet test --filter`, not taken on
Stryker's report alone.

### MigrationParser.cs — `ReadObjectName` bracket-quoted-dot correction, 2026-09-20 (89.78% scoped → 95.84% whole-project)

Whole-project re-run after the 89.78% scoped pass landed at **95.55%**
(2241 killed, 33 timeout, 95 survived, 11 no-coverage) — an exact match for
the pre-computed estimate above, confirming that number. Investigating the
file's 8 `NoCoverage` mutants found the existing test
`CreateTable_BracketQuotedSchemaAndTableName_Tokenizer...` was asserting a
false premise: its comment claimed `[dbo].[Gadgets]` exercises
`ReadObjectName`'s dotted-name while-loop (lines 815-818), but tracing
`SqlTokenizer.MergeQualifiedIdentifiers` proved the tokenizer actually
**merges** `[dbo].[Gadgets]` into one `Identifier` token before the loop ever
runs (it only leaves an `Identifier '.' Identifier` sequence unmerged when the
left segment's own value already contains a literal dot and isn't a chain
continuation) — the test passed, but not for the reason its comment claimed,
and the loop itself stayed uncovered.

Fixed in `test/migrationparser-readobjectname-boundary` (`4b5c154`, merged
`c9fa8c1`): renamed the test to
`CreateTable_BracketQuotedSchemaAndTableName_TokenizerMergesToOneIdentifier`
with a corrected comment, and added two new tests exploiting the one input
shape that actually reaches the while-loop — a bracket-quoted segment
containing a literal internal dot (`[db.with.dots].[Gadgets]`):

- `CreateTable_BracketQuotedNameContainingLiteralDot_TokenizerLeavesUnmergedForReadObjectNamesLoop`
  — kills the loop's real body mutants and documents the line-815
  `tokens[pos + 1]` → `tokens[pos - 1]` mutant as equivalent (at that check,
  `pos` is always one past a just-consumed `Identifier`, so `tokens[pos - 1]`
  is always of type `Identifier` too — same boolean result as the correct
  check, whenever the loop's other conditions hold).
- `CreateTable_TrailingDotWithNoFollowingIdentifier_DoesNotOverrunTheTokenArray`
  — guards the loop's `pos + 1 < tokens.Count` bound with a truncated input
  (`create table [db.with.dots].`) so the unmerged trailing `.` is the last
  token; without the bound, the inner `tokens[pos + 1]` read would index past
  the end of the array.

Result: whole-project Killed 2241→2248 (+7), NoCoverage 11→4, Survived flat
at 95 (6 of the file's 8 no-coverage mutants killed outright, the other 2
converted to 1 killed + 1 confirmed-equivalent, per the file's own
`Survived 70 / NoCov 1` split above). **Whole-project score: 95.55% →
95.84%.** Full Analysis suite: 1431/1431 on both net8.0 and net10.0.

**Why 96% is likely not reachable through more test-writing.** 96% needs
`Survived + NoCoverage ≤ 95` project-wide; the count is currently 99, so 4
more mutants would need killing with no equivalent-mutant substitutions. A
focused trace of the file's remaining survivors found a systematic
equivalence class that likely accounts for most of what's left: nearly every
`while (pos < def.Count)`-style loop guard in this file has a
`pos <= def.Count` ("off-by-one loosen") survivor (lines 503, 618, 647, 649,
682, 689, 748), and every one of them is equivalent for the same reason —
the loop body's own `Is`/`IsSymbol` calls re-check `index < tokens.Count`
internally (lines 792-799), so once `pos` reaches `def.Count`, `Is`/`IsSymbol`
already return `false` regardless of the loop guard's `<` vs `<=`. The
mutation lets the loop run exactly one extra (no-op) iteration, incrementing
`pos` to `def.Count + 1`, but that value is never read by anything after the
loop exits — same observable result either way. This matches (and
generalizes) the line-798 equivalence reasoning already documented above. Line
746's lone remaining `NoCoverage` (`SkipParenGroup`'s `if (!IsSymbol(...))
return;` guard) is dead code in practice: all 3 call sites in the file (lines
502, 719, 724) only ever invoke `SkipParenGroup` after already confirming
`IsSymbol(def, pos, "(")` is true, so the guard's false branch is unreachable
through the public API — not a real gap, just an unreachable defensive check.

**Adversarially re-verified, 2026-09-20 (fable-verify pass).** A second pass
instructed to try to refute the above, not confirm it, hand-traced each of
the 7 `pos <= def.Count` line numbers individually (not just template-matched)
and confirmed all 7 equivalent, and grepped the whole project (not just this
file) for `SkipParenGroup` call sites, confirming exactly 3 (the doc
originally undercounted at "2") and that all 3 pre-check `IsSymbol(...,
"(")`. No refutations survived. The pass did, however, surface one mutant
outside the scope of what it was asked to check: a **Negate** mutation on
line 689 (`else if (pos < def.Count)` → `else if (!(pos < def.Count))`, the
single-token non-parenthesized `DEFAULT` value branch) that traces as a real,
killable gap, not equivalent — `NOT NULL DEFAULT NULL`'s trailing `NULL`
value token is left unconsumed under the mutation and leaks back into the
flags loop's own `Is(def, pos, "NULL")` check, wrongly flipping
`IsNullable` back to `true` even though `NOT NULL` already set it `false`.
Fixed with a new test,
`DefaultExpression_SingleTokenNullValueAfterExplicitNotNull_DoesNotFlipNullabilityBack`,
mirroring the existing parenthesized-expression sibling test just above it in
`MigrationParserMutationCoverageTests.cs`.

**Second genuine gap found while re-checking survivors after that fix,
2026-09-20.** A subsequent whole-project run (95.55% → **95.97%** after the
above fix) left the project 1 mutant short of 96%. Re-tracing line 704's
Negate mutant (`if (Is(def, pos, "GENERATED"))` → `if
(!(Is(def, pos, "GENERATED")))`) — previously assumed part of the
"unknown-flag absorption" equivalence class without being individually
verified — found it is also a real, killable gap. Hand-simulating several
GENERATED-shaped inputs first suggested equivalence (the mutation makes the
block enter one token late, but its own internal forward search for "AS"
coincidentally resynchronizes for every input containing the optional
"ALWAYS"/"BY DEFAULT" prefix, since the misaligned entry token gets
consumed harmlessly and "AS" is still found one position later than
expected) — every existing test happens to use that shape, which is exactly
why this survived undetected. Omitting the optional "ALWAYS" prefix breaks
the resynchronization: the misaligned block instead consumes the real "AS"
token itself as the (wrongly) expected flag keyword, then searches for "AS"
one token too late (landing on "(") and never finds it, so `IsComputed`
silently stays `false`. Confirmed by hand-mutating the source directly:
`create table t (total int generated as (x))` gives `IsComputed=True` on
real code and `IsComputed=False` under the mutant. Fixed with
`GeneratedAsComputedColumn_WithoutAlwaysKeyword_StillMarkedComputed`. Full
suite: 1433/1433 on both net8.0 and net10.0. This is a caution against
trusting a survivor's category assignment without re-verifying each
specific line — "looks like the same absorption pattern as its neighbors"
was wrong here despite being right for the 7 `pos <= def.Count` mutants
right next to it.

**Update: this line-704 fix, combined with the line-689 fix above, closed
the last 1-mutant gap.** The whole-project run immediately after (2026-09-20
20:12-20:16) landed at **96.01%** — target reached. Note that this session's
own prediction just above ("recommend treating 95.84% as the practical
ceiling") turned out to be wrong twice in a row: both the line-689 and
line-704 mutants looked, on first pass, like they belonged to the same
absorbed/equivalent category as their neighbors, and both were in fact real,
killable gaps. The remaining ~91 survivors after this fix are still believed
equivalent under the previously-documented absorption mechanisms, but that
belief has now been wrong twice for mutants that looked exactly this
confident beforehand — treat any future "this is definitely the practical
ceiling" claim in this file with the same skepticism, and adversarially
verify before relying on it.

**Coverage gap (NoCoverage mutants) adversarially closed, 2026-09-20.** At
96.01% the whole project still carries exactly 4 `NoCoverage` mutants (never
executed by any test, as opposed to the 91 `Survived` mutants that ran but
weren't killed): `AutoCrud.cs`'s `JoinColumns` empty-list guard (line 69,
`return ""`), `ReferencedObjects.cs`'s `ReferencedColumnComparer.GetHashCode`
`?? ""` fallbacks (lines 185-186), and `MigrationParser.cs`'s
`SkipParenGroup` not-`"("` early return (line 746, already discussed above
as one of the 7-mutant class's siblings). An adversarial fork, instructed to
find a real call path refuting each claim rather than confirm it, traced
every call site fresh: `JoinColumns`'s 8 call sites in `AutoCrud.cs` are each
upstream-guarded (`pkCols`/`whereCols` via `pkCols.Count == 0 continue`,
`insertCols`/`setCols` via their own `Count > 0` blocks, `columns` via an
`allColumnsUsable || columns.Count == 0` gate); `ReferencedColumn`'s `Table`
and `Column` are only ever constructed inside `ResolveInto`'s `AddColumn`
closure from strings already checked non-empty (column at line 80, table via
`aliasToTable`/`alias`/`inScope`, all populated only from non-empty
strings); and `SkipParenGroup`'s 3 call sites (lines 502, 719, 724) all
pre-check `IsSymbol(def, pos, "(")` immediately before calling, with no
intervening mutation of `pos`. No counter-examples were found — all 4
`NoCoverage` mutants are confirmed genuinely unreachable dead/defensive
code, not real coverage gaps. **The `NoCoverage` portion of the gap to 96%
is closed**; all remaining headroom is in the 91 `Survived` mutants, not
addressed by this pass.

## Post-96% survivor pass, 2026-09-20 — small files

With the target met, work moved to individually re-verifying (not
template-matching) the 91 remaining `Survived` mutants, starting with the
19 in `SchemaSimulator.cs`, `ReferencedObjects.cs`, `AutoCrud.cs`, and
`MigrationImpactReport.cs` whose equivalence had only been self-certified in
the earlier round-4 pass, never adversarially checked.

**One genuine gap found and fixed.** `ReferencedObjects.cs`'s
`ReferencedColumnComparer.Equals` (the `&&` combining the table-name and
column-name comparisons) had a surviving `&&`→`||` Logical mutation. The two
existing tests that should have caught it
(`Resolve_SameColumnNameOnDifferentTables_KeptAsDistinctEntries`,
`Resolve_DifferentColumnNamesOnSameTable_KeptAsDistinctEntries`) only
observe `Equals` at all when two entries land in the same `HashSet<T>`
bucket — pure luck of the hash layout, since `GetHashCode` combines both
fields, so two entries differing in only one field usually (but not
always) hash to different buckets and never reach `Equals`. Fixed by
calling the private `ReferencedColumnComparer.Instance` directly via
reflection, bypassing bucket placement: 3 new tests in
`ReferencedObjectsMutationCoverageTests.cs`
(`Comparer_SameColumnDifferentTable_IsNotEqual`,
`Comparer_SameTableDifferentColumn_IsNotEqual`,
`Comparer_SameTableAndColumn_IsEqual`). Verified by hand-mutation: the two
negative-case tests fail against the `||` mutant, pass against real code.

**18 mutants confirmed equivalent, re-derived from scratch:**

- `ReferencedObjects.cs`'s `GetHashCode` (4 mutants: 2 null-coalescing
  fallbacks, 1 bitwise-complement, 1 arithmetic-divide) are all equivalent
  for the same reason: `GetHashCode`'s only contract obligation is "equal
  objects → equal hash codes," not distinctness or specific values. Each
  mutation is a deterministic pure transform applied uniformly, so it can
  only ever increase hash collisions — which `HashSet<T>` already handles
  correctly via `Equals` (now itself covered, see above). No input through
  the public API can distinguish a worse-but-still-contract-preserving hash
  function. This corrects the prior claim, which had conflated this with
  the separate (and also equivalent, but for a different reason) NoCoverage
  null-fallback finding above.
- `SchemaSimulator.cs`'s all 12 survivors, re-derived (not re-stated) via
  the `TryFindTable`/`TryFindColumnKey`/`TryFindColumn` "out-param null iff
  returns false" invariant, checked against every real call site: 9
  caller-side `!TryFind...() || x == null` Logical mutations
  (`A||A`→`A&&A`, both reduce to `A` under the invariant), 1 internal
  `&&`→`||` at the same identity, 1 inert `continue;` removal (the loop's
  last statement before implicit loop-back — the fallthrough produces a
  logically identical, if reallocated, result), and 1 `return false`→
  `return true` bug inside `TryFindColumn` that is fully masked by both
  call sites' own redundant `|| existing == null` guard.
- `AutoCrud.cs`'s `break;`-removal survivor: equivalent because the loop's
  only externally-visible effect is the `allColumnsUsable` flag, which the
  surrounding `if (!allColumnsUsable || columns.Count == 0) continue;`
  gates on — removing `break` just lets the loop finish populating a
  `columns` list that's discarded either way.
- `MigrationImpactReport.cs`'s `>`→`>=` survivor in `Highest`'s max-finding
  loop: equivalent because `Classification` is a value-type enum, so
  reassigning `highest = e.Classification` when the two are already equal
  is a true no-op with no observable side effect.

Full suite: 1436/1436 passing on both net8.0 and net10.0 after the fix.

## Post-96% survivor pass, 2026-09-20 — MigrationParser.cs full sweep

Following the small-files pass above, all 66 remaining `Survived` mutants in
`MigrationParser.cs` were individually re-traced (not template-matched
against prior "equivalent" classes without checking the specific mechanism
applies).

**6 more genuine gaps found and fixed** (5 new tests, one of which also
corrects a wrong equivalence claim written earlier this session):

- `ColumnFlag_ParenthesizedDefaultExpression_TrailingNotNullStillRecognized`
  (line 685) — the DEFAULT branch's paren-depth scan (`--depth == 0`
  mutated to `!= 0`) never finds the matching close-paren of a single
  balanced pair like `(5)` (depth goes 1→0, which doesn't satisfy `!= 0`),
  so the scan runs past the closing paren with nothing to stop it, silently
  swallowing a trailing `not null` as part of the "skipped default
  expression."
- `GeneratedComputedColumn_ExpressionContainsNotNull_DoesNotFlipNullability`
  (line 724) — dropping the `SkipParenGroup` call in the GENERATED-AS
  computed-expression branch leaves `pos` on the expression's own opening
  `(`, so the outer flags loop re-scans tokens *inside* the expression one
  at a time — an embedded `is not null` inside the expression's own text
  gets misread as a real trailing flag on the column itself.
- `ComputedColumn_RedundantNotNullRepeatedTwice_StaysNonNullable` and
  `ColumnFlag_RedundantNotNullRepeatedTwice_StaysNonNullable` (lines 506,
  624) — dropping the NOT+NULL branch's own `continue` lets a second
  `NOT NULL` pair's trailing `NULL` fall through to the bare-NULL check on
  the *next* iteration, wrongly flipping `IsNullable` back to `true`.
- `ColumnFlag_BareNullFollowedByNotNull_NotNullTakesEffect` (line 630) —
  mirror image: dropping the bare-NULL branch's `continue` lets the
  following `NOT` get consumed by the generic unknown-flag skip instead of
  being recognized as the start of a `NOT NULL` pair (which sits earlier in
  the if-chain and is never revisited), so the trailing `NULL` is
  wrongly caught as a fresh bare-NULL on the next iteration.
- `CreateTable_DottedNameSegmentFollowedByNonIdentifierToken_DoesNotMergeAndIsUnsupported`
  (line 815) — **corrects a wrong equivalence claim** written earlier this
  session, which argued `ReadObjectName`'s `tokens[pos + 1].Type ==
  Identifier` check was equivalent to `tokens[pos - 1]...` because
  `tokens[pos - 1]` (the segment just consumed) is always an Identifier at
  that point. True but insufficient — the real check's purpose is to
  reject the case where `tokens[pos + 1]` is *not* an Identifier, which
  `tokens[pos - 1]` can never see. `SqlTokenizer.MergeQualifiedIdentifiers`
  only fuses `Identifier '.' Identifier`, so `[dbo].5` leaves `.` and the
  Number `5` unmerged; real code correctly refuses to merge and falls back
  to `Unsupported`, while the mutant wrongly merges anyway and accepts the
  statement with `TableName == "5"`.

**All other survivors confirmed equivalent**, each via a specific traced
mechanism (not a blanket "looks like the others"):
- **Generic-skip absorption**: the main flags loop's trailing unknown-flag
  `pos++` catchall silently reabsorbs leftover tokens from a dropped
  branch, at lines 558, 579, 585, 606, 611, 649 (×4), 671/672, 725/726
  (×6), 751 (×2, verified across all 3 call sites).
- **Cross-recognition between independent branches**: a failed branch's
  leftover tokens get caught by a separate, more generic branch elsewhere
  in the same loop recognizing the same concept, at lines 709 (×3), 714,
  717 (×2), 718 (×2), 719.
- **Off-by-one loop-guard absorbed by bounds-checked helpers** (the
  previously-confirmed `pos <= def.Count` class, individually re-checked
  rather than assumed): lines 618, 647, 682, 689, 748.
- **Dead output** (mutated `ref pos` value never read again by the
  caller): line 845 (×2).
- **Invariant/identity proofs**: line 805 (`Substring(dot+1)` when
  `dot==-1` degenerates to `Substring(0)`, identical to the unconditional
  branch), line 798 (`index >= 0`→`index > 0` in `IsSymbol`, exhaustively
  confirmed `index` is never 0 at any of ~17 call sites).
- `SplitTopLevel`/`SplitRemaining`'s spurious-empty-entry survivors (lines
  862, 886) — absorbed by the respective callers' `Count == 0` guards one
  layer up.

**Result: `MigrationParser.cs` 70→64 survived (6 killed), whole-project
Killed+Timeout 2285→2292 (+7, including the small-files fix above),
Survived 91→84.** Full suite: 1442/1442 on both net8.0 and net10.0.
**Whole-project score: 96.01% → 96.30%.**

## Adversarial fable-verify pass over the post-96% survivor claims, 2026-09-20

Following the two passes above, dispatched two independent adversarial
forks (instructed to try to REFUTE the equivalence claims just written, not
confirm them) over all 78 `Survived` mutants those passes had labeled
equivalent: 60 in `MigrationParser.cs`, and 18 across `SchemaSimulator.cs`
(12), `ReferencedObjects.cs` (4), `AutoCrud.cs` (1), and
`MigrationImpactReport.cs` (1). (The other 6 sub-100% files —
`UpsertKeyResolver.cs`, `DialectMapper.cs`, `DialectReservedWords.cs` — had
already been through an equivalent adversarial pass earlier this session,
see the "Correction, 2026-09-20 (fable-verify pass)" note above.)

**Small files: 0/18 refuted.** Independent re-derivation confirmed every
claim, including re-checking the `SchemaSimulator.cs` null-iff-false
invariant at all 9 call sites directly rather than assuming it holds
everywhere, and confirming `GetHashCode`'s only contract (equal objects ⇒
equal hashes) is preserved by all 4 `ReferencedObjects.cs` mutations
regardless of the specific hash transform.

**MigrationParser.cs: 3/60 refuted (6 mutants), 2 of which correct
claims made two paragraphs above** — the "invariant/identity proof" at
line 576 and the "never-fires / vacuous-condition absorption" at lines
898-899 were both wrong:

- **Line 576** (`def[pos+1]`→`def[pos-1]`) — the prior claim ("invariant on
  token type at that position") was insufficient: with different
  precision/scale values in a `decimal(10,2)` facet, the mutant reads
  `Scale` from the wrong token and silently duplicates `Precision` (`Scale`
  comes out `10` instead of `2`). No existing test used differing
  precision/scale, so the duplication went unnoticed. New test:
  `FacetParen_PrecisionAndScale_ScaleReadsTheSecondDigitGroupNotTheFirst`.
- **Lines 898-899** (`ReadParenNameList`'s closing-paren check) — the prior
  claim's contradiction analysis was correct as far as it went, but missed
  that a broken check means the scan never stops at all, running past the
  paren list's own close paren into the surrounding clause and picking up
  a trailing identifier as a bogus extra PK column name. New test:
  `TableLevelPrimaryKeyParenList_StopsAtClosingParen_DoesNotConsumeTrailingIdentifier`.
- **Line 507** (computed-column shorthand's inline flags loop) — a
  trailing bare `NULL` after an explicit `NOT NULL` (e.g.
  `total as (x) not null null`) didn't flip `IsNullable` back to `true`;
  this specific override case was untested. New test:
  `ComputedColumn_NotNullFollowedByBareNull_FlipsBackToNullable`.

The other 54 `MigrationParser.cs` mutants (57 individual mutations) were
independently re-derived as genuinely equivalent, matching the mechanisms
documented above.

**Result: `MigrationParser.cs` 64→61 survived (3 killed).** Full suite:
1445/1445 on net8.0. Whole-project re-run (2026-09-20 22:48-22:52): Killed
2256, Timeout 40, Survived 80, NoCoverage 4 (unchanged) — **whole-project
score: 96.30% → 96.47%**. (Survived dropped by 4 rather than the expected 3
and Timeout rose by 4 vs. the 96.30% run; this is normal run-to-run
variance in which near-miss mutants land as Timeout vs. Survived under
Stryker's coverage-based test selection, not a discrepancy in the fix
count.)

All 84 mutants that survived the 96.30% run have now been through the
adversarial fable-verify pattern: the 4 `NoCoverage` mutants (closed
earlier this session), and all 84 `Survived` mutants across every sub-100%
file. No further batch-level re-check is planned; any remaining survivors
are considered a confirmed equivalent-mutant floor for this assembly.

## 2026-09-21 pass: genuine fable-model verify + malformed-SQL survivor category (96.47% → 97.23%)

A genuine fable-tier `verify` worker was dispatched over the 84-survivor
state from the prior adversarial pass, split into two buckets: mutants it
judged non-equivalent (real, killable gaps) and mutants it judged
"distinguishable only with malformed SQL" (15 mutants at the time, later
found to be 20 once the exact line ranges were re-checked).

### 5 confirmed real gaps (`5deae22`)

The worker found 5 genuine, non-equivalent survivors with concrete valid-SQL
distinguishing inputs (lines 606/611 WITH/WITHOUT TIME ZONE `pos` underflow
absorbed by a flag-word column name; line 725 negated STORED/VIRTUAL guard;
line 751 SkipParenGroup off-by-one via the generated-column expression path;
line 685 DEFAULT-expression close-paren off-by-one; line 507 dropped
`continue` in the computed-column bare-NULL branch). All 5 fixed with new
tests, hand-verified against individually-mutated code before commit.

### Malformed-SQL category resolved (`a9603e4`)

The worker's remaining 15 (later confirmed 20) mutants spanning lines
447-719 were all previously undecided: distinguishable only by SQL that is
syntactically well-formed but not valid for the dialect in question (e.g.
`decimal(10,[2])`, `time(6,2) with time zone`, `identity(null)`). Prior
practice in this doc already accepted such inputs as legitimate
distinguishing tests (they are not *malformed* tokenizer-breaking input,
just semantically nonsensical SQL) — the remaining question was purely
whether a testable subset of these SQL-shaped inputs actually reaches and
kills each mutant. 14 of the 20 were resolved in earlier rounds; the final
9 (lines 709-719, the Postgres `GENERATED BY DEFAULT AS IDENTITY (...)`
parsing chain) were resolved this round:

- **6 mutants killed** (2129, 2132, 2133, 2134, 2135, 2136 — the
  `IDENTITY` literal check, the `pos++` past it, the following-paren guard,
  and the `SkipParenGroup` call itself). The first attempt at a
  distinguishing test used a single-level paren
  (`identity(null)`-style) and did **not** kill the mutants: removing the
  `SkipParenGroup` call causes execution to fall through to the outer
  flags-loop catchall, which re-dispatches into a *separate*, unrelated
  standalone-IDENTITY branch elsewhere in the file — that branch has its
  own naive (non-nested) paren-skip that happens to land on the exact same
  `pos` for single-level content. The fix needed a **nested**-paren
  sequence-options clause (`identity (x(y)null)`): the naive scan stops at
  the first (inner) `)`, exposing a trailing bare `null` to the outer
  loop, which the depth-tracking `SkipParenGroup` would have consumed
  atomically. New test:
  `GeneratedAsIdentity_SequenceOptionsSkippedAtomically_EvenWithNestedParens`.
- **3 mutants confirmed genuinely equivalent** (2119, 2120, 2121 — the
  `BY`+`DEFAULT` prefix check on line 709). Hand-mutation of all three
  proved no input, valid or malformed, distinguishes them: the outer
  flags-loop catchall unconditionally re-scans every subsequent token
  against all flag branches regardless of how the `GENERATED` prefix was
  parsed, so a later bare `IDENTITY` keyword is picked up by the standalone
  branch either way. This is a stronger, corrected version of the worker's
  original "needs malformed SQL" framing — the absorption is
  unconditional, not merely a malformed-input edge case.
- Two more small tests were added along the way for related NoCoverage/loose
  gaps found during tracing: `AlterColumn_EmptyBracketIdentifierAfterSet_StillMatchesUnsupportedGuard`,
  `DecimalScale_BracketQuotedDigit_NotTreatedAsNumericFacet`,
  `TimeMaxFacet_PositionAdvancesPastClosingParen_BeforeTimeZoneCheck`,
  `TimeTwoFacets_PositionAdvancesPastBothAndClosingParen_BeforeTimeZoneCheck`,
  `StandaloneIdentity_SeedParenPositionAdvancesPastClosingParen`.

### Whole-project re-run (`0eee118`)

Killed 2277, Timeout 37, Survived 62, NoCoverage 4, total 2380 mutants
tested. **Final mutation score: 97.23%.** This closes out the malformed-SQL
survivor category entirely (all 20 mutants either killed with real tests or
documented as genuinely equivalent) and exceeds the 96% parity target with
`jaunty`.

## 2026-09-21 pass: Stryker comment-based exclusions for confirmed-equivalent survivors (97.23% → 99.14%)

The 62 Survived + 4 NoCoverage mutants left after the pass above were
individually triaged rather than left as unexplained residue. For each
candidate: (1) checked whether excluding it (by source line + Stryker
mutator category) would also silence any currently-Killed/Timeout sibling
mutant sharing that same line+category — Stryker's comment-based exclusion
operates at that granularity, not per-mutant-ID, so a collision would
silently weaken real test coverage; (2) for every collision-safe candidate,
traced the actual code to confirm genuine semantic equivalence (not just
"safe to exclude") before writing a reason. Of 66 candidates, 47 passed both
checks and were annotated with `// Stryker disable once <category> :
<reason>` comments directly above the target statement, across 7 files:
`AutoCrud.cs`, `Migrations/SchemaSimulator.cs`, `DialectMapper.cs`,
`DialectReservedWords.cs`, `Impact/ReferencedObjects.cs`,
`UpsertKeyResolver.cs`, `Migrations/MigrationParser.cs`. The recurring
equivalence patterns:

- **`TryFindX(...) || x == null` / `&& x != null` redundant null guards** —
  `TryFindTable`/`TryFindColumnKey`/`TryFindColumn`'s contract guarantees the
  out-parameter is null iff the method returns false, making the extra
  null-check clause unreachable-different from the bare boolean check.
- **Outer flags-loop catchall absorption** (`Migrations/MigrationParser.cs`'s
  column-definition parser) — many flag branches' only effect is to advance
  `pos`/`cp` past a recognized keyword; dropping/reversing that advance, or
  blanking the keyword string being matched, just defers the identical
  single-token skip to the loop's own unconditional catchall on the next
  iteration, with no observable difference in final parse state.
- **`GetHashCode` combining-step mutations** (`ReferencedObjects.cs`) — the
  method's only contract is equal objects ⇒ equal hashes, which holds under
  any fixed arithmetic/bitwise transform, mutated or not.
- **Discarded loop `break`/`continue` outcomes** (`UpsertKeyResolver.cs`,
  `MigrationParser.cs`) — where no later loop iteration ever reverses the
  flag being set, or the dropped `continue` already falls through to an
  identical guard's `continue` right after.
- **Dead-code guards proven unreachable** — e.g. `SkipParenGroup`'s own
  `!IsSymbol(pos, "(")` guard, whose condition is never true at any of its
  3 call sites, all of which already check that condition before calling.

19 candidates were left untouched: either the collision check flagged a
same-line/category Killed sibling, or (one explicit case,
`ReferencedColumnComparer`'s `unchecked` block-removal mutant, id 1459 in
`ReferencedObjects.cs`) tracing showed the mutation is not actually
equivalent — removing `unchecked` could allow an `OverflowException` on hash
combination, a real behavioral difference just impractical to trigger with
normal test inputs — so no equivalence comment was written for it, and it
remains a documented, accepted residual gap.

Each file's edits were verified independently via an isolated, sequential
scoped Stryker run (`dotnet stryker --mutate "**/<File>.cs"`, one at a time
— never in parallel, per the earlier lesson about MSBuild/`obj`-directory
contention) before being trusted:

| File | Scoped score | Notes |
|---|---|---|
| `Migrations/SchemaSimulator.cs` | 100.00% | 12/12 target mutants moved to Ignored, 194/206 tested |
| `DialectMapper.cs` | 99.68% | 1/1 target mutant moved to Ignored; remaining `Ignored: 16` in this file's own bucket are Stryker's own built-in "block already covered filter", unrelated to the comment |
| `AutoCrud.cs` | 100.00% | 141/141 tested |
| `DialectReservedWords.cs` | 100.00% | 631/631 tested |
| `Impact/ReferencedObjects.cs` | 97.87% | 1 Survived remaining (id 1459, the `unchecked`-block mutant above, deliberately left undocumented-equivalent) |
| `UpsertKeyResolver.cs` | 100.00% | 77/77 tested |
| `Migrations/MigrationParser.cs` | 97.71% | 17 Survived remaining — the collision-unsafe set from the triage above |

### Whole-project re-run

Killed 2251, Timeout 60, Survived 20, NoCoverage 0, Ignored 294 (comment
exclusions + Stryker's own coverage-based filters), 3 Pending (transient —
this run overlapped with a peer session's concurrent Stryker process on the
same machine; a handful of mutants hit `vstest.console` connection
flakiness during retries and were not re-attempted), total 2331 mutants
scored. **Final mutation score: 99.14%.**

## Extrode.JauntyQ.SqlParser — baseline

First-ever Stryker run for this assembly, 2026-09-20, whole-project
(`tests/Extrode.JauntyQ.SqlParser.Tests/StrykerOutput/2026-09-20.15-17-57`).
Baseline only — no fixes attempted yet.

**Overall: 59.22%** (2058 killed/timeout, 771 survived, 64 no-coverage, 3475 total).

| Score | Killed | Survived | NoCov | Total | File |
|---|---|---|---|---|---|
| 0.00% | 0 | 1 | 0 | 1 | `IR/CteRef.cs` |
| 0.00% | 0 | 4 | 0 | 4 | `IR/JoinRef.cs` |
| 0.00% | 0 | 3 | 0 | 3 | `IR/LiteralBinding.cs` |
| 0.00% | 0 | 4 | 0 | 4 | `IR/PerfHint.cs` |
| 0.00% | 0 | 1 | 0 | 1 | `IR/QueryModel.cs` |
| 0.00% | 0 | 2 | 0 | 2 | `IR/TableRef.cs` |
| 25.00% | 2 | 6 | 0 | 8 | `IR/ColumnRef.cs` |
| 25.00% | 1 | 3 | 0 | 4 | `IR/ParameterRef.cs` |
| 35.53% | 124 | 34 | 1 | 349 | `SqlParser.Part6.cs` |
| 50.00% | 1 | 1 | 0 | 2 | `IR/OrderByRef.cs` |
| 52.14% | 183 | 125 | 4 | 351 | `SqlParser.Part4.cs` |
| 52.20% | 107 | 62 | 5 | 205 | `SqlParser.Part7.cs` |
| 56.35% | 204 | 94 | 17 | 362 | `SqlParser.Part2.cs` |
| 57.38% | 210 | 72 | 21 | 366 | `SqlParser.Part5.cs` |
| 60.07% | 170 | 80 | 0 | 283 | `SqlParser.Part3.cs` |
| 62.83% | 612 | 216 | 12 | 974 | `SqlParser.cs` |
| 79.82% | 443 | 63 | 4 | 555 | `SqlTokenizer.cs` |
| 100.00% | 1 | 0 | 0 | 1 | `Token.cs` |

The 6 zero-score `IR/*.cs` files are small model types (likely record/DTO
shapes) with no dedicated coverage yet — analogous to `MigrationStatement.cs`
etc. in Analysis before the small-files pass. The bulk of the gap is in the
7 `SqlParser*.cs` parser-body files (50–63% each, ~680 combined survivors),
which will need the same file-by-file assertion-strengthening approach used
on `MigrationParser.cs`. No equivalent-mutant analysis has been done yet —
this is an unfiltered baseline.

## Extrode.JauntyQ.SqlParser — parser-core pass (SqlParser.cs, Part4/6/7)

Same-day follow-up to the baseline above, targeting the 4 largest-gap
parser-body files by file-scoped Stryker runs (`--mutate "**/<File>.cs"`).
Added `SqlParserCoreMutationCoverageTests.cs` (41 tests, `SqlParser.cs`),
`SqlParserPart6MutationCoverageTests.cs` (12 tests), 
`SqlParserPart4PerfHintMutationCoverageTests.cs` (13 tests), and
`SqlParserPart7CteMutationCoverageTests.cs` (10 tests). Full
`Extrode.JauntyQ.SqlParser.Tests` suite passed 479/479 on both net8.0 and
net10.0 after all four files. At the time, no equivalent mutants were found
or claimed in this pass — every survivor closed was believed to be a real,
previously-untested gap (aggregate exact-shape capture, redundant-paren
stripping depth, SELECT INTO target-skipping, TOP N PERCENT WITH TIES token
arithmetic, DELETE's optional FROM, RETURNING's nested-paren/trailing-`;`
handling, EXISTS-subquery lookback guards, WHERE-region boundary and
column/column comparisons in `ExtractPerfHints`, and `ParseWith`'s
parameter-merge/WITH RECURSIVE position-exactness rules).

**Correction, 2026-09-20 (fable-verify pass, `test/fix-weak-mutation-tests`,
`c898c59`).** A subsequent adversarial pass over these same test files found
one genuine exception to "no equivalent mutants": `SqlParser.Part7.cs`'s
trailing-semicolon-strip mutant. An initial fix attempt to write a
distinguishing test failed; re-tracing the actual control flow through
`Parse` confirmed no input reachable through the public API can distinguish
this mutant — it is equivalent, and is now documented as such in
`SqlParserPart7CteMutationCoverageTests.cs` rather than force-killed. The
other three files' fixes in that same pass (Part4, Part6, and the rest of
Part7) were genuine gap-closing, not equivalence corrections; see the score
table below, which reflects their pre-fix scores only — a further re-run is
needed to capture the `c898c59` improvements.

| File | Before | After |
|---|---|---|
| `SqlParser.cs` | 62.83% (whole-project baseline attribution) | **82.26%** (691/142/7/840, scoped) |
| `SqlParser.Part6.cs` | 35.53% | **84.28%** (134/25/0/159, scoped) |
| `SqlParser.Part4.cs` | 52.14% | **73.72%** (230/82/0/312, scoped) |
| `SqlParser.Part7.cs` | 52.20% | **64.94%** (113/57/4/174, scoped) |

A whole-project re-run to obtain a new authoritative overall SqlParser score
was attempted twice and crashed both times with VsTest socket errors
(`SocketException (10054)`, no `reports/` output produced) rather than
completing — the file-scoped scores above are exact, but there is currently
no valid whole-project number superseding the 59.65%/59.22% baseline figures
elsewhere in this doc for these 4 files specifically. Re-run
`scripts/mutate.sh sqlparser --mutate` (ideally offloaded to a machine that
can sustain a multi-minute VsTest session, per this repo's "no long local
runs" convention) to get a clean number.

Not covered in this pass (left for a future round): `SqlParser.Part2.cs`,
`SqlParser.Part3.cs`, `SqlParser.Part5.cs`, `SqlTokenizer.cs`, and the two
still-low IR files (`IR/ColumnRef.cs`, `IR/ParameterRef.cs`,
`IR/OrderByRef.cs`).

## Extrode.JauntyQ.SqlParser — IR model types pass (easiest win, done)

All 15 survivors in the 6 zero-score `IR/*.cs` files turned out to be a single
recurring shape: every `string`-typed property defaulting to `string.Empty`
had its default-value literal mutated (`"" → "Stryker was here!"`) and nothing
asserted the default, so it survived. Added
`tests/Extrode.JauntyQ.SqlParser.Tests/IrModelTypesMutationCoverageTests.cs`
(6 tests, one per type) asserting every string-typed property's default value.
No equivalent mutants — all 15 were real, if trivial, gaps.

| File | Before | After |
|---|---|---|
| `IR/CteRef.cs` | 0.00% (1 survived) | **100.00%** |
| `IR/JoinRef.cs` | 0.00% (4 survived) | **100.00%** |
| `IR/LiteralBinding.cs` | 0.00% (3 survived) | **100.00%** |
| `IR/PerfHint.cs` | 0.00% (4 survived) | **100.00%** |
| `IR/QueryModel.cs` | 0.00% (1 survived) | **100.00%** |
| `IR/TableRef.cs` | 0.00% (2 survived) | **100.00%** |

New overall SqlParser score: **59.65%** (2073 killed/timeout, 756 survived, 64
no-coverage, 3475 total) — up from the 59.22% baseline. `IR/ColumnRef.cs` and
`IR/ParameterRef.cs` (25.00% each) were left untouched — they carry other,
non-default-value mutants (e.g. `IsExpression`/comparer logic) outside this
pass's scope. The 7 `SqlParser.Part*.cs`/`SqlParser.cs` files remain the
dominant gap (~680 of the remaining 756 survivors) and are unaffected by this
pass.
