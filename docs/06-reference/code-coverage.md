# Code coverage

Line coverage per assembly, measured with `scripts/coverage.sh --with-docker` (core suites
plus the SQLite and Testcontainers sample suites, both TFMs, merged by `reportgenerator`).
Mutation scores are tracked separately in `mutation-coverage-tally.md`.

Last run: 2026-10-03 on mb1, dev at `ec0af88`.

| Assembly | Line coverage | Mutation score |
|---|---|---|
| `Extrode.JauntyQ.Analysis` | 99.8% | 100% |
| `Extrode.JauntyQ.Cli` | 100% | no testable mutants |
| `Extrode.JauntyQ.Cli.Core` | 100% | 100% |
| `Extrode.JauntyQ.Generator` | 98.7% | 100% (mutate scope is a subset, see the tally) |
| `Extrode.JauntyQ.Schema` | 100% | 100% |
| `Extrode.JauntyQ.Schema.Extraction` | 99.5% | 100% |
| `Extrode.JauntyQ.SqlParser` | 99.1% | 100% |
| **All 7 assemblies** | **99.1%** (13672 of 13788 coverable lines) | |

Other totals: branch 97.9% (7985 of 8148), method 100% (905 of 905), fully covered
methods 96.5% (874 of 905).

## Reading it

- Each SqlParser and Analysis file is counted once, in its own assembly. The Generator
  compiles every SqlParser/Schema/Analysis file into itself, and until 2026-10-02 its copies
  were counted as well: the same code twice, including files the generator never calls
  (`MigrationImpactReport`). That alone held Generator at 90.2% and the total at 93.3% on
  2026-09-30, with 45 of the 54 uncovered methods being such copies. `coverage.runsettings`
  now leaves the Generator's SqlParser and Analysis copies out. Schema has no test project,
  so its code runs only through the Generator's copy, which stays in; `CoverageScopeTests`
  fails if a `Schema.Tests` project appears without the filter following.
- Every method runs under some test as of 2026-10-03. The nine that did not on 2026-10-02
  (the `AcceptanceFile`/`AcceptanceEntry` getters, `SyntheticQuery.EntityName`,
  `ImpactClassifier.ClassifySingle`, `CliHost.Verbs`, `UserTypeSchema.Precision`/`Scale`)
  were tested or, for `CliHost.Verbs`, deleted. The lowest class is
  `Schema.EnumMemberNaming` at 75.8%, in the Generator's copy.
- Mutation 100% on Generator covers `QueryValidator*`, `*Analyzer.cs`, `JauntyDiagnostics.cs`,
  `IdentifierGuard.cs`, and the emitter files (`CodeEmitter*.cs`, `JauntyQGenerator*.cs`)
  through `mutation-emitter.yml`; other Generator files are not mutated.
- Coverage tells you a line ran, not that a test would notice it changing; the mutation
  score covers that.
- The four Conduit sample suites (81 tests each) ran on this pass, now that mb1 has the
  .NET 8 runtime they target. They exercise the consumer runtime path, which none of the
  7 assemblies above is, so they add tests without moving these numbers.
- Spec 021 phase A (scope refusal, merged after this run as `42060a0`) is not in these
  numbers.

## Reproduce

```bash
scripts/coverage.sh --with-docker
```

Needs `dotnet tool install --global dotnet-reportgenerator-globaltool` and Docker for the
container suites. Run it on mb1, not locally. Output is under `artifacts/coverage/`.
