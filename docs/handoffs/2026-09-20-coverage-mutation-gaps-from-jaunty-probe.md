# Coverage/mutation gaps and cross-repo lessons, from a jaunty-side probe

Handoff from a cross-repo probe run out of the `jaunty` session (user asked to check
what JauntyQ is doing right/wrong on testing, coverage and mutation, after jaunty had
just fixed a real bug in its own coverage tooling and widened its Stryker scope). No
code changed in this repo — investigation only, findings below, JauntyQ's own session
picks it up from here. Builds on [2026-09-19-stryker-mutation-gaps.md](2026-09-19-stryker-mutation-gaps.md).

## 1. VSTest vs MTP — resolved, not actually a gap

Checked directly rather than guessing: `tests/Extrode.JauntyQ.SqlParser.Tests.csproj`
and `tests/Extrode.JauntyQ.Analysis.Tests.csproj` reference only
`Microsoft.NET.Test.Sdk`, no `Microsoft.Testing.Platform.*` packages — both test
projects run on the classic VSTest runner. `scripts/coverage.sh` uses
`--collect:"XPlat Code Coverage" --settings coverage.runsettings`, the VSTest
collector path, and does pass `--nologo` (line 78) — but that's harmless here,
because `--nologo` only breaks `dotnet test` when combined with **Microsoft.Testing
Platform's** `--coverage` flag (confirmed this week in jaunty: with `--nologo` present,
`dotnet test --coverage --coverage-output-format cobertura` silently reports "Zero
tests ran", exit 5, even though the identical command without `--nologo` runs the full
suite). JauntyQ isn't on that code path, so this specific bug class doesn't apply here.
Neither `stryker-config.json` sets `"test-runner"` explicitly, which just means Stryker
defaults to whatever the test project itself is wired for (VSTest, per the above) — not
an oversight.

**Action:** none needed on this specific point. Flagging only because it was raised as
an open question in the earlier exchange and is now confirmed, not because it's a bug.

## 2. Real gaps found

**Nightly mutation job runs unconditionally, unbounded.** `.github/workflows/nightly.yml`'s
`mutation` job has no enable/disable gate and no `timeout-minutes`, unlike jaunty's
(`CI_MUTATION_ENABLED` var gate + `timeout-minutes: 240` with a documented rationale: a
runner's "no maximum duration" isn't by itself enough, so a job needs an explicit ceiling
that fails rather than silently burns the runner's allowance on a hang). A stuck Stryker
run here has no backstop.

**`skip-audit.js` doesn't run in CI.** It only runs inside `scripts/test-all.sh`, a
local/manual entry point. `.github/workflows/ci.yml` runs `dotnet test JauntyQ.slnx`
directly, so the exact failure mode skip-audit was built to catch — a `NotExecuted` test
reported as an overall success — is still live in real CI runs, not just local ones.
The tool exists but isn't wired into the thing it's supposed to protect.

**Only 2 of 8 `src/` projects have any Stryker config**: `Analysis`, `SqlParser`.
`Cli.Core`, `Cli`, `Generator` (13,107 LOC — the largest project in the solution),
`Runtime`, `Schema`, `Schema.Extraction` have zero mutation coverage. Already noted in
the 2026-09-19 handoff; repeating here since it's still true and is the single biggest
lever if mutation testing is meant to mean something project-wide rather than just for
the two projects that happen to have configs.

**Both existing Stryker configs mutate the whole project**, no `mutate` glob narrowing
— unlike jaunty's, which scope to the highest-uncovered-complexity folders per project
(e.g. `**/Internals/**`, `**/Dialects/**`) rather than the whole tree. Not wrong, but it
means mutant count (and CI time) scales with total LOC instead of with what's actually
under-tested — worth reconsidering once more projects get configs, or Generator's
13K LOC will make its own mutation run expensive by default.

**`break: 0` isn't explained at the config level.** Both `stryker-config.json` files
set it with no comment; the "report, not gate" reasoning lives only in
`nightly.yml`'s workflow comment. Fine today, but if a config is ever copied out of
this repo's `nightly.yml` context (e.g. to a project's own directory, the way jaunty's
are laid out) the reasoning goes missing with it.

## 3. Things this repo does well — for context, not action

`scripts/coverage.sh`'s retry-once-then-fail-hard pattern (lines 79-88: on failure,
wipe the suite's output dir, retry once, and only then record it as failed) and
`scripts/test-all.sh`'s assembly-parallelism cap are both good patterns jaunty doesn't
have yet and is adding on the strength of seeing them here. `skip-audit.js` itself —
parsing every `.trx` for `NotExecuted` and failing on anything not matched against a
narrow, documented allowlist — is a good design; the gap above is purely that it isn't
invoked from CI, not that the tool is wrong.

## 4. Other lessons from the jaunty session worth carrying over

- **A flag combination can silently zero a test run without a nonzero exit code your
  own retry/CI logic would catch.** Jaunty's `--nologo` bug and this repo's own
  documented 2026-08-17 incident (101 tests silently skipped, exit 0) are the same
  failure shape from different causes. Any time a test/coverage script's flags change,
  worth a smoke check that the reported pass/fail count actually matches expectations,
  not just that the exit code is 0.
- **Batch review/delegation work by file count, not method/mutant count**, if this repo
  ever farms out mutation-gap triage the way jaunty did for its coverage report — a
  review-style pass costs roughly one tool call per file regardless of how much is
  inside it, so file count is the load-bearing constraint on how big a batch a dispatched
  worker/agent can actually finish before hitting its turn cap.
- **Re-verify self-reported "equivalent mutant" classifications independently** — the
  2026-09-19 handoff already demonstrates this exact lesson in this repo (3 mutants
  marked equivalent were later found to be real, killable gaps via a second pass) — just
  flagging that it generalizes: any single agent's "this is unreachable/equivalent/dead
  code" classification is worth a second, differently-framed pass before it's trusted
  in a report, not just for mutants.

## How this was produced

Read-only probe from the `jaunty` repo's session: `scripts/coverage.sh`,
`scripts/test-all.sh`, `scripts/skip-audit.js`, `.github/workflows/{ci,nightly}.yml`,
both `tests/*/stryker-config.json` and their sibling `.csproj` files, `BannedSymbols.txt`,
and the existing `docs/06-reference/mutation-coverage-*.md` + 2026-09-19 handoff. No
`.claude/worktrees/*` copies were used (stale parallel-agent scratch, not canonical).
