# Mutation coverage report — Extrode.JauntyQ.Analysis

Living record of Stryker.NET mutation-testing results for the `Extrode.JauntyQ.Analysis`
project. Updated after each coverage-raising pass. Equivalent-mutant reasoning and
per-mutant detail live in [`docs/handoffs/2026-09-19-stryker-mutation-gaps.md`](../handoffs/2026-09-19-stryker-mutation-gaps.md);
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

## Current per-file breakdown (as of 94.50%, commit `3535b13`)

19 source files, 2380 mutants tested (2249 killed, 120 survived, 11 no-coverage).

| Score | Killed | Survived | NoCov | Total | File |
|---|---|---|---|---|---|
| 86.54% | 45 | 5 | 2 | 52 | `Impact/ReferencedObjects.cs` |
| 87.02% | 664 | 91 | 8 | 763 | `Migrations/MigrationParser.cs` |
| 90.00% | 9 | 1 | 0 | 10 | `Impact/MigrationImpactReport.cs` |
| 94.17% | 194 | 12 | 0 | 206 | `Migrations/SchemaSimulator.cs` |
| 96.25% | 77 | 3 | 0 | 80 | `UpsertKeyResolver.cs` |
| 97.90% | 140 | 2 | 1 | 143 | `AutoCrud.cs` |
| 98.72% | 308 | 4 | 0 | 312 | `DialectMapper.cs` |
| 99.68% | 630 | 2 | 0 | 632 | `DialectReservedWords.cs` |
| 100.00% | 1 | 0 | 0 | 1 | `AnalysisDiagnostic.cs` |
| 100.00% | 27 | 0 | 0 | 27 | `CrudColumnRules.cs` |
| 100.00% | 15 | 0 | 0 | 15 | `Diff/SchemaDelta.cs` |
| 100.00% | 44 | 0 | 0 | 44 | `Diff/StructuralSchemaDiff.cs` |
| 100.00% | 15 | 0 | 0 | 15 | `EntityNameResolver.cs` |
| 100.00% | 4 | 0 | 0 | 4 | `Impact/Classification.cs` |
| 100.00% | 66 | 0 | 0 | 66 | `Impact/ImpactClassifier.cs` |
| 100.00% | 3 | 0 | 0 | 3 | `Impact/ImpactEntry.cs` |
| 100.00% | 4 | 0 | 0 | 4 | `Impact/ImpactReason.cs` |
| 100.00% | 1 | 0 | 0 | 1 | `Impact/QueryImpactInput.cs` |
| 100.00% | 2 | 0 | 0 | 2 | `Migrations/MigrationStatement.cs` |

## Mutation-coverage test files added this effort

| Test file | Target file | Score reached | Commit |
|---|---|---|---|
| `AutoCrudSynthesizeTests.cs` (strengthened) | `AutoCrud.cs` | 97.90% | `7c554e0` |
| `ImpactClassifierTests.cs` (strengthened) | `Impact/ImpactClassifier.cs` | 100.00% | `875ac78` |
| `SchemaSimulatorMutationCoverageTests.cs` | `Migrations/SchemaSimulator.cs` | 94.17% | `bd11450` |
| `DialectMapperMutationCoverageTests.cs` | `DialectMapper.cs` | 98.72% | `147e904` |
| `ReferencedObjectsMutationCoverageTests.cs` | `Impact/ReferencedObjects.cs` | 86.54% | `e6a75c3` |
| `UpsertKeyResolverMutationCoverageTests.cs` | `UpsertKeyResolver.cs` | 96.25% | `bf40c03` |
| `MigrationParserMutationCoverageTests.cs` (created) | `Migrations/MigrationParser.cs` | 84.0% → 87.02% | `23e129c`, `04f7b1f`, `75dfcf9`, `091f6a9` |
| `SmallModelTypesMutationCoverageTests.cs` | `MigrationStatement.cs`, `ImpactReason.cs`, `ImpactEntry.cs`, `MigrationImpactReport.cs`, `Classification.cs`, `SchemaDelta.cs` | 90–100% | `951a120` |
| `DialectReservedWordsTests.cs` (earlier session, strengthened) | `DialectReservedWords.cs` | 99.68% | `7a08a4b` |

Also relocated from `Generator.Tests` to `Analysis.Tests` (earlier session): `DialectMapperTests.cs`
(`ae9b086`), `MigrationParserTests.cs` (`afb9fad`, `9f34577`).

## Remaining gap to 96%

As of 94.50%, 4 files remain below 96%: `ReferencedObjects.cs` (86.54%),
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

Net: the 96% target is likely not fully reachable through more test-writing
alone; the remaining gap is dominated by a confirmed equivalent-mutant floor
in 3 of the 4 sub-96% files, with `MigrationParser.cs` as the sole file where
further (slow) work could still move the needle.
