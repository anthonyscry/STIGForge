using BenchmarkDotNet.Running;

// BenchmarkDotNet Entry Point
//
// How to run benchmarks:
//   dotnet run -c Release --filter "*"
//   dotnet run -c Release --filter "*StartupBenchmarks*"
//   dotnet run -c Release --filter "* --job short"  (for quick iterations)
//
// Results are saved to BenchmarkDotNet.Artifacts/ by default.
// Add --runtimes net8.0 to test multiple runtimes.

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

return 0;
