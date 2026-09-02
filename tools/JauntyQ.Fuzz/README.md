# JauntyQ.Fuzz

Coverage-guided fuzzing of the SQL front end, via [SharpFuzz](https://github.com/Metalnem/sharpfuzz)
over libFuzzer. Phase 2 item 3 of the testing strategy.

## Status

**Fuzzed locally, never yet in CI.** Written 2026-08-24 on Windows; libFuzzer is Linux-only in
practice. The first nightly run (2026-08-25, run 32806048696) aborted in 41s before fuzzing a
single input, because the job invoked the harness without the `libfuzzer-dotnet` driver — see
"Running it" below. The driver was added the same day and the job verified under WSL; the first
green *nightly* is still pending.

What *has* run, both on 2026-08-25:

- All 18 corpus seeds replayed one at a time through the fallback path on Windows: 0 crashes.
  That exercises the harness body and the seeds, not the fuzzer.
- The whole job reproduced under WSL Debian 12 with `libfuzzer-dotnet-debian`, at the CI
  duration: **6,352,668 executions in 601s, exit 0, no crashing inputs**, corpus grown 18 -> 9,884.
  A 60s warm-up run first did 706,920 executions, also clean.

Two deviations from CI in that local run, neither affecting what it proves about the wiring:
it used the `-debian` driver against Debian where CI pairs `-ubuntu` with `ubuntu-latest`, and
it ran on .NET 10 via `DOTNET_ROLL_FORWARD=LatestMajor` because that WSL has no 8.0 runtime,
where CI pins 8.0.x. A crasher found on one runtime still has to be re-checked on the other.

## What it asserts

Total-ness, not correctness. `SqlTokenizer.Tokenize` and `SqlParser.Parse` run **unguarded**
inside the Roslyn generator (`JauntyQGenerator.Part2.cs`), so an escaping exception surfaces as
CS8785 and every generated method vanishes from the consumer's build. The harness mirrors the
generator's own gates: a token list carrying `Unterminated` / `Unknown` / `TooLarge` / `TooDeep`
is rejected with a diagnostic upstream and never reaches `Parse`, so those are returned early
rather than counted as findings.

`HostileInputParserTests` already pins ~60 curated cases of this shape, and
`ParserPropertyTests` covers a generated token vocabulary. This harness exists for the inputs
neither would think to write.

## Running it

```sh
dotnet publish tools/JauntyQ.Fuzz -c Release -o out/fuzz
dotnet tool install --global SharpFuzz.CommandLine
sharpfuzz out/fuzz/JauntyQ.SqlParser.dll

curl -sSfL -o libfuzzer-dotnet \
  https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2025.05.02.0904/libfuzzer-dotnet-ubuntu
chmod +x libfuzzer-dotnet

mkdir -p out/corpus out/fuzz-findings
./libfuzzer-dotnet -max_total_time=600 -artifact_prefix=out/fuzz-findings/ \
  --target_path="$(command -v dotnet)" --target_arg=out/fuzz/JauntyQ.Fuzz.dll \
  out/corpus tools/JauntyQ.Fuzz/corpus
```

Two corpus directories, and the order matters: libFuzzer writes new units to the **first** one
only and treats the rest as read-only seeds. `out/corpus` is the one that grows;
`tools/JauntyQ.Fuzz/corpus` keeps its 18 committed seeds untouched. Nightly caches `out/corpus`
under a rolling `fuzz-corpus-*` key so each night resumes where the last left off, and minimises
it with `-merge=1` before saving:

```sh
mkdir -p out/corpus-min
./libfuzzer-dotnet -merge=1 \
  --target_path="$(command -v dotnet)" --target_arg=out/fuzz/JauntyQ.Fuzz.dll \
  out/corpus-min out/corpus tools/JauntyQ.Fuzz/corpus
```

`libfuzzer-dotnet` is not optional and not a wrapper for convenience. `Fuzzer.LibFuzzer.Run`
reads `__LIBFUZZER_SHM_ID`, `__LIBFUZZER_STATUS_PIPE_ID` and `__LIBFUZZER_CONTROL_PIPE_ID`, which
only that driver sets; missing any of them it falls back to `RunWithoutLibFuzzer`, which does
`File.ReadAllBytes(args[1])` and dies on a corpus **directory** with `UnauthorizedAccessException`.
That fallback is a useful single-input replay — `dotnet out/fuzz/JauntyQ.Fuzz.dll corpus/001-select.sql`
works on Windows and is how a promoted crasher is re-checked — but it is not fuzzing.

## Corpus discipline

`corpus/` holds 18 hand-written seeds spanning the grammar's shapes. Fuzzer output is **not**
directly a test:

1. Minimise the crashing input first (`-minimize_crash=1`).
2. Promote it to a named `HostileInputParserTests` case with an assertion message saying why it
   is there. A raw byte blob with no context rots into noise nobody dares delete.
3. Keep the seed in `corpus/` alongside it.
