using BenchmarkDotNet.Running;

// Run all benchmarks: dotnet run -c Release --project benchmarks/JauntyQ.Benchmarks
// Filter one:        dotnet run -c Release --project benchmarks/JauntyQ.Benchmarks -- --filter *ReadBenchmarks*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal partial class Program { }
