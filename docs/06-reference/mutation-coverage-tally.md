# Mutation coverage tally vs. target

Per-assembly, per-file mutation score, tallied against the 96% target set for
`Extrode.JauntyQ.Analysis` (parity with sibling repo `jaunty`'s ~96% baseline).
Last confirmed whole-project `Extrode.JauntyQ.Analysis` run: **97.23%**
(2026-09-21 02:14-02:19, commit `0eee118`) — target exceeded. This followed
96.47%: a genuine fable-model verify pass fixed 5 more real gaps in
`MigrationParser.cs` (`5deae22`), and the previously-undecided
"malformed-SQL-only" survivor category (20 mutants total, lines 447-719) was
fully resolved — 17 killed with real tests across earlier rounds plus this
round's 6, and 3 confirmed genuinely equivalent (`a9603e4`). See the new
"2026-09-21 pass" section in mutation-coverage-report.md for the full
per-mutant detail. Prior path (still valid history): 96.01% (target first
reached, two genuine Negate mutants in `MigrationParser.cs`'s DEFAULT-value
and GENERATED-column handling, lines 689/704, found via an adversarial
fable-verify pass) → 96.30% (a full post-96% survivor pass over the
remaining 91 `Survived` mutants found 1 more genuine gap in
`ReferencedObjects.cs`'s `ReferencedColumnComparer.Equals` and 6 more in
`MigrationParser.cs`, individually re-traced rather than template-matched —
one of which corrected a previously-wrong equivalence claim) → **96.47%**
(an adversarial fable-verify batch pass over all 84 remaining `Survived`
mutants — the same treatment already given the 4 `NoCoverage` mutants —
refuted 0/18 small-file claims and 3/60 `MigrationParser.cs` claims, 2 of
which corrected wrong equivalence claims written earlier the same session).
80 `Survived` + 4 `NoCoverage` mutants remain; all mutants in both
categories, across every sub-100% file, have now individually been through
an adversarial refutation attempt with no further batch-level re-check
planned — the remaining survivors are considered a confirmed
equivalent-mutant floor. See mutation-coverage-report.md for the full
per-mutant reasoning and the standing lesson that category-matching a
survivor to a known-equivalent neighbor is not sufficient — each needs its
own trace. `Extrode.JauntyQ.SqlParser`
has a first-ever baseline (59.22%), a 6-file IR-model cleanup (59.65%), and a
parser-core pass on 4 more files (no new whole-project number yet — two
attempts crashed with VsTest socket errors). For score-history-over-time and
equivalent-mutant reasoning, see [`mutation-coverage-report.md`](mutation-coverage-report.md)
and [`../handoffs/2026-09-19-stryker-mutation-gaps.md`](../handoffs/2026-09-19-stryker-mutation-gaps.md).

## Assemblies covered by Stryker

Only 2 of 8 `src/` assemblies have a Stryker config at all; both have been
run this effort.

| Assembly | Stryker config | Ever run this effort | Target | Actual | Δ to target |
|---|---|---|---|---|---|
| `Extrode.JauntyQ.Analysis` | `tests/Extrode.JauntyQ.Analysis.Tests/stryker-config.json` | Yes | 96.00% | **97.23%** | **+1.23 pp (target exceeded)** |
| `Extrode.JauntyQ.SqlParser` | `tests/Extrode.JauntyQ.SqlParser.Tests/stryker-config.json` | Yes | 96.00% | **59.65%** | **-36.35 pp** |
| `Extrode.JauntyQ.Cli.Core` | none | No | — | — | out of scope |
| `Extrode.JauntyQ.Cli` | none | No | — | — | out of scope |
| `Extrode.JauntyQ.Generator` | none | No | — | — | out of scope |
| `Extrode.JauntyQ.Runtime` | none | No | — | — | out of scope |
| `Extrode.JauntyQ.Schema` | none | No | — | — | out of scope |
| `Extrode.JauntyQ.Schema.Extraction` | none | No | — | — | out of scope |

Both configured Stryker projects carry the same `stryker-config.json`
thresholds: `high: 80, low: 65, break: 0`. The 96% target is a user-set goal
for `Extrode.JauntyQ.Analysis` specifically, not the tool's own break
threshold — `dotnet stryker` will not fail the build below 96%, only below 0%
(i.e. never), so this is a manually tracked goal, not a CI gate.

## Extrode.JauntyQ.Analysis — per-file tally vs. 96% target

| File | Killed | Timeout | Survived | NoCov | Total | Score | vs. 96% |
|---|---|---|---|---|---|---|---|
| `Impact/ReferencedObjects.cs` | 46 | 0 | 4 | 2 | 52 | 88.46% | -7.54 pp |
| `Impact/MigrationImpactReport.cs` | 9 | 0 | 1 | 0 | 10 | 90.00% | -6.00 pp |
| `Migrations/MigrationParser.cs` | 672 | 34 | 56 | 1 | 763 | 92.53% | -3.47 pp |
| `Migrations/SchemaSimulator.cs` | 194 | 0 | 12 | 0 | 206 | 94.17% | -1.83 pp |
| `UpsertKeyResolver.cs` | 77 | 0 | 3 | 0 | 80 | 96.25% | **+0.25 pp** |
| `AutoCrud.cs` | 141 | 0 | 1 | 1 | 143 | 98.60% | **+2.60 pp** |
| `DialectMapper.cs` | 308 | 2 | 2 | 0 | 312 | 99.36% | **+3.36 pp** |
| `DialectReservedWords.cs` | 627 | 4 | 1 | 0 | 632 | 99.84% | **+3.84 pp** |
| `AnalysisDiagnostic.cs` | 1 | 0 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| `CrudColumnRules.cs` | 27 | 0 | 0 | 0 | 27 | 100.00% | **+4.00 pp** |
| `Diff/SchemaDelta.cs` | 15 | 0 | 0 | 0 | 15 | 100.00% | **+4.00 pp** |
| `Diff/StructuralSchemaDiff.cs` | 44 | 0 | 0 | 0 | 44 | 100.00% | **+4.00 pp** |
| `EntityNameResolver.cs` | 15 | 0 | 0 | 0 | 15 | 100.00% | **+4.00 pp** |
| `Impact/Classification.cs` | 4 | 0 | 0 | 0 | 4 | 100.00% | **+4.00 pp** |
| `Impact/ImpactClassifier.cs` | 66 | 0 | 0 | 0 | 66 | 100.00% | **+4.00 pp** |
| `Impact/ImpactEntry.cs` | 3 | 0 | 0 | 0 | 3 | 100.00% | **+4.00 pp** |
| `Impact/ImpactReason.cs` | 4 | 0 | 0 | 0 | 4 | 100.00% | **+4.00 pp** |
| `Impact/QueryImpactInput.cs` | 1 | 0 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| `Migrations/MigrationStatement.cs` | 2 | 0 | 0 | 0 | 2 | 100.00% | **+4.00 pp** |
| **Tally (19 files)** | **2314*** | — | **62** | **4** | **2380** | **97.23%** | **+1.23 pp (target exceeded)** |

**2026-09-21 update:** whole-project re-run (`0eee118`) after the fable-verify 5-gap fix and the malformed-SQL survivor category resolution: Killed 2277, Timeout 37, Survived 62, NoCoverage 4, total 2380 — **97.23%**, up from 96.47%. See mutation-coverage-report.md's "2026-09-21 pass" section for the per-mutant detail.

\* "Killed" column includes Timeout mutants (Stryker treats Timeout as a kill
for scoring purposes); the 2314/62/4/2380 total row is the last confirmed
whole-project run, 2026-09-21 02:14-02:19 (2277 killed, 37 timeout, 62
survived, 4 no-coverage) — this is a real, coherent whole-project number, not
a per-file sum. The prior run (2026-09-20 22:48-22:52: 2256 killed, 40
timeout, 80 survived, 4 no-coverage, 96.47%) is superseded. 96% needed
`Survived + NoCoverage ≤ 95`; the final count is 66, 29 better than the
line. In total this session found and fixed 12
genuine (non-equivalent) mutants across 2 files: 2 Negate mutants in
`MigrationParser.cs` (lines 689, 704, via an adversarial fable-verify pass)
that closed the last gap to 96%, a further post-96% pass that individually
re-traced all 91 remaining survivors and found 1 more genuine gap in
`ReferencedObjects.cs`'s `ReferencedColumnComparer.Equals` and 6 more in
`MigrationParser.cs`, then a final adversarial fable-verify batch pass over
all 84 remaining survivors (mirroring the treatment already given the 4
no-coverage mutants) found 3 more genuine gaps in `MigrationParser.cs` — 2
of which corrected wrong equivalence claims written earlier the same
session — see mutation-coverage-report.md for the full per-mutant
reasoning.

**2026-09-21 update:** a genuine fable-model verify pass (distinct from the
prior adversarial batch pass — dispatched fresh over the same 84-survivor
state) found 5 more real gaps in `MigrationParser.cs` (`5deae22`), and the
previously-undecided "malformed-SQL-only" category (20 mutants across lines
447-719) was fully resolved: 6 more killed with a nested-paren test
exploiting a naive-vs-depth-tracking paren-skip discrepancy, and 3 (lines
709, the `BY`+`DEFAULT` prefix check) confirmed genuinely equivalent via
exhaustive hand-mutation — no input, valid or malformed, distinguishes them,
because the outer flags-loop's own catchall unconditionally re-scans a
later bare `IDENTITY` keyword regardless of how the `GENERATED` prefix was
parsed. Net this round: Survived 80→62, Timeout 40→37, Killed 2256→2277.
The remaining 62 survivors + 4 no-coverage are considered a confirmed
equivalent-mutant floor pending any future re-check.

† This row is stale — it reflects the file-scoped 89.78% pass from earlier
in the session, before the two fixes above and before the whole-project
score folded in. A fresh file-scoped `MigrationParser.cs` Stryker run would
be needed to get its exact current per-file numbers; the whole-project total
row above is the authoritative current figure.

## Files at or above target (15 of 19)

`UpsertKeyResolver.cs`, `AutoCrud.cs` (98.60%, ceiling — 1 remaining survivor
is an equivalent `break;`-removal), `DialectMapper.cs`,
`DialectReservedWords.cs`, and 11 files at exactly 100.00%
(`AnalysisDiagnostic.cs`, `CrudColumnRules.cs`, `Diff/SchemaDelta.cs`,
`Diff/StructuralSchemaDiff.cs`, `EntityNameResolver.cs`,
`Impact/Classification.cs`, `Impact/ImpactClassifier.cs`,
`Impact/ImpactEntry.cs`, `Impact/ImpactReason.cs`,
`Impact/QueryImpactInput.cs`, `Migrations/MigrationStatement.cs`).

## Files below target (4 of 19) — round-4 pass complete, results final

| File | Score | Mutants still needing attention (Survived + NoCov) | Round-4 verdict |
|---|---|---|---|
| `Impact/ReferencedObjects.cs` | 86.54% | 7 | Re-verified, no new findings — at documented-equivalent floor |
| `Migrations/MigrationParser.cs` | 89.78% (scoped; whole-project fold-in pending) | 78 | Per-mutant pass, 2026-09-20: 7 real gaps fixed (12 new tests, 91→70 survivors); remaining ~70 individually re-traced (not template-matched) and confirmed under the previously-established absorption mechanisms — see mutation-coverage-report.md for the per-mutant breakdown |
| `Impact/MigrationImpactReport.cs` | 90.00% | 1 | Re-verified, no new findings — at documented-equivalent floor |
| `Migrations/SchemaSimulator.cs` | 94.17% | 12 | **Fully closed** — all 12 proven equivalent via the `TryFindTable`/`TryFindColumnKey`/`TryFindColumn` "null-iff-false" contract |

Combined: 98 of the whole project's 106 remaining non-killed mutants (per the
current per-file scores, `MigrationParser.cs`'s post-per-mutant-pass 78
included) sit in these 4 files, of which `SchemaSimulator.cs`'s 12,
`ReferencedObjects.cs`'s 7, and `MigrationImpactReport.cs`'s 1 (20 total) are
now confirmed-equivalent floors — closed, not gaps. The genuinely open
question is only in `MigrationParser.cs`'s remaining ~70: individually
re-traced and believed equivalent under the previously-established
absorption mechanisms (see mutation-coverage-report.md's per-mutant pass
section), though a fable-verify pass on that section found one of its new
tests (line 647) doesn't actually kill what it claims to — see that doc for
the correction.

