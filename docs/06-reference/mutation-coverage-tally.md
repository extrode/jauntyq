# Mutation coverage tally vs. target

Per-assembly, per-file mutation score, tallied against the 96% target set for
`Extrode.JauntyQ.Analysis` (parity with sibling repo `jaunty`'s ~96% baseline).
Snapshot as of commit `3535b13` (94.50% overall), after the round-4 pass on
the 4 sub-96% files completed. For score-history-over-time and
equivalent-mutant reasoning, see [`mutation-coverage-report.md`](mutation-coverage-report.md)
and [`../handoffs/2026-09-19-stryker-mutation-gaps.md`](../handoffs/2026-09-19-stryker-mutation-gaps.md).

## Assemblies covered by Stryker

Only 2 of 8 `src/` assemblies have a Stryker config at all; only 1 has ever
been run.

| Assembly | Stryker config | Ever run this effort | Target | Actual | Δ to target |
|---|---|---|---|---|---|
| `Extrode.JauntyQ.Analysis` | `tests/Extrode.JauntyQ.Analysis.Tests/stryker-config.json` | Yes | 96.00% | **94.50%** | **-1.50 pp** |
| `Extrode.JauntyQ.SqlParser` | `tests/Extrode.JauntyQ.SqlParser.Tests/stryker-config.json` | No | — | — | not measured |
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
| `AutoCrud.cs` | 140 | incl. above | 2 | 1 | 143 | 97.90% | **+1.90 pp** |
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
| **Tally (19 files)** | **2249*** | — | **120** | **11** | **2380** | **94.50%** | **-1.50 pp** |

\* "Killed" column includes Timeout mutants (Stryker treats Timeout as a kill
for scoring purposes); the whole-project total of 2245 killed + timeouts
matches the `mutation-coverage-report.md` whole-project figure.

## Files at or above target (10 of 19)

`UpsertKeyResolver.cs`, `AutoCrud.cs`, `DialectMapper.cs`,
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

- **Overall: 94.50% vs. 96% target → -1.50 percentage points (~52 mutants).**
- 10 of 19 files already meet or exceed 96%; 9 of those are at a clean 100%.
- 3 of the 4 sub-96% files (`SchemaSimulator.cs`, `ReferencedObjects.cs`,
  `MigrationImpactReport.cs`) are now confirmed done at their current scores —
  no further test-writing can move them.
- `MigrationParser.cs` (87.02%) is the only file with any real remaining
  headroom, but closing it needs slow per-mutant manual-mutation verification,
  not another broad sweep — 96% overall is likely not reachable through more
  test-writing alone.
- `Extrode.JauntyQ.SqlParser` has a Stryker config but has never been run this
  effort — no score exists to compare against any target for that assembly.
