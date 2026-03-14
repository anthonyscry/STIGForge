using System.Text.Json;
using FluentAssertions;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class FleetSummaryServiceTests
{
    private readonly FleetSummaryService _sut = new();

    // ── GenerateSummary ────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSummary_DirectoryNotFound_Throws()
    {
        var act = () => _sut.GenerateSummary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void GenerateSummary_EmptyFleet_ReturnsZeroComplianceAndEmptyCollections()
    {
        using var tmp = new TempDirectory();

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.FleetWideCompliance.Should().Be(0);
        summary.PerHostStats.Should().BeEmpty();
        summary.HostNames.Should().BeEmpty();
        summary.FailingControls.Should().BeEmpty();
        summary.ControlStatusMatrix.Should().BeEmpty();
    }

    [Fact]
    public void GenerateSummary_HostWithNoVerifyDir_ZeroControlsEntry()
    {
        using var tmp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "host1"));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.HostNames.Should().ContainSingle().Which.Should().Be("host1");
        summary.PerHostStats.Should().ContainSingle(h => h.HostName == "host1" && h.TotalControls == 0);
    }

    [Fact]
    public void GenerateSummary_AllPassControls_Returns100Percent()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "Title 1", "NotAFinding"),
            ("V-002", "SV-002", "Title 2", "pass"));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.FleetWideCompliance.Should().Be(100);
        var stats = summary.PerHostStats.Single();
        stats.PassCount.Should().Be(2);
        stats.FailCount.Should().Be(0);
        stats.CompliancePercentage.Should().Be(100);
    }

    [Fact]
    public void GenerateSummary_MixedStatuses_CountsCorrectly()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"),   // Pass
            ("V-002", "SV-002", "T2", "Open"),           // Fail
            ("V-003", "SV-003", "T3", "Not_Applicable"), // NA
            ("V-004", "SV-004", "T4", null));             // NR

        var summary = _sut.GenerateSummary(tmp.Path);

        var stats = summary.PerHostStats.Single();
        stats.PassCount.Should().Be(1);
        stats.FailCount.Should().Be(1);
        stats.NaCount.Should().Be(1);
        stats.NrCount.Should().Be(1);
        // compliance = 1 pass / (1 pass + 1 fail) = 50%
        stats.CompliancePercentage.Should().Be(50);
    }

    [Fact]
    public void GenerateSummary_OnlyNaAndNr_ZeroCompliancePercentage()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "Not_Applicable"),
            ("V-002", "SV-002", "T2", null));

        var summary = _sut.GenerateSummary(tmp.Path);

        // no pass or fail → applicable = 0 → compliance = 0
        summary.PerHostStats.Single().CompliancePercentage.Should().Be(0);
        summary.FleetWideCompliance.Should().Be(0);
    }

    [Fact]
    public void GenerateSummary_MultipleHosts_BuildsControlMatrix()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "alpha",
            ("V-001", "SV-001", "T1", "NotAFinding"));
        CreateHostVerifyReport(tmp.Path, "beta",
            ("V-001", "SV-001", "T1", "Open"));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.HostNames.Should().HaveCount(2);
        summary.ControlStatusMatrix.Should().ContainKey("V-001");
        summary.ControlStatusMatrix["V-001"]["alpha"].Should().Be("Pass");
        summary.ControlStatusMatrix["V-001"]["beta"].Should().Be("Fail");
    }

    [Fact]
    public void GenerateSummary_MultipleHosts_WeightedFleetCompliance()
    {
        using var tmp = new TempDirectory();
        // alpha: 2 pass → 100%
        CreateHostVerifyReport(tmp.Path, "alpha",
            ("V-001", "SV-001", "T1", "NotAFinding"),
            ("V-002", "SV-002", "T2", "NotAFinding"));
        // beta: 2 fail → 0%
        CreateHostVerifyReport(tmp.Path, "beta",
            ("V-001", "SV-001", "T1", "Open"),
            ("V-002", "SV-002", "T2", "Open"));

        var summary = _sut.GenerateSummary(tmp.Path);

        // total: 2 pass, 2 fail → 50%
        summary.FleetWideCompliance.Should().Be(50);
    }

    [Fact]
    public void GenerateSummary_MultipleHostsSameFailingControl_PopulatesFailingControls()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "alpha",
            ("V-001", "SV-001", "T1", "Open"));
        CreateHostVerifyReport(tmp.Path, "beta",
            ("V-001", "SV-001", "T1", "Open"));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.FailingControls.Should().ContainSingle(fc =>
            fc.ControlId == "V-001" && fc.AffectedCount == 2);
        summary.FailingControls[0].AffectedHosts.Should().Contain("alpha").And.Contain("beta");
    }

    [Fact]
    public void GenerateSummary_PassingControlsNotInFailingList()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"),
            ("V-002", "SV-002", "T2", "Open"));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.FailingControls.Should().ContainSingle(fc => fc.ControlId == "V-001" || fc.ControlId == "V-002");
        summary.FailingControls.Should().NotContain(fc => fc.ControlId == "V-001" && fc.AffectedCount > 0);
    }

    [Fact]
    public void GenerateSummary_FallsBackToRuleIdWhenNoVulnId()
    {
        using var tmp = new TempDirectory();
        var verifyDir = Path.Combine(tmp.Path, "host1", "Verify");
        Directory.CreateDirectory(verifyDir);
        var report = new
        {
            tool = "TestTool",
            toolVersion = "1.0",
            startedAt = DateTimeOffset.UtcNow,
            finishedAt = DateTimeOffset.UtcNow,
            outputRoot = verifyDir,
            results = new[]
            {
                new { vulnId = (string?)null, ruleId = "SV-999", title = "RuleOnly", status = "Open", tool = "TestTool", sourceFile = "f.ckl" }
            }
        };
        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(report));

        var summary = _sut.GenerateSummary(tmp.Path);

        summary.ControlStatusMatrix.Should().ContainKey("SV-999");
    }

    // ── GenerateFleetPoam ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateFleetPoam_DirectoryNotFound_Throws()
    {
        var act = () => _sut.GenerateFleetPoam(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "Sys");
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void GenerateFleetPoam_EmptyFleet_ReturnsEmptyPoam()
    {
        using var tmp = new TempDirectory();

        var poam = _sut.GenerateFleetPoam(tmp.Path, "TestSystem");

        poam.Items.Should().BeEmpty();
        poam.Summary.TotalFindings.Should().Be(0);
        poam.Summary.SystemName.Should().Be("TestSystem");
    }

    [Fact]
    public void GenerateFleetPoam_OnlyPassingControls_EmptyPoam()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"));

        var poam = _sut.GenerateFleetPoam(tmp.Path, "Sys");

        poam.Items.Should().BeEmpty();
    }

    [Fact]
    public void GenerateFleetPoam_FailingControl_CreatesPoamItem()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "Open"));

        var poam = _sut.GenerateFleetPoam(tmp.Path, "Sys");

        poam.Items.Should().ContainSingle(i => i.ControlId == "V-001");
        poam.Summary.TotalFindings.Should().Be(1);
    }

    [Fact]
    public void GenerateFleetPoam_FailingControlOnMultipleHosts_AggregatesHostsAffected()
    {
        using var tmp = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "alpha",
            ("V-001", "SV-001", "T1", "Open"));
        CreateHostVerifyReport(tmp.Path, "beta",
            ("V-001", "SV-001", "T1", "Open"));

        var poam = _sut.GenerateFleetPoam(tmp.Path, "Sys");

        poam.Items.Should().ContainSingle();
        poam.Items[0].HostsAffected.Should().Contain("alpha").And.Contain("beta");
    }

    [Fact]
    public void GenerateFleetPoam_HighSeverityControl_CountedInCritical()
    {
        using var tmp = new TempDirectory();
        var verifyDir = Path.Combine(tmp.Path, "host1", "Verify");
        Directory.CreateDirectory(verifyDir);
        var report = new
        {
            tool = "T",
            toolVersion = "1.0",
            startedAt = DateTimeOffset.UtcNow,
            finishedAt = DateTimeOffset.UtcNow,
            outputRoot = verifyDir,
            results = new[]
            {
                new { vulnId = "V-001", ruleId = "SV-001", title = "High Sev", severity = "high", status = "Open", tool = "T", sourceFile = "f.ckl" }
            }
        };
        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(report));

        var poam = _sut.GenerateFleetPoam(tmp.Path, "Sys");

        poam.Summary.CriticalFindings.Should().Be(1);
    }

    // ── WriteSummaryFiles ──────────────────────────────────────────────────────

    [Fact]
    public void WriteSummaryFiles_CreatesAllThreeFiles()
    {
        using var tmp = new TempDirectory();
        using var outDir = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"));
        var summary = _sut.GenerateSummary(tmp.Path);

        _sut.WriteSummaryFiles(summary, outDir.Path);

        File.Exists(Path.Combine(outDir.Path, "fleet_summary.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir.Path, "fleet_summary.csv")).Should().BeTrue();
        File.Exists(Path.Combine(outDir.Path, "fleet_summary.txt")).Should().BeTrue();
    }

    [Fact]
    public void WriteSummaryFiles_JsonContainsCamelCaseFleetCompliance()
    {
        using var tmp = new TempDirectory();
        using var outDir = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"));
        var summary = _sut.GenerateSummary(tmp.Path);

        _sut.WriteSummaryFiles(summary, outDir.Path);

        var json = File.ReadAllText(Path.Combine(outDir.Path, "fleet_summary.json"));
        json.Should().Contain("fleetWideCompliance");
    }

    [Fact]
    public void WriteSummaryFiles_CsvContainsControlIdHeaderAndHostColumn()
    {
        using var tmp = new TempDirectory();
        using var outDir = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "NotAFinding"));
        var summary = _sut.GenerateSummary(tmp.Path);

        _sut.WriteSummaryFiles(summary, outDir.Path);

        var csv = File.ReadAllText(Path.Combine(outDir.Path, "fleet_summary.csv"));
        csv.Should().Contain("ControlId").And.Contain("host1");
    }

    [Fact]
    public void WriteSummaryFiles_TxtContainsFleetHeader()
    {
        using var tmp = new TempDirectory();
        using var outDir = new TempDirectory();
        CreateHostVerifyReport(tmp.Path, "host1",
            ("V-001", "SV-001", "T1", "Open"));
        var summary = _sut.GenerateSummary(tmp.Path);

        _sut.WriteSummaryFiles(summary, outDir.Path);

        var txt = File.ReadAllText(Path.Combine(outDir.Path, "fleet_summary.txt"));
        txt.Should().Contain("FLEET COMPLIANCE SUMMARY").And.Contain("Fleet-Wide Failing Controls");
    }

    [Fact]
    public void WriteSummaryFiles_CsvMissingHostStatusWritesNR()
    {
        using var tmp = new TempDirectory();
        using var outDir = new TempDirectory();
        // host1 has V-001; host2 has V-002 — each host is missing the other's control
        CreateHostVerifyReport(tmp.Path, "host1", ("V-001", "SV-001", "T1", "NotAFinding"));
        CreateHostVerifyReport(tmp.Path, "host2", ("V-002", "SV-002", "T2", "NotAFinding"));
        var summary = _sut.GenerateSummary(tmp.Path);

        _sut.WriteSummaryFiles(summary, outDir.Path);

        var csv = File.ReadAllText(Path.Combine(outDir.Path, "fleet_summary.csv"));
        csv.Should().Contain("NR");
    }

    // ── GeneratePerHostCkl (smoke test — best effort, no CKL files) ───────────

    [Fact]
    public void GeneratePerHostCkl_NonExistentRoot_DoesNotThrow()
    {
        var act = () => FleetSummaryService.GeneratePerHostCkl(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        act.Should().NotThrow();
    }

    [Fact]
    public void GeneratePerHostCkl_EmptyFleet_DoesNotThrow()
    {
        using var tmp = new TempDirectory();
        var act = () => FleetSummaryService.GeneratePerHostCkl(tmp.Path);
        act.Should().NotThrow();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void CreateHostVerifyReport(
        string fleetRoot,
        string hostName,
        params (string VulnId, string? RuleId, string Title, string? Status)[] controls)
    {
        var verifyDir = Path.Combine(fleetRoot, hostName, "Verify");
        Directory.CreateDirectory(verifyDir);

        var results = controls.Select(c => new
        {
            vulnId = c.VulnId,
            ruleId = c.RuleId,
            title = c.Title,
            status = c.Status,
            tool = "TestTool",
            sourceFile = "test.ckl"
        }).ToArray();

        var report = new
        {
            tool = "TestTool",
            toolVersion = "1.0",
            startedAt = DateTimeOffset.UtcNow,
            finishedAt = DateTimeOffset.UtcNow,
            outputRoot = verifyDir,
            results
        };

        File.WriteAllText(
            Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(report));
    }
}
