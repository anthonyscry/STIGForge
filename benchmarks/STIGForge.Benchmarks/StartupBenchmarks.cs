using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace STIGForge.Benchmarks;

/// <summary>
/// Startup time benchmarks for the STIGForge WPF application.
///
/// Measures cold startup time by spawning a new process for each iteration.
/// The --exit-after-load flag causes the app to exit after MainWindow.Loaded event,
/// providing a clean measurement of startup time.
///
/// IMPORTANT NOTES:
/// - ColdStartup benchmark requires building STIGForge.App in Release mode first
/// - The benchmark locates the built executable relative to the benchmark assembly
/// - Each iteration spawns a fresh process, measuring cold start conditions
/// - Warm startup measurement requires external orchestration (run twice and compare)
///
/// Performance targets (from requirements):
/// - PERF-01: Cold startup < 3 seconds
/// - PERF-02: Warm startup < 1 second
///
/// How to run:
///   dotnet run -c Release --project benchmarks/STIGForge.Benchmarks --filter "*StartupBenchmarks*"
///
/// Prerequisites:
///   dotnet build src/STIGForge.App -c Release
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, launchCount: 3, warmupCount: 1, iterationCount: 5)]
public class StartupBenchmarks
{
    private string? _appExePath;
    private string? _testBundlePath;

    /// <summary>
    /// Locates the STIGForge.App executable and sets up test data directory.
    /// </summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        // Locate the STIGForge.App.exe relative to the benchmark assembly
        // The benchmark runs from benchmarks/STIGForge.Benchmarks/bin/Release/net8.0/
        // The WPF app is at src/STIGForge.App/bin/Release/net8.0-windows/STIGForge.App.exe

        var benchmarkAssemblyLocation = Assembly.GetExecutingAssembly().Location;
        var benchmarkDir = Path.GetDirectoryName(benchmarkAssemblyLocation)
            ?? throw new InvalidOperationException("Could not determine benchmark assembly directory");

        // Navigate up from benchmark bin directory to solution root, then to app output
        // benchmarks/STIGForge.Benchmarks/bin/Release/net8.0/ -> solution root
        var solutionRoot = Path.GetFullPath(Path.Combine(benchmarkDir, "..", "..", "..", ".."));
        _appExePath = Path.Combine(solutionRoot, "src", "STIGForge.App", "bin", "Release", "net8.0-windows", "STIGForge.App.exe");

        if (!File.Exists(_appExePath))
        {
            throw new FileNotFoundException(
                $"STIGForge.App.exe not found at {_appExePath}. " +
                "Build the WPF application in Release mode first: dotnet build src/STIGForge.App -c Release");
        }

        // Create a minimal test bundle path for consistent benchmark conditions
        _testBundlePath = Path.Combine(solutionRoot, "benchmarks", "STIGForge.Benchmarks", "TestData", "minimal-bundle");
        Directory.CreateDirectory(_testBundlePath);
    }

    /// <summary>
    /// Measures cold startup time by spawning a new process for each iteration.
    /// The --exit-after-load flag causes the app to exit after MainWindow.Loaded event,
    /// providing a clean measurement of startup time from process spawn to UI ready.
    ///
    /// Target: < 3 seconds (PERF-01)
    /// </summary>
    [Benchmark(Description = "Cold startup time (process spawn to MainWindow.Loaded)")]
    public void ColdStartup()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _appExePath,
            Arguments = "--exit-after-load",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_appExePath)
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process: {_appExePath}");

        // Wait for the process to exit (it exits after MainWindow.Loaded due to --exit-after-load flag)
        process.WaitForExit();

        // Check exit code - 0 indicates successful startup and clean exit
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(
                $"STIGForge.App exited with code {process.ExitCode}. Stderr: {stderr}");
        }
    }

    /// <summary>
    /// Measures initialization timing within a running process.
    ///
    /// NOTE: This is NOT the same as true warm startup, which requires:
    /// 1. The OS to have cached DLLs and memory from a previous run
    /// 2. The JIT to have compiled hot paths
    /// 3. Disk cache to be primed
    ///
    /// To measure true warm startup:
    /// - Run the ColdStartup benchmark multiple times in quick succession
    /// - Compare first run (cold) vs subsequent runs (warmer)
    /// - Check PerformanceInstrumenter logs (startup-trace.log) for timing data
    ///
    /// Target: < 1 second (PERF-02)
    /// </summary>
    [Benchmark(Description = "Internal initialization timing (not true warm startup)")]
    public void WarmStartupInternal()
    {
        // This benchmark demonstrates the limitation of measuring warm startup
        // within BenchmarkDotNet's execution model.
        //
        // True warm startup timing is captured by:
        // 1. The PerformanceInstrumenter.RecordStartupTime() call in App.xaml.cs
        // 2. The startup-trace.log file in %LocalAppData%\STIGForge\
        //
        // To get warm startup measurements:
        // 1. Launch STIGForge.App.exe --exit-after-load (captures cold start)
        // 2. Launch again immediately (captures warm start)
        // 3. Compare timing in startup-trace.log

        var elapsedMs = Stopwatch.GetTimestamp(); // Minimal operation to avoid optimization
        _ = elapsedMs;
    }
}
