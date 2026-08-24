# JauntyQ.Fuzz

Coverage-guided fuzzing of the SQL front end, via [SharpFuzz](https://github.com/Metalnem/sharpfuzz)
over libFuzzer. Phase 2 item 3 of `the plan`.

## Status

**Never executed.** Written 2026-08-24 on Windows; libFuzzer is Linux-only in practice, so this
harness has been compiled but not run. The first real evidence is the first nightly CI run
(`.github/workflows/nightly.yml`). Treat any claim about what it finds as unproven until then.

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
dotnet out/fuzz/JauntyQ.Fuzz.dll tools/JauntyQ.Fuzz/corpus -max_total_time=600
```

## Corpus discipline

`corpus/` holds 18 hand-written seeds spanning the grammar's shapes. Fuzzer output is **not**
directly a test:

1. Minimise the crashing input first (`-minimize_crash=1`).
2. Promote it to a named `HostileInputParserTests` case with an assertion message saying why it
   is there. A raw byte blob with no context rots into noise nobody dares delete.
3. Keep the seed in `corpus/` alongside it.
