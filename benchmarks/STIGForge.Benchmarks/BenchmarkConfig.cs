using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Exporters;

namespace STIGForge.Benchmarks;

/// <summary>
/// Benchmark configuration for STIGForge performance measurements.
/// Configures BenchmarkDotNet with appropriate settings for .NET 8 applications.
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Add a simple job targeting .NET 8 runtime
        AddJob(Job.ShortRun
            .WithWarmupCount(2)
            .WithIterationCount(10));

        // Include key columns for performance analysis
        AddColumnProvider(
            DefaultColumnProviders.Descriptor,   // Method name, categories
            DefaultColumnProviders.Job,          // Job name, runtime
            DefaultColumnProviders.Statistics,   // Mean, StdErr, StdDev
            DefaultColumnProviders.Params);      // Parameter values

        // Enable memory diagnostics
        AddDiagnoser(BenchmarkDotNet.Diagnosers.MemoryDiagnoser.Default);

        // Use markdown exporter for documentation
        AddExporter(MarkdownExporter.GitHub);
    }
}
