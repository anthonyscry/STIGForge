using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class ManualAnswerServiceTests
{
    private readonly ManualAnswerService _sut = new();

    // ── NormalizeStatus ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Open")]
    [InlineData("", "Open")]
    [InlineData("   ", "Open")]
    [InlineData("pass", "Pass")]
    [InlineData("Pass", "Pass")]
    [InlineData("NotAFinding", "Pass")]
    [InlineData("compliant", "Pass")]
    [InlineData("closed", "Pass")]
    [InlineData("fail", "Fail")]
    [InlineData("Fail", "Fail")]
    [InlineData("noncompliant", "Fail")]
    [InlineData("NotApplicable", "NotApplicable")]
    [InlineData("notapplicable", "NotApplicable")]
    [InlineData("NA", "NotApplicable")]
    [InlineData("na", "NotApplicable")]
    [InlineData("open", "Open")]
    [InlineData("NotReviewed", "Open")]
    [InlineData("notchecked", "Open")]
    [InlineData("unknown", "Open")]
    [InlineData("informational", "Open")]
    [InlineData("error", "Open")]
    [InlineData("randomstatus", "Open")]
    public void NormalizeStatus_VariousInputs_ReturnsExpected(string? input, string expected)
    {
        _sut.NormalizeStatus(input).Should().Be(expected);
    }

    // ── RequiresReason ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Fail", true)]
    [InlineData("Open", false)]
    [InlineData("Pass", false)]
    [InlineData("NotApplicable", true)]
    [InlineData(null, false)]
    public void RequiresReason_ReturnsExpected(string? status, bool expected)
    {
        _sut.RequiresReason(status).Should().Be(expected);
    }

    // ── IsMeaningfulReason ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("ab", false)]             // too short
    [InlineData("na", false)]             // placeholder
    [InlineData("n/a", false)]            // placeholder
    [InlineData("none", false)]           // placeholder
    [InlineData("unknown", false)]        // placeholder
    [InlineData("test", false)]           // placeholder
    [InlineData("tbd", false)]            // placeholder
    [InlineData("Valid reason text", true)]
    [InlineData("abc", true)]             // exactly minimum length 3
    public void IsMeaningfulReason_VariousInputs_ReturnsExpected(string? reason, bool expected)
    {
        _sut.IsMeaningfulReason(reason).Should().Be(expected);
    }

    // ── ValidateReasonRequirement ──────────────────────────────────────────────

    [Fact]
    public void ValidateReasonRequirement_PassStatus_DoesNotThrow()
    {
        var act = () => _sut.ValidateReasonRequirement("Pass", null);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateReasonRequirement_FailWithNoReason_Throws()
    {
        var act = () => _sut.ValidateReasonRequirement("Fail", null);
        act.Should().Throw<ArgumentException>().WithMessage("*Reason is required*");
    }

    [Fact]
    public void ValidateReasonRequirement_FailWithPlaceholderReason_Throws()
    {
        var act = () => _sut.ValidateReasonRequirement("Fail", "na");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateReasonRequirement_FailWithMeaningfulReason_DoesNotThrow()
    {
        var act = () => _sut.ValidateReasonRequirement("Fail", "System is air-gapped");
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateReasonRequirement_NotApplicableWithNoReason_Throws()
    {
        var act = () => _sut.ValidateReasonRequirement("NotApplicable", null);
        act.Should().Throw<ArgumentException>();
    }

    // ── ValidateBreakGlassReason ───────────────────────────────────────────────

    [Fact]
    public void ValidateBreakGlassReason_ShortReason_Throws()
    {
        var act = () => _sut.ValidateBreakGlassReason("short");
        act.Should().Throw<ArgumentException>().WithMessage("*Break-glass*");
    }

    [Fact]
    public void ValidateBreakGlassReason_SufficientReason_DoesNotThrow()
    {
        var act = () => _sut.ValidateBreakGlassReason("Emergency access required for patching");
        act.Should().NotThrow();
    }

    // ── LoadAnswerFile / SaveAnswerFile ────────────────────────────────────────

    [Fact]
    public void LoadAnswerFile_NoFile_ReturnsEmptyAnswerFile()
    {
        using var tmp = new TempDirectory();

        var file = _sut.LoadAnswerFile(tmp.Path);

        file.Should().NotBeNull();
        file.Answers.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_Answers()
    {
        using var tmp = new TempDirectory();
        var answerFile = new AnswerFile
        {
            ProfileId = "profile1",
            PackId = "pack1",
            Answers = new List<ManualAnswer>
            {
                new() { RuleId = "SV-001", Status = "Pass" }
            }
        };

        _sut.SaveAnswerFile(tmp.Path, answerFile);
        var loaded = _sut.LoadAnswerFile(tmp.Path);

        loaded.ProfileId.Should().Be("profile1");
        loaded.PackId.Should().Be("pack1");
        loaded.Answers.Should().ContainSingle(a => a.RuleId == "SV-001");
    }

    [Fact]
    public void LoadAnswerFile_CorruptJson_ReturnsEmptyAnswerFile()
    {
        using var tmp = new TempDirectory();
        var dir = Path.Combine(tmp.Path, "Manual");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "answers.json"), "{ not valid json !!!");

        var file = _sut.LoadAnswerFile(tmp.Path);

        file.Should().NotBeNull();
        file.Answers.Should().BeEmpty();
    }

    [Fact]
    public void LoadAnswerFile_WithManifest_ReadsProfileAndPackId()
    {
        using var tmp = new TempDirectory();
        var manifestDir = Path.Combine(tmp.Path, "Manifest");
        Directory.CreateDirectory(manifestDir);
        File.WriteAllText(Path.Combine(manifestDir, "manifest.json"),
            """{"run":{"profileName":"TestProfile","packName":"TestPack"}}""");

        var file = _sut.LoadAnswerFile(tmp.Path);

        file.ProfileId.Should().Be("TestProfile");
        file.PackId.Should().Be("TestPack");
    }

    // ── SaveAnswer ─────────────────────────────────────────────────────────────

    [Fact]
    public void SaveAnswer_NullAnswer_Throws()
    {
        using var tmp = new TempDirectory();
        var act = () => _sut.SaveAnswer(tmp.Path, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveAnswer_NoRuleIdOrVulnId_Throws()
    {
        using var tmp = new TempDirectory();
        var act = () => _sut.SaveAnswer(tmp.Path, new ManualAnswer { Status = "Pass" });
        act.Should().Throw<ArgumentException>().WithMessage("*RuleId or VulnId*");
    }

    [Fact]
    public void SaveAnswer_NewAnswer_PersistsToFile()
    {
        using var tmp = new TempDirectory();

        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });

        var file = _sut.LoadAnswerFile(tmp.Path);
        file.Answers.Should().ContainSingle(a => a.RuleId == "SV-001" && a.Status == "Pass");
    }

    [Fact]
    public void SaveAnswer_UpdateExistingByRuleId_OverwritesStatus()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Fail", Reason = "Not configured" });

        var file = _sut.LoadAnswerFile(tmp.Path);
        file.Answers.Should().ContainSingle();
        file.Answers[0].Status.Should().Be("Fail");
        file.Answers[0].Reason.Should().Be("Not configured");
    }

    [Fact]
    public void SaveAnswer_UpdateExistingByVulnId_OverwritesStatus()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { VulnId = "V-100", Status = "Pass" });
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { VulnId = "V-100", Status = "Fail", Reason = "Missing control" });

        var file = _sut.LoadAnswerFile(tmp.Path);
        file.Answers.Should().ContainSingle();
        file.Answers[0].Status.Should().Be("Fail");
    }

    [Fact]
    public void SaveAnswer_WithRequireReason_FailAndNoReason_Throws()
    {
        using var tmp = new TempDirectory();
        var act = () => _sut.SaveAnswer(tmp.Path,
            new ManualAnswer { RuleId = "SV-001", Status = "Fail" },
            requireReasonForDecision: true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SaveAnswer_SetsProfileAndPackId()
    {
        using var tmp = new TempDirectory();

        _sut.SaveAnswer(tmp.Path,
            new ManualAnswer { RuleId = "SV-001", Status = "Pass" },
            profileId: "P1",
            packId: "Pack1");

        var file = _sut.LoadAnswerFile(tmp.Path);
        file.ProfileId.Should().Be("P1");
        file.PackId.Should().Be("Pack1");
    }

    [Fact]
    public void SaveAnswer_WithAuditService_DoesNotThrowEvenIfAuditFails()
    {
        var mockAudit = new Mock<IAuditTrailService>();
        mockAudit.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Audit failed"));

        var sut = new ManualAnswerService(mockAudit.Object);
        using var tmp = new TempDirectory();

        var act = () => sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        act.Should().NotThrow();
    }

    // ── GetAnswer ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetAnswer_MatchByRuleId_ReturnsAnswer()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });

        var control = MakeControl(ruleId: "SV-001");
        var answer = _sut.GetAnswer(tmp.Path, control);

        answer.Should().NotBeNull();
        answer!.Status.Should().Be("Pass");
    }

    [Fact]
    public void GetAnswer_MatchByVulnId_ReturnsAnswer()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { VulnId = "V-001", Status = "Fail", Reason = "Found open" });

        var control = MakeControl(vulnId: "V-001");
        var answer = _sut.GetAnswer(tmp.Path, control);

        answer.Should().NotBeNull();
        answer!.Status.Should().Be("Fail");
    }

    [Fact]
    public void GetAnswer_NoMatch_ReturnsNull()
    {
        using var tmp = new TempDirectory();

        var answer = _sut.GetAnswer(tmp.Path, MakeControl(ruleId: "SV-999"));

        answer.Should().BeNull();
    }

    // ── GetUnansweredControls ──────────────────────────────────────────────────

    [Fact]
    public void GetUnansweredControls_EmptyAnswerFile_ReturnsAllControls()
    {
        using var tmp = new TempDirectory();
        var controls = new List<ControlRecord>
        {
            MakeControl(ruleId: "SV-001"),
            MakeControl(ruleId: "SV-002")
        };

        var result = _sut.GetUnansweredControls(tmp.Path, controls);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetUnansweredControls_AnsweredControlsExcluded()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        var controls = new List<ControlRecord>
        {
            MakeControl(ruleId: "SV-001"),
            MakeControl(ruleId: "SV-002")
        };

        var result = _sut.GetUnansweredControls(tmp.Path, controls);

        result.Should().ContainSingle(c => c.ExternalIds.RuleId == "SV-002");
    }

    [Fact]
    public void GetUnansweredControls_OpenStatusNotCountedAsAnswered()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Open" });
        var controls = new List<ControlRecord> { MakeControl(ruleId: "SV-001") };

        var result = _sut.GetUnansweredControls(tmp.Path, controls);

        result.Should().ContainSingle();
    }

    // ── GetProgressStats ───────────────────────────────────────────────────────

    [Fact]
    public void GetProgressStats_EmptyControls_ZeroStats()
    {
        using var tmp = new TempDirectory();

        var stats = _sut.GetProgressStats(tmp.Path, Array.Empty<ControlRecord>());

        stats.TotalControls.Should().Be(0);
        stats.PercentComplete.Should().Be(0);
    }

    [Fact]
    public void GetProgressStats_MixedAnswers_ComputesCorrectly()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-002", Status = "Fail", Reason = "Open finding" });
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-003", Status = "NotApplicable", Reason = "Not in scope" });

        var controls = new List<ControlRecord>
        {
            MakeControl(ruleId: "SV-001"),
            MakeControl(ruleId: "SV-002"),
            MakeControl(ruleId: "SV-003"),
            MakeControl(ruleId: "SV-004") // unanswered
        };

        var stats = _sut.GetProgressStats(tmp.Path, controls);

        stats.TotalControls.Should().Be(4);
        stats.PassCount.Should().Be(1);
        stats.FailCount.Should().Be(1);
        stats.NotApplicableCount.Should().Be(1);
        stats.UnansweredControls.Should().Be(1);
        stats.AnsweredControls.Should().Be(3);
        stats.PercentComplete.Should().Be(75);
    }

    // ── ExportAnswers / WriteExportFile / ReadExportFile ───────────────────────

    [Fact]
    public void ExportAnswers_ReturnsExportWithAnswers()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });

        var export = _sut.ExportAnswers(tmp.Path, stigId: "STIG-123");

        export.StigId.Should().Be("STIG-123");
        export.Answers.Answers.Should().ContainSingle(a => a.RuleId == "SV-001");
        export.ExportedBy.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WriteAndReadExportFile_RoundTrips()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        var export = _sut.ExportAnswers(tmp.Path, "STIG-X");
        var outputPath = tmp.File("export.json");

        _sut.WriteExportFile(outputPath, export);
        var loaded = _sut.ReadExportFile(outputPath);

        loaded.StigId.Should().Be("STIG-X");
        loaded.Answers.Answers.Should().ContainSingle(a => a.RuleId == "SV-001");
    }

    // ── ImportAnswers ──────────────────────────────────────────────────────────

    [Fact]
    public void ImportAnswers_NewAnswers_Imported()
    {
        using var tmp = new TempDirectory();
        var import = new AnswerFileExport
        {
            Answers = new AnswerFile
            {
                Answers = new List<ManualAnswer>
                {
                    new() { RuleId = "SV-001", Status = "Pass" },
                    new() { RuleId = "SV-002", Status = "Fail", Reason = "Open finding" }
                }
            }
        };

        var result = _sut.ImportAnswers(tmp.Path, import);

        result.Imported.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Total.Should().Be(2);
    }

    [Fact]
    public void ImportAnswers_ExistingOpenAnswer_Overwritten()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Open" });
        var import = new AnswerFileExport
        {
            Answers = new AnswerFile
            {
                Answers = new List<ManualAnswer>
                {
                    new() { RuleId = "SV-001", Status = "Pass" }
                }
            }
        };

        var result = _sut.ImportAnswers(tmp.Path, import);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(0);
    }

    [Fact]
    public void ImportAnswers_ExistingResolvedAnswer_Skipped()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { RuleId = "SV-001", Status = "Pass" });
        var import = new AnswerFileExport
        {
            Answers = new AnswerFile
            {
                Answers = new List<ManualAnswer>
                {
                    new() { RuleId = "SV-001", Status = "Fail", Reason = "Override attempt" }
                }
            }
        };

        var result = _sut.ImportAnswers(tmp.Path, import);

        result.Skipped.Should().Be(1);
        result.Imported.Should().Be(0);
        result.SkippedControls.Should().Contain("SV-001");
    }

    [Fact]
    public void ImportAnswers_EmptyImport_ZeroResults()
    {
        using var tmp = new TempDirectory();
        var import = new AnswerFileExport { Answers = new AnswerFile() };

        var result = _sut.ImportAnswers(tmp.Path, import);

        result.Total.Should().Be(0);
        result.Imported.Should().Be(0);
    }

    [Fact]
    public void ImportAnswers_MatchByVulnId_Skipped()
    {
        using var tmp = new TempDirectory();
        _sut.SaveAnswer(tmp.Path, new ManualAnswer { VulnId = "V-100", Status = "Fail", Reason = "Resolved" });
        var import = new AnswerFileExport
        {
            Answers = new AnswerFile
            {
                Answers = new List<ManualAnswer>
                {
                    new() { VulnId = "V-100", Status = "Pass" }
                }
            }
        };

        var result = _sut.ImportAnswers(tmp.Path, import);

        result.Skipped.Should().Be(1);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ControlRecord MakeControl(string? ruleId = null, string? vulnId = null) =>
        new()
        {
            ControlId = ruleId ?? vulnId ?? Guid.NewGuid().ToString("N"),
            IsManual = true,
            ExternalIds = new ExternalIds { RuleId = ruleId, VulnId = vulnId }
        };
}
