# Push Extrode.JauntyQ.Analysis mutation score from 81.51% to 96%+

## Context

Prior session closed Analysis's mutation gap from 32.52% to 81.51% (three fixes:
exhaustive reserved-word tests, and two misplaced-test-file relocations from
`Generator.Tests`). User now wants parity with jaunty's ~96% baseline. Score
formula (verified against latest local report,
`tests/Extrode.JauntyQ.Analysis.Tests/StrykerOutput/2026-09-19.16-17-01/reports/mutation-report.json`):

    score = (Killed + Timeout) / (Killed + Timeout + Survived + NoCoverage)

Current: 1940 / 2380 = 81.51%. To hit 96%, `Survived + NoCoverage` must drop
from 440 to ≤95 project-wide — i.e. fix ~345 of the current 424 remaining
mutants in these 7 files (the other ~16 outside them are residual/untouched):

| File | Survived | NoCoverage | Total to address |
|---|---|---|---|
| `Migrations/MigrationParser.cs` | 163 | 8 | 171 |
| `AutoCrud.cs` | 80 | 29 | 109 |
| `Impact/ImpactClassifier.cs` | 16 | 23 | 39 |
| `Migrations/SchemaSimulator.cs` | 27 | 5 | 32 |
| `DialectMapper.cs` | 21 | 11 | 32 |
| `Impact/ReferencedObjects.cs` | 16 | 7 | 23 |
| `UpsertKeyResolver.cs` | 11 | 7 | 18 |

Checked for the same misplaced-test bug that caused the prior 55.97%→81.51%
jump: `tests/Extrode.JauntyQ.Generator.Tests/AutoCrudTests.cs` (1868 lines)
does call `AutoCrud.Synthesize` directly, but it also uses
`CSharpGeneratorDriver`/`CodeEmitter`/`JauntyQGenerator` throughout — it's a
genuine integration test file, not a misplaced unit-test file, so it stays put.
No further move-only wins found. AutoCrud.cs's own survivor list is
concentrated in String/Equality/Statement mutations on synthesized
method-name/SQL-text literals (lines 60–370) with almost no NoCoverage —
meaning the code **is** exercised by `CrudColumnRulesTests.cs`, just with
assertions too loose to catch a mutated string/comparison. This is the same
"strengthen assertions" pattern as the DialectReservedWords fix, not a
zero-coverage gap.

This pass is genuine fixture/assertion work across all 7 files — no more
structural shortcuts expected. It's the largest lever left; going in file by
file, largest-impact first.

## Approach

Branch `test/analysis-mutation-coverage-96` off `dev`. Work through the 7
files above in this order (impact-weighted): MigrationParser.cs, AutoCrud.cs,
ImpactClassifier.cs, SchemaSimulator.cs, DialectMapper.cs,
ReferencedObjects.cs, UpsertKeyResolver.cs.

Per file:
1. Get the current survivor/NoCoverage list for that file (re-derive from a
   fresh scoped Stryker run's JSON, not the stale 16:17 snapshot, once prior
   files' fixes may have shifted line numbers).
2. Read the source file and its existing test file (all 7 already have one:
   `MigrationParserTests.cs`, `CrudColumnRulesTests.cs` (+ related AutoCrud
   diagnostic tests), `ImpactClassifierTests.cs`, `SchemaSimulator*Tests.cs`
   (3 files), `DialectMapperTests.cs`, tests covering `ReferencedObjects` and
   `UpsertKeyResolverTests.cs`).
3. Fix survivors by category:
   - **Survived, String/Equality/Statement mutations on literals or
     comparisons**: strengthen existing test assertions to check exact
     values (e.g. full generated SQL text, exact method/column names) instead
     of loose existence/count checks — mirrors the `RequiresQuotingForCase`
     fix from the prior session.
   - **NoCoverage**: add a new test/theory case that exercises the untested
     branch (e.g. a specific dialect, an edge-case schema shape, a guard
     clause's false path).
   - Where a whole subset is enumerable (like the reserved-word lists were),
     generate exhaustive `[Theory]`/`[InlineData]` cases rather than
     hand-picking a few.
4. Verify fast and locally with a **file-scoped** Stryker run (keeps within
   the "seconds to a few minutes" local-run rule — no full-project run until
   the final check):
   ```
   cd tests/Extrode.JauntyQ.Analysis.Tests
   dotnet stryker --mutate "**/<File>.cs" --reporter progress --reporter json
   ```
5. Commit: `test: kill mutation survivors in <File>.cs` (one commit per file,
   keeps the branch bisectable).

Some mutants may be equivalent (semantically identical after mutation, e.g. a
swapped `&&`/`||` that can't be distinguished by any input) — these get
flagged, not force-killed with contrived tests; document them in the handoff
if any remain.

## After all 7 files

1. Full whole-project Stryker run (`dotnet stryker` from
   `tests/Extrode.JauntyQ.Analysis.Tests`, no `--mutate` scoping — the
   config itself keeps whole-project scope, unchanged from before) to get the
   real final score. Historically ~3-4 min for this project; acceptable per
   the local-run-duration rule.
2. If short of 96%, one more targeted pass on whatever's left (should be
   small — mostly equivalent-mutant residue at that point).
3. Full solution `dotnet build -c Release` + `dotnet test`, redirected to
   `tmp/full-test-run.log` and checked in full (not truncated), to confirm no
   regressions — same process as the prior session.
4. Update `docs/handoffs/2026-09-19-stryker-mutation-gaps.md`: replace the
   "Remaining in Extrode.JauntyQ.Analysis" punch list with the final result
   and any documented equivalent mutants.
5. Delete the two scratch analysis scripts written during planning
   (`tmp/summarize_mutants.py`, `tmp/autocrud_survivors.py`) — project-local
   scratch, free to remove.
6. Merge `--no-ff` into `dev` (no confirmation needed per standing git
   workflow rule). Leave unpushed — pushing is a separate confirmation, as
   before.

## Verification

- Per-file: scoped Stryker run shows 0 (or documented-equivalent-only)
  Survived/NoCoverage for that file before moving to the next.
- Final: whole-project Stryker score ≥96%, full solution build green, full
  test suite green (aside from any confirmed-unrelated environmental flake,
  same triage as last time — rerun the flaky project alone to confirm before
  writing it off).
- Handoff doc reflects the true final state.
