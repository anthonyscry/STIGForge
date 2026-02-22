using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

/// <summary>
/// Tests for VerifyOrchestrator.ApplyMappingManifest - enriching verification results
/// with BenchmarkId and mapping status from ScapMappingManifest.
/// </summary>
public sealed class VerifyOrchestratorMappingTests
{
    [Fact]
    public void MappingManifest_AssociatesResultsPerStig()
    {
        // Arrange
        var orchestrator = new VerifyOrchestrator();
        var manifest = new ScapMappingManifest
        {
            StigPackId = "stig-win11",
            StigName = "Windows 11 STIG",
            SelectedBenchmarkPackId = "scap-win11",
            SelectedBenchmarkName = "Win11 SCAP Benchmark",
            ControlMappings = new[]
            {
                new ScapControlMapping { VulnId = "V-100001", RuleId = "SV-100001r1_rule", BenchmarkId = "benchmark_win11", Method = ScapMappingMethod.BenchmarkOverlap, Confidence = 1.0 },
                new ScapControlMapping { VulnId = "V-100002", RuleId = "SV-100002r1_rule", BenchmarkId = "benchmark_win11", Method = ScapMappingMethod.BenchmarkOverlap, Confidence = 1.0 },
                new ScapControlMapping { VulnId = "V-100003", RuleId = "SV-100003r1_rule", BenchmarkId = "benchmark_win11", Method = ScapMappingMethod.StrictTagMatch, Confidence = 0.7 }
            }
        };

        var report = CreateReport(
            ("V-100001", "SV-100001r1_rule", VerifyStatus.Pass),
            ("V-100002", "SV-100002r1_rule", VerifyStatus.Fail),
            ("V-100003", "SV-100003r1_rule", VerifyStatus.Pass));

        // Act
        orchestrator.ApplyMappingManifest(report, manifest);

        // Assert
        report.Results[0].BenchmarkId.Should().Be("benchmark_win11");
        report.Results[1].BenchmarkId.Should().Be("benchmark_win11");
        report.Results[2].BenchmarkId.Should().Be("benchmark_win11");
    }

    [Fact]
    public void UnmappedControls_IncludeNoScapMappingReason()
    {
        // Arrange
        var orchestrator = new VerifyOrchestrator();
        var manifest = new ScapMappingManifest
        {
            StigPackId = "stig-orphan",
            StigName = "Orphan STIG",
            ControlMappings = new[]
            {
                new ScapControlMapping { VulnId = "V-200001", RuleId = "SV-200001r1_rule", Method = ScapMappingMethod.Unmapped, Confidence = 0.0, Reason = "no_scap_mapping" },
                new ScapControlMapping { VulnId = "V-200002", RuleId = "SV-200002r1_rule", Method = ScapMappingMethod.Unmapped, Confidence = 0.0, Reason = "no_scap_mapping" }
            }
        };

        var report = CreateReport(
            ("V-200001", "SV-200001r1_rule", VerifyStatus.Pass),
            ("V-200002", "SV-200002r1_rule", VerifyStatus.Fail));

        // Act
        orchestrator.ApplyMappingManifest(report, manifest);

        // Assert
        report.Results[0].BenchmarkId.Should().BeNull();
        report.Results[0].Metadata.Should().ContainKey("mapping_status");
        report.Results[0].Metadata["mapping_status"].Should().Be("no_scap_mapping");

        report.Results[1].BenchmarkId.Should().BeNull();
        report.Results[1].Metadata["mapping_status"].Should().Be("no_scap_mapping");
    }

    [Fact]
    public void NullManifest_PreservesExistingBehavior()
    {
        // Arrange
        var orchestrator = new VerifyOrchestrator();
        var report = CreateReport(
            ("V-300001", "SV-300001r1_rule", VerifyStatus.Pass));

        var originalBenchmarkId = report.Results[0].BenchmarkId;
        var originalMetadataCount = report.Results[0].Metadata.Count;

        // Act
        orchestrator.ApplyMappingManifest(report, null);

        // Assert: nothing changed
        report.Results[0].BenchmarkId.Should().Be(originalBenchmarkId);
        report.Results[0].Metadata.Count.Should().Be(originalMetadataCount);
    }

