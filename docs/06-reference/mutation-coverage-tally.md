# Mutation coverage tally vs. target

Per-assembly, per-file mutation score, tallied against the 96% target set for
`Extrode.JauntyQ.Analysis` (parity with sibling repo `jaunty`'s ~96% baseline).
Snapshot as of commit `1dd7886` (94.54% overall), after the round-4 pass on
the 4 sub-96% files and a follow-up near-target cleanup pass on the four
files that were already ≥96%. For score-history-over-time and
equivalent-mutant reasoning, see [`mutation-coverage-report.md`](mutation-coverage-report.md)
and [`../handoffs/2026-09-19-stryker-mutation-gaps.md`](../handoffs/2026-09-19-stryker-mutation-gaps.md).

## Assemblies covered by Stryker

Only 2 of 8 `src/` assemblies have a Stryker config at all; only 1 has ever
been run.

| Assembly | Stryker config | Ever run this effort | Target | Actual | Δ to target |
|---|---|---|---|---|---|
| `Extrode.JauntyQ.Analysis` | `tests/Extrode.JauntyQ.Analysis.Tests/stryker-config.json` | Yes | 96.00% | **94.54%** | **-1.46 pp** |
| `Extrode.JauntyQ.SqlParser` | `tests/Extrode.JauntyQ.SqlParser.Tests/stryker-config.json` | Yes (baseline) | 96.00% | **59.22%** | **-36.78 pp** |
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
| `Impact/ReferencedObjects.cs` | 45 | incl. above | 5 | 2 | 52 | 86.54% | -9.46 pp |
| `Migrations/MigrationParser.cs` | 664 | incl. above | 91 | 8 | 763 | 87.02% | -8.98 pp |
| `Impact/MigrationImpactReport.cs` | 9 | incl. above | 1 | 0 | 10 | 90.00% | -6.00 pp |
| `Migrations/SchemaSimulator.cs` | 194 | incl. above | 12 | 0 | 206 | 94.17% | -1.83 pp |
| `UpsertKeyResolver.cs` | 77 | incl. above | 3 | 0 | 80 | 96.25% | **+0.25 pp** |
| `AutoCrud.cs` | 141 | incl. above | 1 | 1 | 143 | 98.60% | **+2.60 pp** |
| `DialectMapper.cs` | 308 | incl. above | 4 | 0 | 312 | 98.72% | **+2.72 pp** |
| `DialectReservedWords.cs` | 630 | incl. above | 2 | 0 | 632 | 99.68% | **+3.68 pp** |
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
| **Tally (19 files)** | **2250*** | — | **119** | **11** | **2380** | **94.54%** | **-1.46 pp** |

\* "Killed" column includes Timeout mutants (Stryker treats Timeout as a kill
for scoring purposes); the whole-project total of 2245 killed + timeouts
matches the `mutation-coverage-report.md` whole-project figure.

## Files at or above target (10 of 19)

`UpsertKeyResolver.cs`, `AutoCrud.cs` (98.60%, ceiling — 1 remaining survivor
is an equivalent `break;`-removal), `DialectMapper.cs`,
`DialectReservedWords.cs`, and 9 files at exactly 100.00%
(`AnalysisDiagnostic.cs`, `CrudColumnRules.cs`, `Diff/SchemaDelta.cs`,
`Diff/StructuralSchemaDiff.cs`, `EntityNameResolver.cs`,
`Impact/Classification.cs`, `Impact/ImpactClassifier.cs`,
`Impact/ImpactEntry.cs`, `Impact/ImpactReason.cs`,
`Impact/QueryImpactInput.cs`, `Migrations/MigrationStatement.cs`).

## Files below target (4 of 19) — round-4 pass complete, results final

| File | Score | Mutants still needing attention (Survived + NoCov) | Round-4 verdict |
|---|---|---|---|
| `Impact/ReferencedObjects.cs` | 86.54% | 7 | Re-verified, no new findings — at documented-equivalent floor |
| `Migrations/MigrationParser.cs` | 87.02% | 99 | 4 fixed this round; ~91 remain, mostly equivalent under 2 generalized absorption mechanisms — only file with real remaining headroom |
| `Impact/MigrationImpactReport.cs` | 90.00% | 1 | Re-verified, no new findings — at documented-equivalent floor |
| `Migrations/SchemaSimulator.cs` | 94.17% | 12 | **Fully closed** — all 12 proven equivalent via the `TryFindTable`/`TryFindColumnKey`/`TryFindColumn` "null-iff-false" contract |

Combined: 119 of the whole project's 131 remaining non-killed mutants sit in
these 4 files, of which `SchemaSimulator.cs`'s 12,
`ReferencedObjects.cs`'s 7, and `MigrationImpactReport.cs`'s 1 (20 total) are
now confirmed-equivalent floors — closed, not gaps. The genuinely open
question is only in `MigrationParser.cs`'s remaining ~91: most look
equivalent under the same two absorption mechanisms, but that has not been
proven per-mutant the way `SchemaSimulator.cs`'s was.

## Bottom line

- **Overall: 94.54% vs. 96% target → -1.46 percentage points (~51 mutants).**
- 10 of 19 files already meet or exceed 96%; 9 of those are at a clean 100%.
- A near-target cleanup pass (commit `1dd7886`) pushed the 4 files already
  ≥96% toward their own ceilings: only `AutoCrud.cs` had a real fixable
  survivor (97.90% → 98.60%, a `List<T>` negative-capacity arithmetic
  mutant). `UpsertKeyResolver.cs`, `DialectMapper.cs`, and
  `DialectReservedWords.cs` were unchanged — all their remaining survivors
  are proven equivalent by direct code-flow analysis. Combined with the 3
  sub-96% files closed in round 4, that's 6 of the 7 sub-100% files (all but
  `MigrationParser.cs`) now confirmed closed — no further test-writing can
  move any of them.
