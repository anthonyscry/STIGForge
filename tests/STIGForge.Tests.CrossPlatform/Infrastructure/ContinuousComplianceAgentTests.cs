using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

/// <summary>
/// Tests for the severity gate / ExcludeBaselineEstablishedEvents filtering in
/// ContinuousComplianceAgent.RunComplianceCheckAsync (exercised via StartAsync).
///
/// Setup produces exactly 2 drift events per run:
///   - V-Rule-1: BaselineEstablished (no prior baseline in repo)
///   - V-Rule-2: StateChanged       (prior baseline "Pass", current scan "fail")
/// </summary>
public class ContinuousComplianceAgentTests : IDisposable
{
    private readonly string _bundleRoot;
    private readonly Mock<IDriftRepository> _mockDriftRepo;
    private readonly Mock<IAuditTrailService> _mockAudit;

    public ContinuousComplianceAgentTests()
    {
        _bundleRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(_bundleRoot, "Verify"));

        // V-Rule-1 (pass) has no prior baseline → BaselineEstablished event
        // V-Rule-2 (fail) has prior baseline "Pass" → StateChanged event
        File.WriteAllText(
            Path.Combine(_bundleRoot, "Verify", "consolidated-results.json"),
            """{"results":[{"ruleId":"V-Rule-1","status":"pass"},{"ruleId":"V-Rule-2","status":"fail"}]}""");

        _mockDriftRepo = new Mock<IDriftRepository>();
        _mockDriftRepo
            .Setup(r => r.GetLatestByRuleAsync(_bundleRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DriftSnapshot>
            {
                new DriftSnapshot
                {
                    SnapshotId = Guid.NewGuid().ToString("N"),
                    RuleId = "V-Rule-2",
                    BundleRoot = _bundleRoot,
                    CurrentState = "Pass",
                    ChangeType = DriftChangeTypes.BaselineEstablished,
                    DetectedAt = DateTimeOffset.UtcNow
                }
            });
        _mockDriftRepo
            .Setup(r => r.SaveBatchAsync(It.IsAny<IReadOnlyList<DriftSnapshot>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockAudit = new Mock<IAuditTrailService>();
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleRoot, recursive: true); } catch { }
    }

    private ContinuousComplianceAgent CreateAgent(bool? excludeBaseline, bool withAudit = true)
    {
        var driftService = new DriftDetectionService(_mockDriftRepo.Object);
        var config = excludeBaseline.HasValue
            ? new ComplianceAgentConfig
            {
                BundleRoot = _bundleRoot,
                CheckIntervalMinutes = 1440,
                ExcludeBaselineEstablishedEvents = excludeBaseline.Value
            }
            : null;

        return new ContinuousComplianceAgent(
            driftService,
            NullLogger<ContinuousComplianceAgent>.Instance,
            _bundleRoot,
            checkInterval: TimeSpan.FromHours(24), // won't re-trigger during test
            autoRemediate: false,
            auditTrail: withAudit ? _mockAudit.Object : null,
            config: config);
    }

    private void SetupAuditCapture(TaskCompletionSource<string> tcs)
    {
        _mockAudit
            .Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEntry, CancellationToken>((e, _) => tcs.TrySetResult(e.Detail))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ComplianceCheck_ExcludeBaselineEvents_True_FiltersBaselineFromRemediableCount()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupAuditCapture(tcs);

        var agent = CreateAgent(excludeBaseline: true);
        await agent.StartAsync(CancellationToken.None);
        var detail = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await agent.StopAsync(CancellationToken.None);

        // BaselineEstablished excluded → only StateChanged is remediable (1 of 2)
        detail.Should().Contain("1 remediable");
        detail.Should().Contain("total: 2");
    }

    [Fact]
    public async Task ComplianceCheck_ExcludeBaselineEvents_False_IncludesBaselineInRemediableCount()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupAuditCapture(tcs);

        var agent = CreateAgent(excludeBaseline: false);
        await agent.StartAsync(CancellationToken.None);
        var detail = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await agent.StopAsync(CancellationToken.None);

        // Both events are remediable (2 of 2)
        detail.Should().Contain("2 remediable");
        detail.Should().Contain("total: 2");
    }

    [Fact]
    public async Task ComplianceCheck_ConfigNull_IncludesAllEvents()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupAuditCapture(tcs);

        // config=null → no filtering at all
        var agent = CreateAgent(excludeBaseline: null);
        await agent.StartAsync(CancellationToken.None);
        var detail = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await agent.StopAsync(CancellationToken.None);

        detail.Should().Contain("2 remediable");
        detail.Should().Contain("total: 2");
    }

    [Fact]
    public async Task ComplianceCheck_AuditDetail_ReflectsRemediableCount()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupAuditCapture(tcs);

        var agent = CreateAgent(excludeBaseline: true);
        await agent.StartAsync(CancellationToken.None);
        var detail = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await agent.StopAsync(CancellationToken.None);

        // Verify exact audit detail format
        detail.Should().Be("Scheduled check: 1 remediable drift events detected (total: 2)");
    }
}
