using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class CanonicalScapSelectorTests
{
    private static readonly DateTimeOffset _baseTime = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CanonicalScapSelector _sut = new();

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void Select_NullInput_ThrowsArgumentNullException()
    {
        var act = () => _sut.Select(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("input");
    }

    // ── Empty candidates ──────────────────────────────────────────────────────

    [Fact]
    public void Select_NoCandidates_ReturnsNullWinnerAndNoConflict()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG V1R1",
            Candidates = []
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeNull();
        result.HasConflict.Should().BeFalse();
        result.Reasons.Should().ContainSingle().Which.Should().Contain("No SCAP candidate");
    }

    [Fact]
    public void Select_NullCandidates_ReturnsNullWinner()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            Candidates = null!
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeNull();
    }

    // ── Single candidate ──────────────────────────────────────────────────────

    [Fact]
    public void Select_SingleCandidate_ReturnsThatCandidateAsWinner()
    {
        var candidate = MakeCandidate("c1", "Windows 11 SCAP V1R1");
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG V1R1",
            Candidates = new[] { candidate }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(candidate);
        result.HasConflict.Should().BeFalse();
    }

    // ── Version alignment ─────────────────────────────────────────────────────

    [Fact]
    public void Select_MultipleVersions_PrefersCandidateMatchingStigVersion()
    {
        var v1r1 = MakeCandidate("c1", "Windows 10 SCAP V1R1");
        var v2r7 = MakeCandidate("c2", "Windows 10 SCAP V2R7");

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 10 STIG V2R7",
            Candidates = new[] { v1r1, v2r7 }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(v2r7);
        result.HasConflict.Should().BeTrue();
        result.Reasons.Should().Contain(r => r.Contains("Version-aligned"));
    }

    [Fact]
    public void Select_AllCandidatesSameVersion_NoVersionFilteringReason()
    {
        var c1 = MakeCandidate("c1", "Windows 11 SCAP V2R7");
        var c2 = MakeCandidate("c2", "Windows 11 SCAP V2R7 Enhanced");

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG V2R7",
            Candidates = new[] { c1, c2 }
        };

        var result = _sut.Select(input);

        result.Reasons.Should().NotContain(r => r.Contains("Version-aligned"));
    }

    // ── Benchmark ID overlap scoring ──────────────────────────────────────────

    [Fact]
    public void Select_BenchmarkIdOverlap_PrefersHigherOverlapCandidate()
    {
        var lowOverlap = MakeCandidate("c1", "Generic SCAP", benchmarkIds: new[] { "BENCHMARK_A" });
        var highOverlap = MakeCandidate("c2", "Specific SCAP", benchmarkIds: new[] { "BENCHMARK_A", "BENCHMARK_B" });

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Test STIG",
            StigBenchmarkIds = new[] { "BENCHMARK_A", "BENCHMARK_B" },
            Candidates = new[] { lowOverlap, highOverlap }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(highOverlap);
        result.Reasons.Should().Contain(r => r.Contains("Benchmark ID overlap"));
    }

    [Fact]
    public void Select_NoBenchmarkOverlap_SkipsOverlapFilter()
    {
        var c1 = MakeCandidate("c1", "SCAP A");
        var c2 = MakeCandidate("c2", "SCAP B");

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Test STIG",
            StigBenchmarkIds = [],
            Candidates = new[] { c1, c2 }
        };

        var result = _sut.Select(input);

        result.Winner.Should().NotBeNull();
        result.Reasons.Should().NotContain(r => r.Contains("Benchmark ID overlap"));
    }

    // ── NIWC Enhanced tie-breaking ────────────────────────────────────────────

    [Fact]
    public void Select_NiwcEnhancedCandidateAmongTied_PrefersNiwcEnhanced()
    {
        var plain = MakeCandidate("c1", "Windows 11 SCAP V2R7");
        var niwc = MakeCandidate("c2", "Windows 11 SCAP V2R7 NIWC Enhanced Consolidated Bundle");

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG V2R7",
            Candidates = new[] { plain, niwc }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(niwc);
        result.Reasons.Should().Contain(r => r.Contains("NIWC Enhanced"));
    }

    [Fact]
    public void Select_NiwcCandidateSourceLabel_IsDetectedAsNiwcEnhanced()
    {
        var plain = MakeCandidate("c1", "Windows 11 SCAP V2R7");
        var niwc = MakeCandidate("c2", "Windows 11 SCAP V2R7", sourceLabel: "NIWC Enhanced Bundle SCAP");

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG V2R7",
            Candidates = new[] { plain, niwc }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(niwc);
    }

    // ── Deterministic fallback ────────────────────────────────────────────────

    [Fact]
    public void Select_TwoIdenticalCandidates_FallsBackToDeterministicOrdering()
    {
        var older = MakeCandidate("a_pack", "Windows 11 SCAP",
            releaseDate: _baseTime,
            importedAt: _baseTime);
        var newer = MakeCandidate("b_pack", "Windows 11 SCAP",
            releaseDate: _baseTime.AddDays(30),
            importedAt: _baseTime.AddDays(30));

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { older, newer }
        };

        var result = _sut.Select(input);

        // Newer release date wins
        result.Winner.Should().BeSameAs(newer);
        result.Reasons.Should().Contain(r => r.Contains("Deterministic fallback"));
    }

    [Fact]
    public void Select_SameReleaseDate_BreaksTieByImportDate()
    {
        var older = MakeCandidate("a_pack", "Windows 11 SCAP",
            releaseDate: _baseTime,
            importedAt: _baseTime);
        var newer = MakeCandidate("b_pack", "Windows 11 SCAP",
            releaseDate: _baseTime,
            importedAt: _baseTime.AddHours(1));

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { older, newer }
        };

        var result = _sut.Select(input);

        result.Winner.Should().BeSameAs(newer);
    }

    [Fact]
    public void Select_SameReleaseDateAndImportDate_BreaksTieLexically()
    {
        var c_pack = MakeCandidate("c_pack", "Windows 11 SCAP Z", releaseDate: _baseTime, importedAt: _baseTime);
        var a_pack = MakeCandidate("a_pack", "Windows 11 SCAP A", releaseDate: _baseTime, importedAt: _baseTime);

        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { c_pack, a_pack }
        };

        var result = _sut.Select(input);

        // Lexically first name wins
        result.Winner!.Name.Should().Be("Windows 11 SCAP A");
    }

    // ── HasConflict flag ──────────────────────────────────────────────────────

    [Fact]
    public void Select_MultipleCandidates_SetsHasConflictTrue()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            Candidates = new[]
            {
                MakeCandidate("c1", "SCAP A"),
                MakeCandidate("c2", "SCAP B")
            }
        };

        _sut.Select(input).HasConflict.Should().BeTrue();
    }

    [Fact]
    public void Select_SingleCandidate_HasConflictIsFalse()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            Candidates = new[] { MakeCandidate("c1", "SCAP A") }
        };

        _sut.Select(input).HasConflict.Should().BeFalse();
    }

    // ── BuildMappingManifest ──────────────────────────────────────────────────

    [Fact]
    public void BuildMappingManifest_NoWinner_AllControlsMappedAsUnmapped()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Test STIG",
            Candidates = []
        };
        var controls = new List<ControlRecord>
        {
            MakeControl("V-001", "SV-001", null),
            MakeControl("V-002", "SV-002", null)
        };

        var manifest = _sut.BuildMappingManifest(input, controls);

        manifest.ControlMappings.Should().AllSatisfy(m =>
        {
            m.Method.Should().Be(ScapMappingMethod.Unmapped);
            m.Confidence.Should().Be(0.0);
        });
        manifest.SelectedBenchmarkPackId.Should().BeNull();
    }

    [Fact]
    public void BuildMappingManifest_BenchmarkIdOverlap_MapsWithHighConfidence()
    {
        var winner = MakeCandidate("scap1", "Windows 11 SCAP", benchmarkIds: new[] { "BENCH_WIN11" });
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { winner }
        };
        var controls = new List<ControlRecord>
        {
            MakeControl("V-001", "SV-001", "BENCH_WIN11")
        };

        var manifest = _sut.BuildMappingManifest(input, controls);

        manifest.ControlMappings.Should().ContainSingle();
        var mapping = manifest.ControlMappings[0];
        mapping.Method.Should().Be(ScapMappingMethod.BenchmarkOverlap);
        mapping.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void BuildMappingManifest_NoBenchmarkOverlap_MapsAsUnmapped()
    {
        var winner = MakeCandidate("scap1", "Windows 11 SCAP", benchmarkIds: new[] { "BENCH_WIN11" });
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { winner }
        };
        var controls = new List<ControlRecord>
        {
            MakeControl("V-999", "SV-999", "TOTALLY_DIFFERENT_BENCH")
        };

        var manifest = _sut.BuildMappingManifest(input, controls);

        manifest.ControlMappings[0].Method.Should().Be(ScapMappingMethod.Unmapped);
    }

    [Fact]
    public void BuildMappingManifest_EmptyControls_ReturnsManifestWithNoMappings()
    {
        var winner = MakeCandidate("scap1", "Windows 11 SCAP");
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Windows 11 STIG",
            Candidates = new[] { winner }
        };

        var manifest = _sut.BuildMappingManifest(input, new List<ControlRecord>());

        manifest.ControlMappings.Should().BeEmpty();
        manifest.StigPackId.Should().Be("pack1");
        manifest.SelectedBenchmarkPackId.Should().Be("scap1");
    }

    [Fact]
    public void BuildMappingManifest_GeneratedAtIsRecent()
    {
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            Candidates = []
        };

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var manifest = _sut.BuildMappingManifest(input, new List<ControlRecord>());
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        manifest.GeneratedAt.Should().BeAfter(before).And.BeBefore(after);
    }

    // ── ID normalization (tested indirectly via overlap) ──────────────────────

    [Fact]
    public void Select_BenchmarkIdsWithPunctuation_NormalizedForComparison()
    {
        var candidate = MakeCandidate("c1", "Windows SCAP", benchmarkIds: new[] { "BENCH-WIN-11" });
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "pack1",
            StigName = "Test STIG",
            StigBenchmarkIds = new[] { "BENCH_WIN_11" },
            Candidates = new[] { candidate, MakeCandidate("c2", "Other SCAP") }
        };

        var result = _sut.Select(input);

        // Both normalize to "benchwin11" so overlap exists
        result.Winner.Should().BeSameAs(candidate);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CanonicalScapCandidate MakeCandidate(
        string packId,
        string name,
        string sourceLabel = "",
        IReadOnlyCollection<string>? benchmarkIds = null,
        DateTimeOffset? releaseDate = null,
        DateTimeOffset? importedAt = null) =>
        new()
        {
            PackId = packId,
            Name = name,
            SourceLabel = sourceLabel,
            BenchmarkIds = benchmarkIds ?? [],
            ReleaseDate = releaseDate,
            ImportedAt = importedAt ?? _baseTime
        };

    private static ControlRecord MakeControl(string vulnId, string ruleId, string? benchmarkId) =>
        new()
        {
            ControlId = vulnId,
            ExternalIds = new STIGForge.Core.Models.ExternalIds
            {
                VulnId = vulnId,
                RuleId = ruleId,
                BenchmarkId = benchmarkId
            }
        };
}
