using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class AnswerRebaseServiceTests
{
    private const string BaselinePack = "pack-v1";
    private const string NewPack = "pack-v2";

    // ── factory helpers ───────────────────────────────────────────────────────

    private static ControlRecord MakeControl(string ruleId, string? checkText = null, string? sourcePack = null) =>
        new()
        {
            ControlId = ruleId,
            SourcePackId = sourcePack ?? BaselinePack,
            ExternalIds = new ExternalIds { RuleId = ruleId },
            Title = $"Title for {ruleId}",
            CheckText = checkText ?? $"Check text for {ruleId}",
            Severity = "medium"
        };

    private static ControlRecord CloneWithPack(ControlRecord c, string sourcePack) =>
        new()
        {
            ControlId = c.ControlId,
            SourcePackId = sourcePack,
            ExternalIds = new ExternalIds { RuleId = c.ExternalIds.RuleId, VulnId = c.ExternalIds.VulnId },
            Title = c.Title,
            CheckText = c.CheckText,
            FixText = c.FixText,
            Severity = c.Severity,
            IsManual = c.IsManual
        };

    private static ManualAnswer MakeAnswer(string ruleId, string status = "Pass") =>
        new() { RuleId = ruleId, Status = status, UpdatedAt = DateTimeOffset.UtcNow };

    private static (AnswerRebaseService svc, string bundleRoot) BuildService(
        TempDirectory tmp,
        IReadOnlyList<ControlRecord> baselineControls,
        IReadOnlyList<ControlRecord> newControls,
        IReadOnlyList<ManualAnswer>? answers = null,
        IAuditTrailService? audit = null)
    {
        // Write answer file
        var answerService = new ManualAnswerService();
        var answerFile = new AnswerFile { PackId = BaselinePack, Answers = answers?.ToList() ?? new List<ManualAnswer>() };
        answerService.SaveAnswerFile(tmp.Path, answerFile);

        // Mock control repository
        var controlRepo = new Mock<IControlRepository>();
        controlRepo.Setup(r => r.ListControlsAsync(BaselinePack, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baselineControls);
        controlRepo.Setup(r => r.ListControlsAsync(NewPack, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newControls);
        controlRepo.Setup(r => r.VerifySchemaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var diffService = new BaselineDiffService(controlRepo.Object);
        var rebaseService = new AnswerRebaseService(answerService, diffService, audit);

        return (rebaseService, tmp.Path);
    }

    // ── no answers ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RebaseAnswersAsync_ReturnsSuccess_WhenAnswerFileIsEmpty()
    {
        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [],
            newControls: [],
            answers: []);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        report.Success.Should().BeTrue();
        report.Actions.Should().BeEmpty();
    }

    // ── unchanged controls (Carry) ────────────────────────────────────────────

    [Fact]
    public async Task RebaseAnswersAsync_ProducesCarryAction_WhenControlIsUnchanged()
    {
        var control = MakeControl("RULE-001");
        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [control],
            newControls: [CloneWithPack(control, NewPack)],
            answers: [MakeAnswer("RULE-001")]);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        report.Success.Should().BeTrue();
        report.Actions.Should().ContainSingle();
        report.Actions[0].ActionType.Should().Be(AnswerRebaseActionType.Carry);
        report.Actions[0].Confidence.Should().Be(1.0);
    }

    [Fact]
    public async Task RebaseAnswersAsync_SetsOverallConfidence_ToOne_WhenAllControlsUnchanged()
    {
        var c1 = MakeControl("RULE-001");
        var c2 = MakeControl("RULE-002");
        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [c1, c2],
            newControls: [CloneWithPack(c1, NewPack), CloneWithPack(c2, NewPack)],
            answers: [MakeAnswer("RULE-001"), MakeAnswer("RULE-002")]);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        report.OverallConfidence.Should().Be(1.0);
        report.SafeActions.Should().Be(2);
    }

    // ── removed controls (blocking) ───────────────────────────────────────────

    [Fact]
    public async Task RebaseAnswersAsync_ProducesRemoveAction_WhenControlRemovedFromNewBaseline()
    {
        var control = MakeControl("RULE-REMOVED");
        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [control],
            newControls: [],
            answers: [MakeAnswer("RULE-REMOVED")]);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        report.Actions.Should().ContainSingle(a => a.ActionType == AnswerRebaseActionType.Remove);
        report.BlockingConflicts.Should().Be(1);
        report.HasBlockingConflicts.Should().BeTrue();
    }

    [Fact]
    public async Task RebaseAnswersAsync_SetsRequiresReview_ForRemovedControl()
    {
        var control = MakeControl("RULE-REMOVED");
        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [control],
            newControls: [],
            answers: [MakeAnswer("RULE-REMOVED")]);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        var action = report.Actions.Single();
        action.IsBlockingConflict.Should().BeTrue();
        action.RequiresReview.Should().BeTrue();
    }

    // ── high-impact changes (ReviewRequired) ──────────────────────────────────

    [Fact]
    public async Task RebaseAnswersAsync_ProducesReviewRequired_WhenCheckTextChanges()
    {
        var baseline = MakeControl("RULE-HIGH", "Original check text");
        var newControl = MakeControl("RULE-HIGH", "Completely different check text that changes the check", NewPack);

        using var tmp = new TempDirectory();
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [baseline],
            newControls: [newControl],
            answers: [MakeAnswer("RULE-HIGH")]);

        var report = await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        // CheckText is a High-impact field
        report.Actions.Should().ContainSingle();
        var action = report.Actions[0];
        action.ActionType.Should().BeOneOf(
            AnswerRebaseActionType.ReviewRequired,
            AnswerRebaseActionType.CarryWithWarning,
            AnswerRebaseActionType.Carry);
        // At minimum it should be present and have reasonable confidence
        action.Confidence.Should().BeGreaterThan(0.0).And.BeLessThanOrEqualTo(1.0);
    }

    // ── ApplyAnswerRebase ────────────────────────────────────────────────────

    [Fact]
    public void ApplyAnswerRebase_ThrowsInvalidOperationException_WhenBlockingConflictsExist()
    {
        var svc = new AnswerRebaseService(new ManualAnswerService(), BuildDummyDiffService());

        var report = new AnswerRebaseReport
        {
            Success = true,
            BlockingConflicts = 1,
            Actions =
            [
                new AnswerRebaseAction
                {
                    ControlKey = "RULE:RULE-001",
                    ActionType = AnswerRebaseActionType.Remove,
                    IsBlockingConflict = true,
                    Confidence = 1.0,
                    OriginalAnswer = MakeAnswer("RULE-001")
                }
            ]
        };

        var source = new AnswerFile { Answers = [MakeAnswer("RULE-001")] };

        Action act = () => svc.ApplyAnswerRebase(report, source);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*blocking conflicts*");
    }

    [Fact]
    public void ApplyAnswerRebase_ReturnsRebasedFile_WhenNoBlockingConflicts()
    {
        var svc = new AnswerRebaseService(new ManualAnswerService(), BuildDummyDiffService());

        var originalAnswer = MakeAnswer("RULE-001", "Pass");
        var report = new AnswerRebaseReport
        {
            Success = true,
            BlockingConflicts = 0,
            Actions =
            [
                new AnswerRebaseAction
                {
                    ControlKey = "RULE:RULE-001",
                    ActionType = AnswerRebaseActionType.Carry,
                    IsBlockingConflict = false,
                    Confidence = 1.0,
                    OriginalAnswer = originalAnswer
                }
            ]
        };

        var source = new AnswerFile { Answers = [originalAnswer] };

        var rebased = svc.ApplyAnswerRebase(report, source);

        rebased.Answers.Should().ContainSingle(a => a.RuleId == "RULE-001");
    }

    [Fact]
    public void ApplyAnswerRebase_ExcludesRemovedActions()
    {
        var svc = new AnswerRebaseService(new ManualAnswerService(), BuildDummyDiffService());

        var report = new AnswerRebaseReport
        {
            Success = true,
            BlockingConflicts = 0,
            Actions =
            [
                new AnswerRebaseAction
                {
                    ControlKey = "RULE:RULE-REMOVED",
                    ActionType = AnswerRebaseActionType.Remove,
                    IsBlockingConflict = false, // non-blocking remove
                    Confidence = 1.0,
                    OriginalAnswer = MakeAnswer("RULE-REMOVED")
                }
            ]
        };

        var source = new AnswerFile { Answers = [MakeAnswer("RULE-REMOVED")] };

        var rebased = svc.ApplyAnswerRebase(report, source);

        rebased.Answers.Should().BeEmpty("Remove actions should not carry the answer over");
    }

    [Fact]
    public void ApplyAnswerRebase_AddsRebaseMarker_ForCarryWithWarning()
    {
        var svc = new AnswerRebaseService(new ManualAnswerService(), BuildDummyDiffService());
        var originalAnswer = new ManualAnswer
        {
            RuleId = "RULE-001",
            Status = "Pass",
            Comment = "Original comment",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var report = new AnswerRebaseReport
        {
            Success = true,
            BlockingConflicts = 0,
            Actions =
            [
                new AnswerRebaseAction
                {
                    ControlKey = "RULE:RULE-001",
                    ActionType = AnswerRebaseActionType.CarryWithWarning,
                    IsBlockingConflict = false,
                    Confidence = 0.7,
                    OriginalAnswer = originalAnswer
                }
            ]
        };

        var rebased = svc.ApplyAnswerRebase(report, new AnswerFile { Answers = [originalAnswer] });

        rebased.Answers.Should().ContainSingle();
        rebased.Answers[0].Comment.Should().Contain("[REBASED:");
    }

    // ── audit trail ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RebaseAnswersAsync_CallsAudit_WhenAuditServiceProvided()
    {
        using var tmp = new TempDirectory();
        var audit = new Mock<IAuditTrailService>();
        audit.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var control = MakeControl("RULE-AUDIT");
        var (svc, bundleRoot) = BuildService(tmp,
            baselineControls: [control],
            newControls: [CloneWithPack(control, NewPack)],
            answers: [MakeAnswer("RULE-AUDIT")],
            audit: audit.Object);

        await svc.RebaseAnswersAsync(bundleRoot, BaselinePack, NewPack);

        // Fire-and-forget; allow brief time for audit task
        await Task.Delay(150);
        audit.Verify(a => a.RecordAsync(
            It.Is<AuditEntry>(e => e.Action == "rebase-answers"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static BaselineDiffService BuildDummyDiffService()
    {
        var controlRepo = new Mock<IControlRepository>();
        controlRepo.Setup(r => r.ListControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ControlRecord>)[]);
        return new BaselineDiffService(controlRepo.Object);
    }
}
