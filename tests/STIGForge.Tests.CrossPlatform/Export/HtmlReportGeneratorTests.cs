using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Export;
using STIGForge.Verify;
using VerifyControlResult = STIGForge.Verify.ControlResult;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class HtmlReportGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public HtmlReportGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "html-report-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ── Bundle construction helpers ──────────────────────────────────────────

    /// <summary>Creates a minimal bundle with a consolidated-results.json under Verify/.</summary>
    private string CreateBundle(IEnumerable<VerifyControlResult> results, string? systemName = null, string? bundleId = null)
    {
        var root = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var verifyDir = Path.Combine(root, "Verify");
        Directory.CreateDirectory(verifyDir);

        var report = new
        {
            Tool = "unit-test",
            ToolVersion = "1.0",
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            OutputRoot = verifyDir,
            Results = results
        };
        File.WriteAllText(
            Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(report));

        if (systemName != null || bundleId != null)
        {
            var manifestDir = Path.Combine(root, "Manifest");
            Directory.CreateDirectory(manifestDir);
            var manifest = new
            {
                bundleId = bundleId ?? "test-bundle",
                run = new { systemName = systemName ?? "Test System" }
            };
            File.WriteAllText(
                Path.Combine(manifestDir, "manifest.json"),
                JsonSerializer.Serialize(manifest));
        }

        return root;
    }

    private static VerifyControlResult Pass(string id = "V-001", string severity = "medium") =>
        new() { VulnId = id, Status = "pass", Severity = severity };

    private static VerifyControlResult Fail(string id = "V-002", string severity = "medium") =>
        new() { VulnId = id, Status = "fail", Severity = severity };

    private static VerifyControlResult NotApplicable(string id = "V-003") =>
        new() { VulnId = id, Status = "notapplicable", Severity = "low" };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildReportData_AudienceExecutive_PopulatesData()
    {
        var bundle = CreateBundle(new[] { Pass(), Fail() });

        var data = HtmlReportGenerator.BuildReportData(bundle, audience: "executive");

        data.Should().NotBeNull();
        data.Audience.Should().Be("executive");
        data.TotalControls.Should().Be(2);
    }

    [Fact]
    public void BuildReportData_NoResults_ZeroCompliancePercent()
    {
        var bundle = CreateBundle(Array.Empty<VerifyControlResult>());

        var data = HtmlReportGenerator.BuildReportData(bundle);

        data.OverallCompliancePercent.Should().Be(0.0);
        data.TotalControls.Should().Be(0);
    }

    [Fact]
    public void BuildReportData_AllPass_HundredPercent()
    {
        var bundle = CreateBundle(new[] { Pass("V-1"), Pass("V-2"), Pass("V-3") });

        var data = HtmlReportGenerator.BuildReportData(bundle);

        data.OverallCompliancePercent.Should().Be(100.0);
        data.PassCount.Should().Be(3);
        data.FailCount.Should().Be(0);
    }

    [Fact]
    public void BuildReportData_MixedResults_CalculatesCorrectPercent()
    {
        // 2 pass, 1 fail → evaluated = 3 → 66.67%
        var bundle = CreateBundle(new[] { Pass("V-1"), Pass("V-2"), Fail("V-3") });

        var data = HtmlReportGenerator.BuildReportData(bundle);

        data.PassCount.Should().Be(2);
        data.FailCount.Should().Be(1);
        data.OverallCompliancePercent.Should().BeApproximately(66.67, 0.01);
    }

    [Fact]
    public void BuildReportData_TrendSnapshots_AreSortedByDate()
    {
        var bundle = CreateBundle(new[] { Pass() });
        var snapshots = new List<ComplianceSnapshot>
        {
            new() { CapturedAt = DateTimeOffset.UtcNow.AddDays(-1), CompliancePercent = 50.0 },
            new() { CapturedAt = DateTimeOffset.UtcNow.AddDays(-5), CompliancePercent = 30.0 },
            new() { CapturedAt = DateTimeOffset.UtcNow,             CompliancePercent = 75.0 },
        };

        var data = HtmlReportGenerator.BuildReportData(bundle, trendSnapshots: snapshots);

        data.TrendData.Should().HaveCount(3);
        data.TrendData.Select(t => t.CompliancePercent)
            .Should().BeInAscendingOrder(Comparer<double>.Create((a, b) =>
                Comparer<DateTimeOffset>.Default.Compare(
                    data.TrendData.First(t => t.CompliancePercent == a).CapturedAt,
                    data.TrendData.First(t => t.CompliancePercent == b).CapturedAt)));
        // Simpler: just verify ordering by date
        data.TrendData[0].CompliancePercent.Should().Be(30.0);
        data.TrendData[1].CompliancePercent.Should().Be(50.0);
        data.TrendData[2].CompliancePercent.Should().Be(75.0);
    }

    [Fact]
    public void BuildReportData_SystemNameOverride_UsesOverride()
    {
        var bundle = CreateBundle(new[] { Pass() }, systemName: "Original System");

        var data = HtmlReportGenerator.BuildReportData(bundle, systemNameOverride: "Override Name");

        data.SystemName.Should().Be("Override Name");
    }

    [Fact]
    public void BuildReportData_SeverityBreakdown_GroupsCorrectly()
    {
        var results = new[]
        {
            Pass("V-1", "high"),
            Fail("V-2", "high"),
            Pass("V-3", "medium"),
            Pass("V-4", "medium"),
            Fail("V-5", "low"),
        };
        var bundle = CreateBundle(results);

        var data = HtmlReportGenerator.BuildReportData(bundle);

        data.Severity.CatITotal.Should().Be(2);   // 2 high
        data.Severity.CatIPass.Should().Be(1);
        data.Severity.CatIFail.Should().Be(1);

        data.Severity.CatIITotal.Should().Be(2);  // 2 medium
        data.Severity.CatIIPass.Should().Be(2);

        data.Severity.CatIIITotal.Should().Be(1); // 1 low
        data.Severity.CatIIIFail.Should().Be(1);
    }
}