    [Fact]
    public void ExistingMergePrecedence_PreservedWithMapping()
    {
        // Arrange: CKL (pass) and SCAP (fail) results for same control
        var orchestrator = new VerifyOrchestrator();

        var cklResult = new NormalizedVerifyResult
        {
            ControlId = "V-400001",
            VulnId = "V-400001",
            RuleId = "SV-400001r1_rule",
            Status = VerifyStatus.Pass,
            Tool = "Manual CKL",
            SourceFile = "test.ckl",
            VerifiedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>()
        };

        var scapResult = new NormalizedVerifyResult
        {
            ControlId = "V-400001",
            VulnId = "V-400001",
            RuleId = "SV-400001r1_rule",
            Status = VerifyStatus.Fail,
            Tool = "SCAP",
            SourceFile = "scap_result.xml",
            VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Metadata = new Dictionary<string, string>()
        };

        var cklReport = new NormalizedVerifyReport
        {
            Tool = "Manual CKL",
            Results = new[] { cklResult },
            Summary = new VerifySummary { TotalCount = 1, PassCount = 1 }
        };

        var scapReport = new NormalizedVerifyReport
        {
            Tool = "SCAP",
            Results = new[] { scapResult },
            Summary = new VerifySummary { TotalCount = 1, FailCount = 1 }
        };

        // Merge
        var merged = orchestrator.MergeReports(
            new[] { cklReport, scapReport },
            Array.Empty<string>());

        // CKL should win (higher precedence: Manual CKL > SCAP)
        var mergedResult = merged.Results.First(r => r.ControlId == "V-400001" || r.VulnId == "V-400001");
        mergedResult.Status.Should().Be(VerifyStatus.Pass,
            because: "Manual CKL overrides SCAP per merge precedence rules");

        // Now apply mapping manifest
        var manifest = new ScapMappingManifest
        {
            StigPackId = "stig-test",
            ControlMappings = new[]
            {
                new ScapControlMapping { VulnId = "V-400001", RuleId = "SV-400001r1_rule", BenchmarkId = "benchmark_test", Method = ScapMappingMethod.BenchmarkOverlap, Confidence = 1.0 }
            }
        };

        orchestrator.ApplyMappingManifest(merged, manifest);

        // Assert: CKL still wins AND BenchmarkId is populated
        mergedResult = merged.Results.First(r => r.ControlId == "V-400001" || r.VulnId == "V-400001");
        mergedResult.Status.Should().Be(VerifyStatus.Pass,
            because: "merge precedence must be preserved even after mapping manifest application");
        mergedResult.BenchmarkId.Should().Be("benchmark_test",
            because: "mapping manifest should populate BenchmarkId on merged results");
    }

    [Fact]
    public void ResultsNotInManifest_GetNotInManifestStatus()
    {
        // Arrange: manifest has no mapping for V-500001
        var orchestrator = new VerifyOrchestrator();
        var manifest = new ScapMappingManifest
        {
            StigPackId = "stig-test",
            ControlMappings = new[]
            {
                new ScapControlMapping { VulnId = "V-999999", RuleId = "SV-999999r1_rule", BenchmarkId = "benchmark_test", Method = ScapMappingMethod.BenchmarkOverlap, Confidence = 1.0 }
            }
        };

        var report = CreateReport(("V-500001", "SV-500001r1_rule", VerifyStatus.Pass));

        // Act
        orchestrator.ApplyMappingManifest(report, manifest);

        // Assert
        report.Results[0].BenchmarkId.Should().BeNull();
        report.Results[0].Metadata.Should().ContainKey("mapping_status");
        report.Results[0].Metadata["mapping_status"].Should().Be("not_in_manifest");
    }

    private static ConsolidatedVerifyReport CreateReport(
        params (string VulnId, string RuleId, VerifyStatus Status)[] defs)
    {
        var results = defs.Select(d => new NormalizedVerifyResult
        {
            ControlId = d.VulnId,
            VulnId = d.VulnId,
            RuleId = d.RuleId,
            Status = d.Status,
            Tool = "SCAP",
            SourceFile = "test_result.xml",
            Metadata = new Dictionary<string, string>()
        }).ToList();

        return new ConsolidatedVerifyReport
        {
            MergedAt = DateTimeOffset.UtcNow,
            Results = results,
            Summary = new VerifySummary { TotalCount = results.Count }
        };
    }
}
