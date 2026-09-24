# Conventions — jauntyq

Mechanical how-we-work rules. Defaults are project-local;
this file records project-specific deltas. Binding principles live in docs/constitution.md;
project memory lives in docs/lessons/.

## File & directory naming
- kebab-case for files and dirs: `mail-pipeline.md`, `data-model.sql`.
- Except where an established convention dictates otherwise: `README.md`, `CHANGELOG.md`,
  `LICENSE.md`, `Directory.Build.props`, or language-mandated casing (C# `PascalCase.cs`).
- Unsure? Match the nearest existing sibling file.

## Git workflow
- `main` is release-only; `dev` is integration. Never commit directly to either.
- All work on named branches off `dev` (`feat/ fix/ chore/ docs/ refactor/ test/`). Commit often.
- Merge completed branches back to `dev` with `--no-ff`. Always merge once a branch is complete —
  never leave finished work unmerged.
- Release: `dev` -> `main` `--no-ff` + semver tag. Ask before deleting branches.

---

This file is a stub. Populate it with project-specific mechanical conventions as they are
established, following the same structure as jaunty's `docs/conventions.md`.
