# Directory-structure standardization, handoff from the jaunty session

Handoff from a cross-repo initiative run out of the `jaunty` session. Owner's goal: standardize
directory-structure conventions across all .NET projects under `C:/home/code/extrode.com/`,
starting with jaunty and jauntyq specifically (as the two most similar/mature sibling repos), then
extending the settled convention to the other ~17 .NET repos in that directory later. No code or
docs in *this* repo (jauntyq) have been touched yet — that's intentionally left for this session to
pick up, with full context below so nothing needs re-deriving.

## What's already been done (all in the jaunty repo, read/analysis only until noted)

1. Generated full ASCII directory trees for both repos and wrote a comparison analysis, committed
   to jaunty's `dev` at `docs/05-quality/reports/`:
   - `dirstruct-jaunty-2026-09-21.txt`
   - `dirstruct-jauntyq-2026-09-21.txt`
   - `dirstruct-comparison-2026-09-21.md` — the original analysis (top-level diffs, docs/ numbering
     mismatch, tests/ naming inconsistencies within jaunty itself, apparent fixture-data
     philosophy split, dead folders found)
2. Got an independent second opinion from Fable (given the same trees + a broader survey of the
   other extrode.com .NET repos), also committed:
   - `dirstruct-fable-recommendations-2026-09-21.md` — **this is the one to read first if you only
     read one file.** It corrects several mistakes in the original comparison (the fixture-data
     "philosophy split" turned out not to be a real conflict — jauntyq's per-sample `db/` folders
     are consumer-facing schema-source-of-truth for the generator, not test fixtures, and jaunty's
     `seed/` turned out to be dead/unused rather than an intentional alternative approach) and lays
     out a concrete proposed top-level convention (section 2), a `tests/` naming rule (section 3),
     a ruling on samples/fixture data (section 4 — **jauntyq's `db/` convention is confirmed
     correct and stays as-is**), a `docs/` numbering map meant to apply identically to both repos
     (section 5), and a short list of what to adopt from each other (section 6).
3. Did the one trivial no-judgment-call fix in jaunty itself: deleted the dead
   `docs/architecture/` folder (`.gitkeep` only, leftover from an earlier docs migration). Nothing
   else has been changed in jaunty yet either — everything else needs a decision from the owner
   before execution (the docs renumbering in particular touches 8 folders + all relative links in
   jaunty).

## Specifically flagged for jauntyq (from Fable's review, section 1 item 10 and section 6)

These are the concrete, jauntyq-specific findings — not yet actioned here, listed so this session
doesn't have to rediscover them:

- **`.config/dotnet-tools.json` is missing.** Jaunty pins `dotnet-stryker 4.16.0` via a local tool
  manifest; jauntyq's nightly workflow instead installs Stryker ad hoc in a workflow step. Flagged
  as an actual oversight (not just an optional style difference) — an unpinned Stryker version in
  CI can silently drift.
- **`data/` is an empty top-level directory** with no files and nothing referencing it — likely
  dead, candidate for deletion (verify nothing gitignored-but-intended lives there first).
- **`docs/assets/build-not-prod.svg` duplicates the `docs/_assets/` convention** — one stray file
  outside the underscore-prefixed asset folder both repos otherwise use. Candidate to move into
  `docs/_assets/`.
- **`docs/` numbering**: under Fable's proposed map (section 5 of the recommendations doc),
  jauntyq needs only two renames — `06-reference` → `04-reference`, `07-roadmap` → `09-roadmap` —
  much cheaper than jaunty's 8-folder renumber. `00-overview`, `01-getting-started`, `02-learn`,
  `03-guides` already land on the right numbers as-is.
- **`test-infra/`** is confirmed to be doing the same job as jaunty's linked shared-test-sources
  (`Extrode.Jaunty.Tests/Helpers`, `<Compile Include>`-linked into `UnitTests`) — Fable's proposal
  is a shared `tests/Shared/` name in both repos, linked via `tests/Directory.Build.props` /
  `samples/Directory.Build.props`. Not urgent, just noted as the target shape if/when this gets
  standardized.
- **`laws/` convention** (jaunty has `docs/laws/` + matching `tests/.../Unit/Laws/L00N*Tests.cs`
  for stated invariants) — worth adopting here once jauntyq has an invariant worth documenting
  that way. Not now, just flagged as a pattern to borrow later.
- Your own `handoffs/` folder convention was called out favorably and is **not** proposed for
  removal or change — jaunty doesn't have one and will likely add it only once it actually writes
  a cross-session handoff of its own.

## What jaunty is *not* changing unilaterally

The docs-numbering rename and the `tests/`-naming rename (`Extrode.Jaunty.UnitTests` →
`Extrode.Jaunty.Parallel.Tests` or similar, per section 3 of the recommendations) both touch CI
YAML, `.slnx` files, and in jaunty's case relative doc links — these need the owner's sign-off
before either repo executes them, and are called out as such in both the comparison and
recommendations docs. Don't start those in jauntyq without confirming the plan still holds
(particularly the `docs/` numbering map — Fable flagged in its own recommendations that if
the shared Docs tool at `C:\home\code\beparey.com\docsgen` hardcodes prefix-to-section names,
the map needs to follow the tool instead, and that wasn't verified).

## Suggested next step for this session

Read the three jaunty-repo files above (`dirstruct-jauntyq-2026-09-21.txt` for your own tree as
jaunty's session saw it, plus both analysis docs), sanity-check the jauntyq-specific findings
against the current tree (Fable's review of jauntyq was done via the semble index and the
committed tree file only — no direct file reads — so worth a quick verification pass here), and
hold on any renames until the owner has weighed in on the `docs/` numbering plan and the `tests/`
rename cost across both repos.
