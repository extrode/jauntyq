# Code coverage

Line coverage per assembly, measured with `scripts/coverage.sh --with-docker` (core suites
plus the SQLite and Testcontainers sample suites, both TFMs, merged by `reportgenerator`).
Mutation scores are tracked separately in `mutation-coverage-tally.md`.

Last run: 2026-10-14 in a 4-CPU cloud container, dev at `2fa7231`.

| Assembly | Line coverage | Mutation score |
|---|---|---|
| `Extrode.JauntyQ.Analysis` | 99.9% | 100% |
| `Extrode.JauntyQ.Cli` | 100% | no testable mutants |
| `Extrode.JauntyQ.Cli.Core` | 100% | 100% |
| `Extrode.JauntyQ.Generator` | 98.7% | 100% (mutate scope is a subset, see the tally) |
| `Extrode.JauntyQ.Schema` | 100% | 100% |
| `Extrode.JauntyQ.Schema.Extraction` | 99.5% | 100% |
| `Extrode.JauntyQ.SqlParser` | 98.9% | 100% |
| **All 7 assemblies** | **99.1%** (14610 of 14737 coverable lines) | |

Other totals: branch 98.1% (8631 of 8798), method 99.8% (987 of 988), fully covered
methods 96.5% (954 of 988). The one method no test ran, `UnprovenReach.Column`, has
had a test since `58e719b`.

Change since 2026-10-03 (`ec0af88`): 949 more coverable lines (spec 021's scope refusal
landed after that run). Line coverage holds at 99.1%; branch coverage rose from 97.9% to 98.1%. SqlParser
moved from 99.1% to 98.9% and Analysis from 99.8% to 99.9%.

## Reading it

- Each SqlParser and Analysis file is counted once, in its own assembly. The Generator
  compiles every SqlParser/Schema/Analysis file into itself, and until 2026-10-02 its copies
  were counted as well: the same code twice, including files the generator never calls
  (`MigrationImpactReport`). That alone held Generator at 90.2% and the total at 93.3% on
  2026-09-30, with 45 of the 54 uncovered methods being such copies. `coverage.runsettings`
  now leaves the Generator's SqlParser and Analysis copies out. Schema has no test project,
  so its code runs only through the Generator's copy, which stays in; `CoverageScopeTests`
  fails if a `Schema.Tests` project appears without the filter following.
- Every method ran under some test on 2026-10-03; on 2026-10-14 one did not
  (`UnprovenReach.Column`) and was tested. The nine that did not on 2026-10-02
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

## Reproduce

```bash
scripts/coverage.sh --with-docker
```

Needs `dotnet tool install --global dotnet-reportgenerator-globaltool` and Docker for the
container suites. Run it on mb1, not locally. Output is under `artifacts/coverage/`.
