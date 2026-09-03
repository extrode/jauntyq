# JauntyQ Benchmarks

BenchmarkDotNet suite backing the performance claims in the top-level README.
All read benchmarks run against real in-memory SQLite (no Docker), using the
generated `Widgets` data access so they measure the actual emitted code path.

## Run

```bash
# All benchmarks (full run — takes several minutes)
dotnet run -c Release --project benchmarks/Extrode.JauntyQ.Benchmarks

# One group
dotnet run -c Release --project benchmarks/Extrode.JauntyQ.Benchmarks -- --filter '*ReadBenchmarks*'

# Fast smoke run (3 iterations, not for publishable numbers)
dotnet run -c Release --project benchmarks/Extrode.JauntyQ.Benchmarks -- --filter '*' --job short
```

Release configuration is required; BenchmarkDotNet refuses to run under Debug.

## What's measured

- **ReadBenchmarks** — buffered `List<T>` materialization the generator emits
  (ordinal reads, no reflection): full read, read-and-aggregate, and single-row
  `@first`, across result-set sizes (10 / 1,000 / 100,000 rows).
- **StreamBenchmarks** — the `@stream` `IAsyncEnumerable<T>` path vs the buffered
  path, highlighting constant-memory consumption of large result sets.
- **TokenizerBenchmarks** — build-time hot path (the generator re-tokenizes
  `.sql` on every design-time build); validates the PERF-2/PERF-5 allocation
  fixes.

Results are written to `BenchmarkDotNet.Artifacts/` (gitignored). Copy the
GitHub-markdown report into `docs/` when publishing numbers.