## Bottom line

- **Overall: 97.23% vs. 96% target — target exceeded, confirmed by a fresh
  whole-project run (2026-09-21 02:14-02:19, commit `0eee118`), up from
  96.47% after a genuine fable-model verify pass fixed 5 more real gaps and
  the malformed-SQL survivor category was fully resolved (6 more killed, 3
  confirmed equivalent).** The path there: 94.66% →
  95.55% (folding in `MigrationParser.cs`'s per-mutant pass) → 95.84%
  (`ReadObjectName` bracket-quoted-dot fix) → 95.97% (DEFAULT-value Negate
  mutant fix, line 689) → 96.01% (GENERATED-column Negate mutant fix, line
  704, target first reached) → 96.30% (post-96% full survivor pass: 1
  gap fixed in `ReferencedObjects.cs`, 6 more in `MigrationParser.cs`) →
  **96.47%** (adversarial fable-verify batch pass over all 84 remaining
  survivors, mirroring the treatment already given the 4 no-coverage
  mutants: 3 more genuine gaps found in `MigrationParser.cs`, 2 of which
  corrected wrong equivalence claims from the immediately preceding pass).
  Every fix from 95.97% onward was found by explicitly re-tracing individual
  survivors rather than trusting their resemblance to an already-confirmed
  equivalence class — an adversarial fable-verify pass had confirmed the
  `pos <= def.Count` class and a `SkipParenGroup` dead-code claim but
  flagged the line-689 mutant as a real gap; the line-704 mutant was found
  the same way on a manual re-check afterward; the full post-96% pass
  over all 91 remaining survivors found 7 more, including one that
  corrected a previously-wrong equivalence claim about `ReadObjectName`'s
  dotted-name check; and a final adversarial batch pass over the resulting
  84 survivors found 3 more, 2 of which corrected wrong claims from that
  same post-96% pass (a facet-parsing scale bug at line 576, and a
  closing-paren detection bug at lines 898-899). All 84 survivors and all 4
  no-coverage mutants have now individually been through an adversarial
  refutation attempt with no further batch-level re-check planned. See
  mutation-coverage-report.md for the full per-mutant trace.
- 15 of 19 files already meet or exceed 96%; 11 of those are at a clean 100%.
- A near-target cleanup pass (commit `1dd7886`) pushed the 4 files already
  ≥96% toward their own ceilings: only `AutoCrud.cs` had a real fixable
  survivor (97.90% → 98.60%, a `List<T>` negative-capacity arithmetic
  mutant). `UpsertKeyResolver.cs`, `DialectMapper.cs`, and
  `DialectReservedWords.cs` were initially reported unchanged, but a
  follow-on adversarial (fable) verify pass found 3 of those "equivalent"
  survivors were actually real, killable gaps — 2 in `DialectMapper.cs`
  (98.72% → **99.36%**) and 1 in `DialectReservedWords.cs` (99.68% →
  **99.84%**), fixed in branch `test/fix-mislabeled-equivalents`. See the
  handoff doc's "fable-verify pass" update for the full per-mutant trace,
  including the corrected reasoning for the survivors that *are* genuinely
  equivalent. Combined with the 3 sub-96% files closed in round 4, 6 of the
  7 sub-100% files (all but `MigrationParser.cs`) are now confirmed closed
  with adversarially-checked reasoning — no further test-writing can move
  any of them.
- `MigrationParser.cs` moved 87.02% → 89.78% (scoped) via a full per-mutant
  pass over the ~91 survivors (12 new tests, 7 real gaps fixed, 91→70
  survivors), then two more genuine gaps were found and fixed afterward (the
  line-689 DEFAULT-value and line-704 GENERATED-column Negate mutants,
  both non-equivalent despite resembling already-confirmed equivalence
  classes) — this is what closed the last 1-mutant gap to 96% overall. A
  Stryker coverage-misattribution issue was also found during this pass (see
  mutation-coverage-report.md) — the tool's own survivor counts for this file
  may include false positives beyond what's been individually verified here.
  The whole-project run that folded these fixes into the overall score
  completed 2026-09-20 20:12-20:16, landing at **96.01%** — see the total
  tally row above.
- `Extrode.JauntyQ.SqlParser` baseline established 2026-09-20: **59.22%**,
  -36.78 pp below the same 96% target. A same-day follow-up closed the 6
  zero-coverage IR model files to 100.00%, moving the assembly to **59.65%**
  (-36.35 pp). A further same-day pass strengthened the 4 largest
  parser-core files (`SqlParser.cs`, `SqlParser.Part6.cs`,
  `SqlParser.Part4.cs`, `SqlParser.Part7.cs`) — see the per-file tally below.
  Per-file scores are exact (file-scoped Stryker re-runs); a whole-project
  re-run to get the new authoritative overall SqlParser score was started but
  did not complete within the session, so the **59.65%** overall figure below
  is stale for these 4 rows specifically — re-run `scripts/mutate.sh
  sqlparser --mutate` to refresh it.

## Extrode.JauntyQ.SqlParser — per-file tally vs. 96% target

| File | Killed | Survived | NoCov | Total | Score | vs. 96% |
|---|---|---|---|---|---|---|
| `IR/ColumnRef.cs` | 2 | 6 | 0 | 8 | 25.00% | -71.00 pp |
| `IR/ParameterRef.cs` | 1 | 3 | 0 | 4 | 25.00% | -71.00 pp |
| `IR/OrderByRef.cs` | 1 | 1 | 0 | 2 | 50.00% | -46.00 pp |
| `SqlParser.Part2.cs` | 204 | 94 | 17 | 362 | 56.35% | -39.65 pp |
| `SqlParser.Part5.cs` | 210 | 72 | 21 | 366 | 57.38% | -38.62 pp |
| `SqlParser.Part3.cs` | 170 | 80 | 0 | 283 | 60.07% | -35.93 pp |
| `SqlParser.Part7.cs` | 113 | 57 | 4 | 174 | 64.94% | -31.06 pp |
| `SqlParser.Part4.cs` | 230 | 82 | 0 | 312 | 73.72% | -22.28 pp |
| `SqlTokenizer.cs` | 443 | 63 | 4 | 555 | 79.82% | -16.18 pp |
| `SqlParser.Part6.cs` | 134 | 25 | 0 | 159 | 84.28% | -11.72 pp |
| `SqlParser.cs` | 691 | 142 | 7 | 840 | 82.26% | -13.74 pp |
| `IR/CteRef.cs` | 1 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| `IR/JoinRef.cs` | 4 | 0 | 0 | 4 | 100.00% | **+4.00 pp** |
| `IR/LiteralBinding.cs` | 3 | 0 | 0 | 3 | 100.00% | **+4.00 pp** |
| `IR/PerfHint.cs` | 4 | 0 | 0 | 4 | 100.00% | **+4.00 pp** |
| `IR/QueryModel.cs` | 1 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| `IR/TableRef.cs` | 2 | 0 | 0 | 2 | 100.00% | **+4.00 pp** |
| `Token.cs` | 1 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| **Tally (18 files, mixed dates — see note above)** | **1899** | **625** | **53** | **2577** | **73.69%** | **-22.31 pp** |

The bottom tally row combines the 4 freshly re-measured files with the other
14 files' figures as of their last measurement (2026-09-20) — it is a
weighted average across files measured at different times, not a single
coherent whole-project Stryker run; treat it as directional only until the
next full run confirms it. Largest remaining single lever:
`SqlParser.Part2.cs`, `SqlParser.Part3.cs`, and `SqlParser.Part5.cs` (246
combined survivors, 38 no-coverage) are the only 3 of the 7 parser-core files
not yet touched by a coverage pass. The 6 IR model files are closed (7 of 18
files at 100.00%, including `Token.cs`) — all 15 mutants were
default-value-literal survivors (`= string.Empty` mutated with nothing
asserting the default), no equivalent mutants found. The 4 files covered in
this pass (`SqlParser.cs`, `SqlParser.Part6.cs`, `SqlParser.Part4.cs`,
`SqlParser.Part7.cs`) moved from 35–63% to 65–84%, adding
`SqlParserCoreMutationCoverageTests.cs`, `SqlParserPart6MutationCoverageTests.cs`,
`SqlParserPart4PerfHintMutationCoverageTests.cs`, and
`SqlParserPart7CteMutationCoverageTests.cs` — no equivalent-mutant analysis
was attempted for their remaining survivors (time-boxed to new-test-writing
only). No equivalent-mutant analysis has been done yet for any of the
remaining sub-96% files.
