# Code coverage

Line coverage per assembly, measured with `scripts/coverage.sh --with-docker` (core suites
plus the SQLite and Testcontainers sample suites, both TFMs, merged by `reportgenerator`).
Mutation scores are tracked separately in `mutation-coverage-tally.md`.

Last run: 2026-09-30 on mb1, dev at `0bee865`.

| Assembly | Line coverage | Mutation score |
|---|---|---|
| `Extrode.JauntyQ.Analysis` | 99.7% | 100% |
| `Extrode.JauntyQ.Cli` | 100% | no testable mutants |
| `Extrode.JauntyQ.Cli.Core` | 99.6% | 100% |
| `Extrode.JauntyQ.Generator` | 90.2% | 100% (mutate scope is a subset, see the tally) |
| `Extrode.JauntyQ.Schema` | 96% | 100% |
| `Extrode.JauntyQ.Schema.Extraction` | 99.3% | 100% |
| `Extrode.JauntyQ.SqlParser` | 98.6% | 100% |
| **All 7 assemblies** | **93.3%** (16064 of 17206 coverable lines) | |

Other totals: branch 88.4% (10100 of 11416), method 95.3% (1112 of 1166), fully covered
methods 83.6% (975 of 1166).

## Reading it

- Mutation 100% on Generator covers only `QueryValidator*`, `*Analyzer.cs`,
  `JauntyDiagnostics.cs` and `IdentifierGuard.cs`; the rest of that assembly is what the
  90.2% line figure exposes.
- Coverage tells you a line ran, not that a test would notice it changing; the mutation
  score covers that.
- Five suites had one failing test on this run, so their coverage is merged from a run
  with a failure: `Generator.Tests` (`LicenceLinkPinTests.EveryLicenceLink_IsPinnedToAVersionAndAFile`)
  and the four `EShopOnWeb.*` sample suites (Postgres `CatalogTests.GetByName_ExactMatch`,
  plus one test each in MySql, MariaDb and SqlServer). Not yet triaged.

## Reproduce

```bash
scripts/coverage.sh --with-docker
```

Needs `dotnet tool install --global dotnet-reportgenerator-globaltool` and Docker for the
container suites. The run takes about 70 minutes on mb1 under load; run it there, not locally.
Output is under `artifacts/coverage/`.
