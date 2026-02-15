using FluentAssertions;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public sealed class CanonicalScapSelectorTests
{
  [Fact]
  public void Select_PrefersVersionMatch_ThenNiwcEnhanced()
  {
    var selector = new CanonicalScapSelector();
    var input = new CanonicalScapSelectionInput
    {
      StigPackId = "stig-win11-v2r1",
      StigName = "Microsoft Windows 11 STIG V2R1",
      StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
      StigBenchmarkIds = new[] { "benchmarkwin11v2r1" },
      Candidates = new[]
      {
        new CanonicalScapCandidate
        {
          PackId = "scap-standalone",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        },
        new CanonicalScapCandidate
        {
          PackId = "scap-niwc",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1 NIWC Enhanced",
          SourceLabel = "scap_import_consolidated",
          ImportedAt = new DateTimeOffset(2026, 2, 13, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        }
      }
    };

    var result = selector.Select(input);

    result.Winner.Should().NotBeNull();
    result.Winner!.PackId.Should().Be("scap-niwc");
    result.HasConflict.Should().BeTrue();
    result.Reasons.Should().Contain(r => r.Contains("NIWC", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Select_WhenVersionTied_UsesNewestReleaseDate()
  {
    var selector = new CanonicalScapSelector();
    var input = new CanonicalScapSelectionInput
    {
      StigPackId = "stig-win11-v2r1",
      StigName = "Microsoft Windows 11 STIG V2R1",
      StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
      StigBenchmarkIds = new[] { "benchmarkwin11v2r1" },
      Candidates = new[]
      {
        new CanonicalScapCandidate
        {
          PackId = "scap-old",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        },
        new CanonicalScapCandidate
        {
          PackId = "scap-new",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        }
      }
    };

    var result = selector.Select(input);

    result.Winner.Should().NotBeNull();
    result.Winner!.PackId.Should().Be("scap-new");
    result.Reasons.Should().Contain(r => r.Contains("release", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Select_WhenAllSignalsTie_UsesStableLexicalFallback()
  {
    var selector = new CanonicalScapSelector();
    var input = new CanonicalScapSelectionInput
    {
      StigPackId = "stig-win11-v2r1",
      StigName = "Microsoft Windows 11 STIG V2R1",
      StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
      StigBenchmarkIds = new[] { "benchmarkwin11v2r1" },
      Candidates = new[]
      {
        new CanonicalScapCandidate
        {
          PackId = "scap-z",
          Name = "Windows 11 Benchmark",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = null,
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        },
        new CanonicalScapCandidate
        {
          PackId = "scap-a",
          Name = "Windows 11 Benchmark",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = null,
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        }
      }
    };

    var result = selector.Select(input);

    result.Winner.Should().NotBeNull();
    result.Winner!.PackId.Should().Be("scap-a");
    result.Reasons.Should().Contain(r => r.Contains("deterministic", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Select_PrefersBenchmarkOverlapBeforeNiwcPreference()
  {
    var selector = new CanonicalScapSelector();
    var input = new CanonicalScapSelectionInput
    {
      StigPackId = "stig-win11-v2r1",
      StigName = "Microsoft Windows 11 STIG V2R1",
      StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
      StigBenchmarkIds = new[] { "benchmarkwin11v2r1" },
      Candidates = new[]
      {
        new CanonicalScapCandidate
        {
          PackId = "scap-niwc-no-overlap",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1 NIWC Enhanced",
          SourceLabel = "scap_import_consolidated",
          ImportedAt = new DateTimeOffset(2026, 2, 13, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "different-benchmark" }
        },
        new CanonicalScapCandidate
        {
          PackId = "scap-overlap",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        }
      }
    };

    var result = selector.Select(input);

    result.Winner.Should().NotBeNull();
    result.Winner!.PackId.Should().Be("scap-overlap");
    result.Reasons.Should().Contain(r => r.Contains("Benchmark ID overlap", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Select_DoesNotTreatNonNiwcConsolidatedCandidateAsNiwcEnhanced()
  {
    var selector = new CanonicalScapSelector();
    var input = new CanonicalScapSelectionInput
    {
      StigPackId = "stig-win11-v2r1",
      StigName = "Microsoft Windows 11 STIG V2R1",
      StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
      StigBenchmarkIds = new[] { "benchmarkwin11v2r1" },
      Candidates = new[]
      {
        new CanonicalScapCandidate
        {
          PackId = "scap-consolidated",
          Name = "Microsoft Windows 11 SCAP consolidated bundle V2R1",
          SourceLabel = "scap_import_consolidated",
          ImportedAt = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        },
        new CanonicalScapCandidate
        {
          PackId = "scap-standard",
          Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
          SourceLabel = "scap_import",
          ImportedAt = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero),
          ReleaseDate = new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.Zero),
          BenchmarkIds = new[] { "benchmarkwin11v2r1" }
        }
      }
    };

    var result = selector.Select(input);

    result.Winner.Should().NotBeNull();
    result.Winner!.PackId.Should().Be("scap-standard");
    result.Reasons.Should().NotContain(r => r.Contains("NIWC", StringComparison.OrdinalIgnoreCase));
  }
}
