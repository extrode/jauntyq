# Code coverage

Line coverage per assembly, measured with `scripts/coverage.sh --with-docker` (core suites
plus the SQLite and Testcontainers sample suites, both TFMs, merged by `reportgenerator`).
Mutation scores are tracked separately in `mutation-coverage-tally.md`.

Last run: 2026-10-02 on mb1, dev at `93a5912`.

| Assembly | Line coverage | Mutation score |
|---|---|---|
| `Extrode.JauntyQ.Analysis` | 99.7% | 100% |
| `Extrode.JauntyQ.Cli` | 100% | no testable mutants |
| `Extrode.JauntyQ.Cli.Core` | 99.6% | 100% |
| `Extrode.JauntyQ.Generator` | 98.7% | 100% (mutate scope is a subset, see the tally) |
| `Extrode.JauntyQ.Schema` | 97.9% | 100% |
| `Extrode.JauntyQ.Schema.Extraction` | 99.5% | 100% |
| `Extrode.JauntyQ.SqlParser` | 99.1% | 100% |
| **All 7 assemblies** | **99%** (13496 of 13622 coverable lines) | |

Other totals: branch 97.9% (7897 of 8060), method 98.9% (879 of 888), fully covered
methods 95.3% (847 of 888).

## Reading it

- Each SqlParser and Analysis file is counted once, in its own assembly. The Generator
  compiles every SqlParser/Schema/Analysis file into itself, and until 2026-10-02 its copies
  were counted as well: the same code twice, including files the generator never calls
  (`MigrationImpactReport`). That alone held Generator at 90.2% and the total at 93.3% on
  2026-09-30, with 45 of the 54 uncovered methods being such copies. `coverage.runsettings`
  now leaves the Generator's SqlParser and Analysis copies out. Schema has no test project,
  so its code runs only through the Generator's copy, which stays in; `CoverageScopeTests`
  fails if a `Schema.Tests` project appears without the filter following.
- The nine methods no test runs on 2026-10-02: the `AcceptanceFile`/`AcceptanceEntry`
  getters in the Schema assembly (the Generator's copy runs them), `SyntheticQuery.EntityName`
  and `ImpactClassifier.ClassifySingle` in Analysis, `CliHost.Verbs` (no caller), and
  `UserTypeSchema.Precision`/`Scale` in the Generator's Schema copy.
- Mutation 100% on Generator covers `QueryValidator*`, `*Analyzer.cs`, `JauntyDiagnostics.cs`,
  `IdentifierGuard.cs`, and the emitter files (`CodeEmitter*.cs`, `JauntyQGenerator*.cs`)
  through `mutation-emitter.yml`; other Generator files are not mutated.
- Coverage tells you a line ran, not that a test would notice it changing; the mutation
  score covers that.
- The four Conduit sample suites did not run on this pass: they target `net8.0` only and mb1
  has only the .NET 10 runtime, so their test host exits with "You must install or update
  .NET". They exercise the consumer runtime path, which none of the 7 assemblies above is.

## Reproduce

```bash
scripts/coverage.sh --with-docker
```

Needs `dotnet tool install --global dotnet-reportgenerator-globaltool` and Docker for the
container suites. Run it on mb1, not locally. Output is under `artifacts/coverage/`.
