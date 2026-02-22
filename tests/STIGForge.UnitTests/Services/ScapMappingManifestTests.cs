using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

/// <summary>
/// Tests for ScapMappingManifest generation via CanonicalScapSelector.BuildMappingManifest.
/// Validates MAP-01 invariants: single benchmark per STIG, no cross-STIG fallback,
/// unmapped controls surface with explicit reasons.
/// </summary>
public sealed class ScapMappingManifestTests
{
    [Fact]
    public void SingleBenchmarkPerStig_ProducesConsistentMapping()
    {
        // Arrange
        var selector = new CanonicalScapSelector();
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "stig-win11-v2r1",
            StigName = "Microsoft Windows 11 STIG V2R1",
            StigImportedAt = new DateTimeOffset(2026, 2, 14, 0, 0, 0, TimeSpan.Zero),
            StigBenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Windows_11" },
            Candidates = new[]
            {
                new CanonicalScapCandidate
                {
                    PackId = "scap-win11-v2r1",
                    Name = "Microsoft Windows 11 SCAP Benchmark V2R1",
                    SourceLabel = "scap_import",
                    ImportedAt = new DateTimeOffset(2026, 2, 12, 0, 0, 0, TimeSpan.Zero),
                    ReleaseDate = new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
                    BenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Windows_11" }
                },
                new CanonicalScapCandidate
                {
                    PackId = "scap-server2022-v2r1",
                    Name = "Microsoft Windows Server 2022 SCAP V2R1",
                    SourceLabel = "scap_import",
                    ImportedAt = new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero),
                    ReleaseDate = new DateTimeOffset(2026, 2, 9, 0, 0, 0, TimeSpan.Zero),
                    BenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Server_2022" }
                }
            }
        };

        var controls = CreateControls(
            ("V-100001", "SV-100001r1_rule", "xccdf_mil.disa.stig_benchmark_Windows_11"),
            ("V-100002", "SV-100002r1_rule", "xccdf_mil.disa.stig_benchmark_Windows_11"),
            ("V-100003", "SV-100003r1_rule", "xccdf_mil.disa.stig_benchmark_Windows_11"),
            ("V-100004", "SV-100004r1_rule", null),
            ("V-100005", "SV-100005r1_rule", null));

        // Act
        var manifest = selector.BuildMappingManifest(input, controls);

        // Assert
        manifest.StigPackId.Should().Be("stig-win11-v2r1");
        manifest.SelectedBenchmarkPackId.Should().Be("scap-win11-v2r1");
        manifest.ControlMappings.Should().HaveCount(5);

        // Controls with matching benchmark get BenchmarkOverlap
        var mapped = manifest.ControlMappings.Where(m => m.Method == ScapMappingMethod.BenchmarkOverlap).ToList();
        mapped.Should().HaveCount(3);

        // No control should map to the Server 2022 benchmark (no cross-STIG fallback)
        manifest.ControlMappings.Should().NotContain(m =>
            m.BenchmarkId != null && m.BenchmarkId.Contains("Server_2022", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnmappedControls_HaveNoScapMappingReason()
    {
        // Arrange: no candidates at all
        var selector = new CanonicalScapSelector();
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "stig-orphan",
            StigName = "Orphan STIG with no SCAP",
            StigImportedAt = DateTimeOffset.UtcNow,
            StigBenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Orphan" },
            Candidates = Array.Empty<CanonicalScapCandidate>()
        };

        var controls = CreateControls(
            ("V-200001", "SV-200001r1_rule", "xccdf_mil.disa.stig_benchmark_Orphan"),
            ("V-200002", "SV-200002r1_rule", null));

        // Act
        var manifest = selector.BuildMappingManifest(input, controls);

        // Assert
        manifest.SelectedBenchmarkPackId.Should().BeNull();
        manifest.ControlMappings.Should().HaveCount(2);
        manifest.ControlMappings.Should().OnlyContain(m => m.Method == ScapMappingMethod.Unmapped);
        manifest.ControlMappings.Should().OnlyContain(m => m.Reason != null && m.Reason.Contains("no_scap_mapping"));
        manifest.ControlMappings.Should().OnlyContain(m => m.Confidence == 0.0);
        manifest.UnmappedCount.Should().Be(2);
    }

    [Fact]
    public void NoCrossStigFallback_EnforcedWhenWinnerSelected()
    {
        // Arrange: winner has Windows 11 benchmark, some controls have no benchmark match
        var selector = new CanonicalScapSelector();
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "stig-win11-v2r1",
            StigName = "Microsoft Windows 11 STIG V2R1",
            StigImportedAt = DateTimeOffset.UtcNow,
            StigBenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Windows_11" },
            Candidates = new[]
            {
                new CanonicalScapCandidate
                {
                    PackId = "scap-win11-v2r1",
                    Name = "Windows 11 SCAP V2R1",
                    SourceLabel = "scap_import",
                    ImportedAt = DateTimeOffset.UtcNow,
                    BenchmarkIds = new[] { "xccdf_mil.disa.stig_benchmark_Windows_11" }
                }
            }
        };

        var controls = CreateControls(
            ("V-300001", "SV-300001r1_rule", "xccdf_mil.disa.stig_benchmark_Windows_11"),
            ("V-300002", "SV-300002r1_rule", "xccdf_mil.disa.stig_benchmark_Server_2022"),
            ("V-300003", "SV-300003r1_rule", null));

        // Act
        var manifest = selector.BuildMappingManifest(input, controls);

        // Assert
        manifest.SelectedBenchmarkPackId.Should().Be("scap-win11-v2r1");

        // V-300001: matches winner benchmark -> BenchmarkOverlap
        var m1 = manifest.ControlMappings.First(m => m.VulnId == "V-300001");
        m1.Method.Should().Be(ScapMappingMethod.BenchmarkOverlap);
        m1.Confidence.Should().Be(1.0);

        // V-300002: has Server 2022 benchmark, NOT the winner -> must NOT map to a different STIG's benchmark
        var m2 = manifest.ControlMappings.First(m => m.VulnId == "V-300002");
        m2.Method.Should().NotBe(ScapMappingMethod.BenchmarkOverlap,
            because: "controls from a different benchmark must not cross-map");
        // Should be Unmapped or StrictTagMatch, never mapped to Server_2022
        m2.BenchmarkId.Should().NotContain("Server_2022");

        // V-300003: no benchmark at all -> Unmapped
        var m3 = manifest.ControlMappings.First(m => m.VulnId == "V-300003");
        m3.Method.Should().Be(ScapMappingMethod.Unmapped);
    }

    [Fact]
    public void MappingConfidence_MatchesMethod()
    {
        var selector = new CanonicalScapSelector();
        var input = new CanonicalScapSelectionInput
        {
            StigPackId = "stig-test",
            StigName = "Test STIG V1R1",
            StigImportedAt = DateTimeOffset.UtcNow,
            StigBenchmarkIds = new[] { "benchmark_test" },
            Candidates = new[]
            {
                new CanonicalScapCandidate
                {
                    PackId = "scap-test",
                    Name = "Test SCAP V1R1",
                    SourceLabel = "scap_import",
                    ImportedAt = DateTimeOffset.UtcNow,
                    BenchmarkIds = new[] { "benchmark_test" }
                }
            }
        };

        var controls = CreateControls(
            ("V-400001", "SV-400001r1_rule", "benchmark_test"),
            ("V-400002", "SV-400002r1_rule", null));

        var manifest = selector.BuildMappingManifest(input, controls);

        // BenchmarkOverlap -> 1.0
        var overlap = manifest.ControlMappings.First(m => m.Method == ScapMappingMethod.BenchmarkOverlap);
        overlap.Confidence.Should().Be(1.0);

        // Unmapped -> 0.0
        var unmapped = manifest.ControlMappings.First(m => m.Method == ScapMappingMethod.Unmapped);
        unmapped.Confidence.Should().Be(0.0);
    }

    private static IReadOnlyList<ControlRecord> CreateControls(
        params (string VulnId, string RuleId, string? BenchmarkId)[] defs)
    {
        return defs.Select(d => new ControlRecord
        {
            ControlId = d.VulnId,
            Title = $"Test control {d.VulnId}",
            ExternalIds = new ExternalIds
            {
                VulnId = d.VulnId,
                RuleId = d.RuleId,
                BenchmarkId = d.BenchmarkId
            }
        }).ToArray();
    }
}
