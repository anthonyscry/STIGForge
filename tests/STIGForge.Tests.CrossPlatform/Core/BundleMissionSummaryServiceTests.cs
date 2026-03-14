using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

/// <summary>
/// Cross-platform tests for BundleMissionSummaryService.
/// Focused on LoadSummary (file system paths), NormalizeStatus, and LoadTimelineSummaryAsync.
/// </summary>
public sealed class BundleMissionSummaryServiceTests
{
    // ── NormalizeStatus ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Open")]
    [InlineData("", "Open")]
    [InlineData("pass", "Pass")]
    [InlineData("NotAFinding", "Pass")]
    [InlineData("compliant", "Pass")]
    [InlineData("closed", "Pass")]
    [InlineData("fail", "Fail")]
    [InlineData("noncompliant", "Fail")]
    [InlineData("NotApplicable", "NotApplicable")]
    [InlineData("NA", "NotApplicable")]
    [InlineData("open", "Open")]
    [InlineData("NotReviewed", "Open")]
    [InlineData("notchecked", "Open")]
    [InlineData("error", "Open")]
    [InlineData("informational", "Open")]
    [InlineData("anything_else", "Open")]
    public void NormalizeStatus_VariousInputs_ReturnsExpected(string? input, string expected)
    {
        var sut = new BundleMissionSummaryService();
        sut.NormalizeStatus(input).Should().Be(expected);
    }

    // ── LoadSummary: argument validation ──────────────────────────────────────

