# Mutation coverage tally vs. target

Per-assembly, per-file mutation score, tallied against the 96% target set for
`Extrode.JauntyQ.Analysis` (parity with sibling repo `jaunty`'s ~96% baseline).
Last confirmed whole-project `Extrode.JauntyQ.Analysis` run: **99.14%**
(2026-09-21 08:02-08:32, pending commit) — a comment-based Stryker exclusion
pass over 47 confirmed-equivalent survivors across 7 files, see the
"2026-09-21 pass: Stryker comment-based exclusions" section below and in
mutation-coverage-report.md. This followed **97.23%**
(2026-09-21 02:14-02:19, commit `0eee118`) — target exceeded. That followed
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
own trace. `Extrode.JauntyQ.SqlParser`'s earlier figures (59.22% baseline, 59.65% after
the IR-model cleanup, a 4-file parser-core pass with no confirmed
whole-project number) are now superseded: a 2026-09-21 pass established a
correct fresh whole-project baseline (**76.98%**) and then pushed all 10
survivor-carrying files through the same real-test + individually-traced
equivalence-exclusion discipline as Analysis above, closing 3 IR files to
100% and improving the 7 parser-body files (`SqlParser.cs` 83.41%,
`SqlParser.Part2.cs` 78.03%, `SqlParser.Part5.cs`, `SqlParser.Part3.cs`
100.00%, `SqlParser.Part4.cs` 85.61%, `SqlTokenizer.cs` 99.80%,
`SqlParser.Part7.cs` 90.24%, `SqlParser.Part6.cs` 94.16%). Final whole-project
re-run: **88.53%** (Killed 2216, Timeout 162, Survived 289, NoCoverage 19) —
short of the 96% target. The remaining gap is dominated by one recurring
survivor class (the `pos < tokens.Count && tokens[pos].Type == X` guard-chain
idiom and variants, repeated across nearly every parser-body file), deferred
honestly rather than force-excluded in every file's pass because proving
equivalence requires per-call-site tracing of each caller's loop-exit
behavior — out of budget for a single-file pass. **Flagged for a dedicated
follow-up session** specifically tracing those guard-chain call sites
file-by-file, expected to take a similar scale of effort to this whole pass.
See the "2026-09-21 push" section in mutation-coverage-report.md for the full
per-file breakdown. For score-history-over-time and
equivalent-mutant reasoning, see [`mutation-coverage-report.md`](mutation-coverage-report.md)
and [`../handoffs/2026-09-19-stryker-mutation-gaps.md`](../handoffs/2026-09-19-stryker-mutation-gaps.md).

## Assemblies covered by Stryker

Only 2 of 8 `src/` assemblies have a Stryker config at all; both have been
run this effort.

| Assembly | Stryker config | Ever run this effort | Target | Actual | Δ to target |
|---|---|---|---|---|---|
| `Extrode.JauntyQ.Analysis` | `tests/Extrode.JauntyQ.Analysis.Tests/stryker-config.json` | Yes | 96.00% | **99.14%** | **+3.14 pp (target exceeded)** |
| `Extrode.JauntyQ.SqlParser` | `tests/Extrode.JauntyQ.SqlParser.Tests/stryker-config.json` | Yes | 96.00% | **88.53%** | **-7.47 pp (follow-up flagged)** |
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
| **Tally (19 files)** | **2311*** | — | **20** | **0** | **2331** | **99.14%** | **+3.14 pp (target exceeded)** |

**2026-09-21 update (comment-based exclusions):** whole-project re-run after adding `// Stryker disable once` comments for 47 confirmed-equivalent survivors across 7 files (pending commit): Killed 2251, Timeout 60, Survived 20, NoCoverage 0, Ignored 294, 3 Pending (transient — this run overlapped with a peer session's concurrent Stryker process), total scored 2331 — **99.14%**, up from 97.23%. Note the denominator dropped from 2380 to 2331 because the 47 newly-excluded mutants no longer count toward the score at all (Stryker's `Ignored` status is excluded from the formula), not because they were "fixed" as Killed. See mutation-coverage-report.md's "2026-09-21 pass: Stryker comment-based exclusions" section for the full per-mutant reasoning and per-file verification runs.

**2026-09-21 update (earlier same day):** whole-project re-run (`0eee118`) after the fable-verify 5-gap fix and the malformed-SQL survivor category resolution: Killed 2277, Timeout 37, Survived 62, NoCoverage 4, total 2380 — **97.23%**, up from 96.47%. See mutation-coverage-report.md's "2026-09-21 pass" section for the per-mutant detail.

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