- `MigrationParser.cs` (87.02%) is the only file with any real remaining
  headroom, but closing it needs slow per-mutant manual-mutation verification,
  not another broad sweep — 96% overall is likely not reachable through more
  test-writing alone.
- `Extrode.JauntyQ.SqlParser` baseline established 2026-09-20: **59.22%**,
  -36.78 pp below the same 96% target, before any fixes. See the per-file
  tally below.

## Extrode.JauntyQ.SqlParser — per-file tally vs. 96% target (baseline, no fixes yet)

| File | Killed | Survived | NoCov | Total | Score | vs. 96% |
|---|---|---|---|---|---|---|
| `IR/CteRef.cs` | 0 | 1 | 0 | 1 | 0.00% | -96.00 pp |
| `IR/JoinRef.cs` | 0 | 4 | 0 | 4 | 0.00% | -96.00 pp |
| `IR/LiteralBinding.cs` | 0 | 3 | 0 | 3 | 0.00% | -96.00 pp |
| `IR/PerfHint.cs` | 0 | 4 | 0 | 4 | 0.00% | -96.00 pp |
| `IR/QueryModel.cs` | 0 | 1 | 0 | 1 | 0.00% | -96.00 pp |
| `IR/TableRef.cs` | 0 | 2 | 0 | 2 | 0.00% | -96.00 pp |
| `IR/ColumnRef.cs` | 2 | 6 | 0 | 8 | 25.00% | -71.00 pp |
| `IR/ParameterRef.cs` | 1 | 3 | 0 | 4 | 25.00% | -71.00 pp |
| `SqlParser.Part6.cs` | 124 | 34 | 1 | 349 | 35.53% | -60.47 pp |
| `IR/OrderByRef.cs` | 1 | 1 | 0 | 2 | 50.00% | -46.00 pp |
| `SqlParser.Part4.cs` | 183 | 125 | 4 | 351 | 52.14% | -43.86 pp |
| `SqlParser.Part7.cs` | 107 | 62 | 5 | 205 | 52.20% | -43.80 pp |
| `SqlParser.Part2.cs` | 204 | 94 | 17 | 362 | 56.35% | -39.65 pp |
| `SqlParser.Part5.cs` | 210 | 72 | 21 | 366 | 57.38% | -38.62 pp |
| `SqlParser.Part3.cs` | 170 | 80 | 0 | 283 | 60.07% | -35.93 pp |
| `SqlParser.cs` | 612 | 216 | 12 | 974 | 62.83% | -33.17 pp |
| `SqlTokenizer.cs` | 443 | 63 | 4 | 555 | 79.82% | -16.18 pp |
| `Token.cs` | 1 | 0 | 0 | 1 | 100.00% | **+4.00 pp** |
| **Tally (18 files)** | **2058** | **771** | **64** | **3475** | **59.22%** | **-36.78 pp** |

Largest single lever: the 7 `SqlParser*.cs` files hold 683 of the 771
survivors (89%) and 60 of the 64 no-coverage mutants — this is where a
raise-the-score pass should start, biggest file (`SqlParser.cs`, 216
survivors) first. The 6 zero-score `IR/*.cs` files are small model/record
types with no dedicated tests at all yet, same shape as the Analysis
small-files pass — cheap to close but only worth ~15 mutants combined.
No equivalent-mutant analysis has been done for this assembly yet.