    [Fact]
    public void LoadSummary_NullBundleRoot_Throws()
    {
        var sut = new BundleMissionSummaryService();
        var act = () => sut.LoadSummary(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadSummary_WhitespaceBundleRoot_Throws()
    {
        var sut = new BundleMissionSummaryService();
        var act = () => sut.LoadSummary("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LoadSummary_NonExistentDirectory_Throws()
    {
        var sut = new BundleMissionSummaryService();
        var act = () => sut.LoadSummary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    // ── LoadSummary: empty bundle ──────────────────────────────────────────────

    [Fact]
    public void LoadSummary_EmptyBundle_ReturnsDefaults()
    {
        using var tmp = new TempDirectory();
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.BundleRoot.Should().Be(tmp.Path);
        summary.TotalControls.Should().Be(0);
        summary.ManualControls.Should().Be(0);
        summary.AutoControls.Should().Be(0);
        summary.PackName.Should().Be("unknown");
        summary.ProfileName.Should().Be("unknown");
        summary.Verify.TotalCount.Should().Be(0);
        summary.Manual.TotalCount.Should().Be(0);
    }

    // ── LoadSummary: with manifest ─────────────────────────────────────────────

    [Fact]
    public void LoadSummary_WithManifest_ReadsPackAndProfileName()
    {
        using var tmp = new TempDirectory();
        WriteManifest(tmp.Path, "MyPack", "MyProfile");
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.PackName.Should().Be("MyPack");
        summary.ProfileName.Should().Be("MyProfile");
    }

    [Fact]
    public void LoadSummary_CorruptManifest_AddsDiagnostic()
    {
        using var tmp = new TempDirectory();
        var manifestDir = Path.Combine(tmp.Path, "Manifest");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{ broken json !!!");
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Diagnostics.Should().Contain(d => d.Contains("manifest.json"));
    }

    // ── LoadSummary: with controls ─────────────────────────────────────────────

    [Fact]
    public void LoadSummary_WithControls_PopulatesControlCounts()
    {
        using var tmp = new TempDirectory();
        WritePackControls(tmp.Path,
            ("V-001", "SV-001", true),
            ("V-002", "SV-002", false),
            ("V-003", "SV-003", false));
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.TotalControls.Should().Be(3);
        summary.ManualControls.Should().Be(1);
        summary.AutoControls.Should().Be(2);
    }

    [Fact]
    public void LoadSummary_CorruptPackControls_AddsDiagnosticAndZeroCounts()
    {
        using var tmp = new TempDirectory();
        var manifestDir = Path.Combine(tmp.Path, "Manifest");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "pack_controls.json"), "{ not an array }");
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.TotalControls.Should().Be(0);
        summary.Diagnostics.Should().Contain(d => d.Contains("pack_controls.json"));
    }

    // ── LoadSummary: verify reports ────────────────────────────────────────────

    [Fact]
    public void LoadSummary_NoVerifyDir_ZeroVerifyStats()
    {
        using var tmp = new TempDirectory();
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Verify.TotalCount.Should().Be(0);
        summary.Verify.ReportCount.Should().Be(0);
    }

    [Fact]
    public void LoadSummary_WithVerifyReport_CountsResults()
    {
        using var tmp = new TempDirectory();
        WriteVerifyReport(tmp.Path,
            ("NotAFinding", false),
            ("Open", false),
            ("Not_Applicable", false));
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Verify.TotalCount.Should().Be(3);
        summary.Verify.ClosedCount.Should().Be(2); // Pass + NotApplicable
        summary.Verify.OpenCount.Should().Be(1);
        summary.Verify.ReportCount.Should().Be(1);
    }

    [Fact]
    public void LoadSummary_VerifyReportWithBlockingFailures_CountedCorrectly()
    {
        using var tmp = new TempDirectory();
        WriteVerifyReport(tmp.Path,
            ("Open", false),
            ("Fail", false));
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Verify.BlockingFailureCount.Should().Be(2);
    }

    [Fact]
    public void LoadSummary_VerifyReportWithWarnings_CountedAsWarnings()
    {
        using var tmp = new TempDirectory();
        WriteVerifyReport(tmp.Path,
            ("informational", false),
            ("warning", false));
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Verify.RecoverableWarningCount.Should().Be(2);
    }

    [Fact]
    public void LoadSummary_VerifyReportNotApplicable_CountedAsOptionalSkip()
    {
        using var tmp = new TempDirectory();
        WriteVerifyReport(tmp.Path,
            ("Not_Applicable", false),
            ("NA", false));
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Verify.OptionalSkipCount.Should().Be(2);
    }

    [Fact]
    public void LoadSummary_CorruptVerifyReport_AddsDiagnosticAndWarning()
    {
        using var tmp = new TempDirectory();
        var verifyDir = Path.Combine(tmp.Path, "Verify");
        Directory.CreateDirectory(verifyDir);
        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"), "broken");
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Diagnostics.Should().Contain(d => d.Contains("consolidated-results.json"));
        summary.Verify.RecoverableWarningCount.Should().Be(1);
    }

    [Fact]
    public void LoadSummary_VerifyReportMissingResultsArray_AddsDiagnostic()
    {
        using var tmp = new TempDirectory();
        var verifyDir = Path.Combine(tmp.Path, "Verify");
        Directory.CreateDirectory(verifyDir);
        File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
            """{"tool":"T"}""");
        var sut = new BundleMissionSummaryService();

        var summary = sut.LoadSummary(tmp.Path);

        summary.Diagnostics.Should().Contain(d => d.Contains("missing results array"));
    }

    // ── LoadSummary: manual answer progress ───────────────────────────────────

    [Fact]
    public void LoadSummary_WithManualControls_PopulatesManualStats()
    {
        using var tmp = new TempDirectory();
        WritePackControls(tmp.Path,
            ("V-001", "SV-001", true),
            ("V-002", "SV-002", true));

        var manualAnswerService = new ManualAnswerService();
        manualAnswerService.SaveAnswer(tmp.Path, new STIGForge.Core.Models.ManualAnswer
        {
            RuleId = "SV-001",
            Status = "Pass"
        });

        var sut = new BundleMissionSummaryService(manualAnswerService);
        var summary = sut.LoadSummary(tmp.Path);

        summary.Manual.TotalCount.Should().Be(2);
        summary.Manual.PassCount.Should().Be(1);
        summary.Manual.OpenCount.Should().Be(1);
        summary.Manual.PercentComplete.Should().Be(50);
    }

    // ── LoadTimelineSummaryAsync ───────────────────────────────────────────────

    [Fact]
    public async Task LoadTimelineSummaryAsync_NoRepository_ReturnsNull()
    {
        var sut = new BundleMissionSummaryService(missionRunRepo: null);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_NoRuns_ReturnsMessageWithNoLatestRun()
    {
        var repo = new Mock<IMissionRunRepository>();
        repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((MissionRun?)null);

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result.Should().NotBeNull();
        result!.LatestRun.Should().BeNull();
        result.NextAction.Should().Contain("No mission runs");
        result.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_CompletedRun_NextActionIsReview()
    {
        var runId = Guid.NewGuid().ToString("N");
        var repo = BuildRepoWithRun(new MissionRun { RunId = runId, Status = MissionRunStatus.Completed });

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.NextAction.Should().Contain("Mission complete");
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_FailedRun_NextActionIndicatesBlockage()
    {
        var runId = Guid.NewGuid().ToString("N");
        var failedEvent = new MissionTimelineEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            Phase = MissionPhase.Verify,
            StepName = "ScapScan",
            Status = MissionEventStatus.Failed
        };
        var repo = BuildRepoWithRun(
            new MissionRun { RunId = runId, Status = MissionRunStatus.Failed },
            failedEvent);

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.IsBlocked.Should().BeTrue();
        result.NextAction.Should().Contain("blocked");
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_RunningRun_NextActionIndicatesRunning()
    {
        var runId = Guid.NewGuid().ToString("N");
        var evt = new MissionTimelineEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            RunId = runId,
            Phase = MissionPhase.Apply,
            StepName = "GPO",
            Status = MissionEventStatus.Started
        };
        var repo = BuildRepoWithRun(
            new MissionRun { RunId = runId, Status = MissionRunStatus.Running },
            evt);

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.NextAction.Should().Contain("running");
        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_CancelledRun_NextActionIndicatesCancelled()
    {
        var runId = Guid.NewGuid().ToString("N");
        var repo = BuildRepoWithRun(new MissionRun { RunId = runId, Status = MissionRunStatus.Cancelled });

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.NextAction.Should().Contain("cancelled");
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_RepoThrows_ReturnsNull()
    {
        var repo = new Mock<IMissionRunRepository>();
        repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB down"));

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_TimelineRepoThrows_UsesEmptyEvents()
    {
        var runId = Guid.NewGuid().ToString("N");
        var repo = new Mock<IMissionRunRepository>();
        repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MissionRun { RunId = runId, Status = MissionRunStatus.Running });
        repo.Setup(r => r.GetTimelineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new Exception("Timeline unavailable"));

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_PendingRun_NextActionIndicatesPending()
    {
        var runId = Guid.NewGuid().ToString("N");
        var repo = BuildRepoWithRun(new MissionRun { RunId = runId, Status = MissionRunStatus.Pending });

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.NextAction.Should().Contain("pending");
    }

    [Fact]
    public async Task LoadTimelineSummaryAsync_FailedRunNoEvents_GenericFailureMessage()
    {
        var runId = Guid.NewGuid().ToString("N");
        var repo = BuildRepoWithRun(new MissionRun { RunId = runId, Status = MissionRunStatus.Failed });

        var sut = new BundleMissionSummaryService(missionRunRepo: repo.Object);
        using var tmp = new TempDirectory();

        var result = await sut.LoadTimelineSummaryAsync(tmp.Path, CancellationToken.None);

        result!.NextAction.Should().Contain("failed");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IMissionRunRepository> BuildRepoWithRun(
        MissionRun run,
        params MissionTimelineEvent[] events)
    {
        var repo = new Mock<IMissionRunRepository>();
        repo.Setup(r => r.GetLatestRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);
        repo.Setup(r => r.GetTimelineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((IReadOnlyList<MissionTimelineEvent>)events.ToList());
        return repo;
    }

    private static void WriteManifest(string bundleRoot, string packName, string profileName)
    {
        var dir = Path.Combine(bundleRoot, "Manifest");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                run = new { packName, profileName }
            }));
    }

    private static void WritePackControls(
        string bundleRoot,
        params (string VulnId, string RuleId, bool IsManual)[] controls)
    {
        var dir = Path.Combine(bundleRoot, "Manifest");
        Directory.CreateDirectory(dir);
        var list = controls.Select(c => new
        {
            controlId = c.VulnId,
            title = "Test",
            isManual = c.IsManual,
            externalIds = new { ruleId = c.RuleId, vulnId = c.VulnId }
        }).ToArray();
        File.WriteAllText(Path.Combine(dir, "pack_controls.json"), JsonSerializer.Serialize(list));
    }

    private static void WriteVerifyReport(
        string bundleRoot,
        params (string Status, bool IsWarning)[] entries)
    {
        var verifyDir = Path.Combine(bundleRoot, "Verify");
        Directory.CreateDirectory(verifyDir);
        var results = entries.Select(e => new { status = e.Status }).ToArray();
        File.WriteAllText(
            Path.Combine(verifyDir, "consolidated-results.json"),
            JsonSerializer.Serialize(new { results }));
    }
}