- **`Extrode.JauntyQ.Analysis` overall: 99.14% vs. 96% target — target
  exceeded, confirmed by a fresh whole-project run (2026-09-21 08:02-08:32,
  commit `bb177ce`), after adding
  Stryker comment-based exclusions for 47 confirmed-equivalent survivors
  across 7 files (`AutoCrud.cs`, `Migrations/SchemaSimulator.cs`,
  `DialectMapper.cs`, `DialectReservedWords.cs`,
  `Impact/ReferencedObjects.cs`, `UpsertKeyResolver.cs`,
  `Migrations/MigrationParser.cs`) — up from 97.23%, confirmed by an earlier
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
- `Extrode.JauntyQ.SqlParser`: the 2026-09-20 figures above (baseline 59.22%,
  IR-model cleanup 59.65%, 4-file parser-core pass with no confirmed
  whole-project number) are superseded. A 2026-09-21 pass established a fresh,
  correct whole-project baseline (**76.98%**) then pushed all 10
  survivor-carrying files through real-test + individually-traced
  equivalence-exclusion passes — see the per-file tally below, now drawn
  from a single coherent whole-project run
  (`tests/Extrode.JauntyQ.SqlParser.Tests/StrykerOutput/2026-09-21.13-28-05`),
  not a mixed-date composite.

## Extrode.JauntyQ.SqlParser — per-file tally vs. 96% target

Whole-project run 2026-09-21 13:28-13:38, **88.53%** overall (Killed 2216,
Timeout 162, Survived 289, NoCoverage 19, 2686 total scored).

| File | Killed+Timeout | Survived | NoCov | Total | Score | vs. 96% |
|---|---|---|---|---|---|---|
| `SqlParser.Part2.cs` | 239 | 57 | 9 | 305 | 78.36% | -17.64 pp |
| `SqlParser.cs` | 699 | 131 | 8 | 838 | 83.41% | -12.59 pp |
| `SqlParser.Part4.cs` | 238 | 40 | 0 | 278 | 85.61% | -10.39 pp |
| `SqlParser.Part5.cs` | 255 | 39 | 2 | 296 | 86.15% | -9.85 pp |
| `SqlParser.Part7.cs` | 111 | 12 | 0 | 123 | 90.24% | -5.76 pp |
| `SqlParser.Part6.cs` | 145 | 9 | 0 | 154 | 94.16% | -1.84 pp |
| `SqlTokenizer.cs` | 509 | 1 | 0 | 510 | 99.80% | **+3.80 pp** |
| `IR/CteRef.cs`, `IR/QueryModel.cs`, `Token.cs`, `IR/OrderByRef.cs`, `IR/TableRef.cs`, `IR/LiteralBinding.cs`, `IR/JoinRef.cs`, `IR/ParameterRef.cs`, `IR/PerfHint.cs`, `IR/ColumnRef.cs`, `SqlParser.Part3.cs` | 178 | 0 | 0 | 178 | 100.00% | **+4.00 pp** |
| **Tally (18 files, single coherent whole-project run)** | **2374** | **289** | **19** | **2682** | **88.51%*** | **-7.49 pp** |

*The 88.51% tally-row figure differs from the JSON's own reported 88.53% by
a fraction of a point due to 4 mutants (`CompileError`/other non-scored
statuses) landing in different per-file buckets than the whole-project
denominator — both numbers point at the same result; treat 88.53% as
authoritative.

3 tiny IR files (`IR/ParameterRef.cs`, `IR/ColumnRef.cs`, `IR/OrderByRef.cs`)
closed to 100.00% via new default-value tests. `SqlParser.Part3.cs` also
reached 100.00% — 45 real tests plus ~15 equivalence-exclusion comments
(collision-checked, covering ~103 individual mutants) resting on the
tokenizer's trailing `TokenType.End` sentinel invariant. The other 6
parser-body files improved substantially (78–94%) but each still has a
meaningful deferred set — dominated by one recurring pattern, the
`pos < tokens.Count && tokens[pos].Type == X` guard-chain idiom, left
plain/undocumented (not falsely claimed equivalent) because proving
equivalence needs per-call-site tracing of each caller's own loop-exit
behavior, judged out of budget for a single-file pass every time it came up.
**Flagged for a dedicated follow-up session** to close the remaining ~7.5pp
gap by tracing that pattern file-by-file — see the "2026-09-21 push" section
in mutation-coverage-report.md for the full file-by-file real-test vs.
equivalence-exclusion vs. deferred breakdown.
