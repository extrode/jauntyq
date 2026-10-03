# Re-cut the local 0.6.2 release as 0.7.0 without losing work

## Context
0.6.2 was cut locally (merge commit on `main`, tag `v0.6.2`, nothing pushed). Under
`docs/06-reference/versioning-and-support.md` the release is a minor version, not a patch: tenant
scoping, `migrate status`, a new public `MigrationOrder.Compare`, the removal of `CliHost.Verbs`
and the generated `Read` -> `__Read` rename. The user wants fable to verify the release first,
then the 0.6.2 cut removed, then 0.7.0 cut with nothing lost.

## Current state (measured 2026-10-03)
| Ref | SHA | Note |
|---|---|---|
| `v0.6.1` / `main^1` | `8b428e0` | "Release 0.6.1" |
| `main` / `v0.6.2` | `7032dc0` | "Release 0.6.2" merge, unpublished |
| `dev` | `57a5a1a` | merge of `chore/release-0.6.2` |
| `dev^1` | `1f43762` | last code commit (upsert fix merge); test-all green here: 11,978 pass, 0 fail, 87 skip |
| `chore/release-0.6.2` | `d4c5ba5` | 3 commits: `15e0c7f` changelog, `6cf6545` bump, `d4c5ba5` cleanup script |

- Main checkout (on `dev`), `.worktrees/verify-parser` (detached) and `.worktrees/verify-scope`: no tracked changes. `tmp/` and `work/` are gitignored, so a reset does not touch them.
- Fable `review-deep` is running (session `c7ca408f-c265-42e7-841e-0e3b94c45bf6`), output to `tmp/fable-verify-070.md`. Follow-ups resume with `--resume-id c7ca408f-c265-42e7-841e-0e3b94c45bf6 --key verify-070`, then `--key verify-070`.

## Steps

### 1. Fable verification (in progress)
Wait for `tmp/fable-verify-070.md`. Findings about code are checked against the code before any
fix (fable has no shell). Confirmed code findings, and any change to the breaking-change list, go
to the user before step 4. Changelog corrections are applied in step 5 without asking.

### 2. Preflight: stop if any check fails
- `git rev-parse dev` = `57a5a1a`, `main` = `7032dc0`, `v0.6.2` = `7032dc0`. Any difference means someone else committed since; stop and report.
- Nothing published: `git branch -r --contains 7032dc0` is empty and `git ls-remote --tags origin v0.6.2` returns nothing.
- `git status --short` is empty in the main checkout and both worktrees.

### 3. Back up before anything moves
- `git bundle create tmp/pre-0.7.0-backup.bundle --all` (an offline copy of every ref; `git bundle verify` it).
- Tags `backup/pre-0.7.0-main` -> `7032dc0` and `backup/pre-0.7.0-dev` -> `57a5a1a`, so the old commits stay reachable without the reflog.
- Leave `chore/release-0.6.2` in place. It keeps the 3 release commits by name.

### 4. Undo the 0.6.2 cut (after the user's yes, given in the approval of this plan)
- `git tag -d v0.6.2`
- Move `main` without checking it out: `git branch -f main 8b428e0` (`main` is not checked out anywhere).
- In the main checkout: `git reset --keep 1f43762`. `--keep` refuses rather than discards if any local change would be lost.
- Check: `main` = `8b428e0`, `dev` = `1f43762`, and both backup tags still resolve.

### 5. Fix the confirmed fable findings
One `fix/` branch per finding off `dev`, with tests and a mutation check for each (break the fix, see the tests fail, revert with `git checkout --`), then merge into `dev` with `--no-ff`. Out-of-scope findings go in `work/todo.md` as one line each.

### 6. Release branch `chore/release-0.7.0` off `dev`
- `git cherry-pick 15e0c7f` (changelog completion; the version bump `6cf6545` is NOT replayed).
- `scripts/bump-version.sh 0.6.1 0.7.0` (Directory.Build.props, README.md, docs/01-getting-started/README.md).
- CHANGELOG: `## [Unreleased]` stays empty, then `## [0.7.0] - <date>`. Add a `### Breaking` section listing each break (at least: `CliHost.Verbs` removed; `__Read`; Postgres `CREATE TABLE` name folding; new JNT6004 and name-collision refusals), plus fable's changelog corrections.
- Cleanup script: `git cherry-pick d4c5ba5`, `git mv` it to `scripts/cleanup/release-0-7-0-2026-10-03.ps1`, and update it:
  - branches: add `chore/release-0.6.2`, `chore/release-0.7.0` and the step 5 fix branches (`git branch -d` refuses any that are not merged);
  - scratch: add `tmp/verify-070-*`, `tmp/fable-verify-070.md`, `tmp/test-all-0.7.0.log`;
  - backups: a separate `-DeleteBackups` switch removes the two `backup/pre-0.7.0-*` tags and the bundle. Off by default, because those are the only copies of the 0.6.2 cut outside the reflog.
- `git grep -n "0\.6\.2"` should then find only the changelog's past-tense mention, if there is one.

### 7. Verify before tagging
- 4 suites at net10.0 after each step 5 merge (`dotnet test tests/Extrode.JauntyQ.<X>.Tests -f net10.0 -v q`).
- On the release branch: `scripts/test-all.sh > tmp/test-all-0.7.0.log` (Docker is up). Must exit 0 with 0 failures.
- `deno run -A ~/.claude/tools/structure-audit.js`: no new findings beyond the existing 6.
- Parse-check the cleanup script with the PowerShell `Parser.ParseFile` call.

### 8. Cut
- Merge `chore/release-0.7.0` into `dev` (`--no-ff`, run on its own because of the hook that blocks commit plus dev in one command).
- In `.worktrees/verify-parser`: `git checkout main`, `git merge --no-ff dev -m "Release 0.7.0"`, `git tag v0.7.0`, `git checkout --detach`.
- Not pushed.

### 9. Post-checks
- `git show v0.7.0:Directory.Build.props` has `<Version>0.7.0</Version>` (release.yml's tag guard reads it).
- `main^1` = `8b428e0`, `git rev-list --count dev..main main..dev` = 0, `v0.6.2` is gone.
- `git branch --contains 15e0c7f` and the backup tags show that no old commit was lost.

## Out of scope
- `jauntyq-premium` stays on core 0.6.1 (`JauntyQCoreVersion`). Its bump follows the core publish, as in 0.6.1.
- Pushing, running the cleanup script, deleting backups: all the user's call.

## Rollback at any point
`git branch -f main backup/pre-0.7.0-main`, `git reset --keep backup/pre-0.7.0-dev` in the main checkout, `git tag v0.6.2 backup/pre-0.7.0-main`. That restores the exact 0.6.2 state. The bundle covers the case where the tags themselves were removed.
