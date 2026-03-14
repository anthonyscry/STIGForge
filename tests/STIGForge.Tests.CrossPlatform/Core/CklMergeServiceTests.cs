using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class CklMergeServiceTests
{
    private static readonly DateTimeOffset _importedAt = new(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CklMergeService _sut = new();

    // ── Null guards ───────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_NullChecklist_ThrowsArgumentNullException()
    {
        var act = () => _sut.MergeAsync(null!, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task MergeAsync_NullExistingResults_ThrowsArgumentNullException()
    {
        var checklist = MakeChecklist();
        var act = () => _sut.MergeAsync(checklist, null!, CklConflictResolutionStrategy.CklWins, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task MergeAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var checklist = MakeChecklist();
        var act = () => _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Empty inputs ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_EmptyChecklistAndNoExisting_ReturnEmptyFindings()
    {
        var checklist = MakeChecklist();
        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
    }

    // ── No matching existing results ──────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_NoMatchingExisting_FindingIsClonedAsIs()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings.Should().ContainSingle();
        result.MergedFindings[0].VulnId.Should().Be("V-001");
        result.MergedFindings[0].Status.Should().Be("NotAFinding");
    }

    [Fact]
    public async Task MergeAsync_NoMatchingExisting_NoConflictsGenerated()
    {
        var checklist = MakeChecklist(MakeFinding("V-001", "SV-001", "NotAFinding"));

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.Conflicts.Should().BeEmpty();
    }

    // ── Status normalization ──────────────────────────────────────────────────

    [Theory]
    [InlineData("pass", "NotAFinding")]
    [InlineData("notafinding", "NotAFinding")]
    [InlineData("open", "Open")]
    [InlineData("fail", "Open")]
    [InlineData("notapplicable", "Not_Applicable")]
    [InlineData("na", "Not_Applicable")]
    [InlineData("notreviewed", "Not_Reviewed")]
    [InlineData("unknown_value", "Not_Reviewed")]
    public async Task MergeAsync_CklStatusNormalization_NormalizesCorrectly(string rawStatus, string expectedNormalized)
    {
        var finding = MakeFinding("V-001", "SV-001", rawStatus);
        var checklist = MakeChecklist(finding);

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be(expectedNormalized);
    }

    // ── No conflict (matching statuses) ──────────────────────────────────────

    [Fact]
    public async Task MergeAsync_BothPassStatuses_NoConflictGenerated()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "pass");

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.Conflicts.Should().BeEmpty();
        result.MergedFindings[0].Status.Should().Be("NotAFinding");
    }

    // ── Conflict resolution: CklWins ─────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_Conflict_CklWins_UsesNormalizedCklStatus()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "fail"); // different

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be("NotAFinding");
        result.Conflicts.Should().ContainSingle();
        result.Conflicts[0].ResolvedStatus.Should().Be("NotAFinding");
        result.Conflicts[0].StrategyApplied.Should().Be(CklConflictResolutionStrategy.CklWins);
        result.Conflicts[0].RequiresManualResolution.Should().BeFalse();
    }

    // ── Conflict resolution: StigForgeWins ───────────────────────────────────

    [Fact]
    public async Task MergeAsync_Conflict_StigForgeWins_UsesStigForgeStatus()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "fail");
        existing.Comments = "stigforge comment";

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.StigForgeWins, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be("Open"); // fail → Open
        result.Conflicts[0].StrategyApplied.Should().Be(CklConflictResolutionStrategy.StigForgeWins);
        // StigForgeWins also copies STIGForge comments
        result.MergedFindings[0].Comments.Should().Be("stigforge comment");
    }

    // ── Conflict resolution: Manual ───────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_Conflict_Manual_FlagsRequiresManualResolution()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "fail");

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.Manual, CancellationToken.None);

        result.Conflicts.Should().ContainSingle().Which.RequiresManualResolution.Should().BeTrue();
    }

    // ── Conflict resolution: MostRecent ──────────────────────────────────────

    [Fact]
    public async Task MergeAsync_Conflict_MostRecent_CklMoreRecent_UsesCklStatus()
    {
        // No source file means STIGForge has no timestamp → CKL wins
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "fail");
        existing.SourceFile = string.Empty; // no file = no timestamp

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be("NotAFinding");
    }

    [Fact]
    public async Task MergeAsync_Conflict_MostRecent_StigForgeMoreRecent_UsesStigForgeStatus()
    {
        using var tempDir = new TempDirectory();
        var sourceFile = tempDir.File("results.json");
        // Write the file with a future date to make StigForge the most recent
        File.WriteAllText(sourceFile, "{}");
        File.SetLastWriteTimeUtc(sourceFile, _importedAt.AddDays(10).UtcDateTime);

        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        var checklist = MakeChecklist(new[] { finding }, _importedAt);
        var existing = MakeControlResult("SV-001", "V-001", "fail");
        existing.SourceFile = sourceFile;

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be("Open"); // fail → Open (StigForge wins)
    }

    // ── Comments / FindingDetails merging ────────────────────────────────────

    [Fact]
    public async Task MergeAsync_FindingHasNoComments_FallsBackToExistingComments()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        finding.Comments = null;
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "pass");
        existing.Comments = "reviewer note";

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings[0].Comments.Should().Be("reviewer note");
    }

    [Fact]
    public async Task MergeAsync_FindingHasComments_KeepsFindingComments()
    {
        var finding = MakeFinding("V-001", "SV-001", "NotAFinding");
        finding.Comments = "ckl note";
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "pass");
        existing.Comments = "stigforge note";

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings[0].Comments.Should().Be("ckl note");
    }

    // ── Existing results not in checklist ─────────────────────────────────────

    [Fact]
    public async Task MergeAsync_ExistingControlNotInChecklist_AppendedToMergedFindings()
    {
        var checklist = MakeChecklist(MakeFinding("V-001", "SV-001", "NotAFinding"));
        var extraExisting = MakeControlResult("SV-999", "V-999", "fail");

        var result = await _sut.MergeAsync(checklist, new[] { extraExisting }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings.Should().HaveCount(2);
        result.MergedFindings.Should().Contain(f => f.VulnId == "V-999");
    }

    [Fact]
    public async Task MergeAsync_ExistingControlAppended_UsesCorrectMappedStatus()
    {
        var checklist = MakeChecklist();
        var existing = MakeControlResult("SV-999", "V-999", "pass");

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings[0].Status.Should().Be("NotAFinding");
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_Findings_OrderedByVulnId()
    {
        var checklist = MakeChecklist(
            MakeFinding("V-300", "SV-300", "Open"),
            MakeFinding("V-100", "SV-100", "NotAFinding"),
            MakeFinding("V-200", "SV-200", "Not_Applicable"));

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedFindings.Select(f => f.VulnId).Should().BeInAscendingOrder();
    }

    // ── MergedChecklist properties ────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_MergedChecklist_HasSameAssetInfoAsImported()
    {
        var checklist = MakeChecklist();
        checklist.AssetName = "myhost";
        checklist.HostName = "myhost.domain.com";
        checklist.StigTitle = "Windows 11 STIG";

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.MergedChecklist.AssetName.Should().Be("myhost");
        result.MergedChecklist.HostName.Should().Be("myhost.domain.com");
        result.MergedChecklist.StigTitle.Should().Be("Windows 11 STIG");
    }

    [Fact]
    public async Task MergeAsync_StrategyIsRecorded_InResult()
    {
        var checklist = MakeChecklist();

        var result = await _sut.MergeAsync(checklist, [], CklConflictResolutionStrategy.StigForgeWins, CancellationToken.None);

        result.Strategy.Should().Be(CklConflictResolutionStrategy.StigForgeWins);
    }

    // ── VulnId vs RuleId key building ────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_ExistingMatchedByRuleIdWhenNoVulnId_MergesCorrectly()
    {
        var finding = MakeFinding(string.Empty, "SV-001", "NotAFinding");
        var checklist = MakeChecklist(finding);
        var existing = new ControlResult { RuleId = "SV-001", VulnId = null, Status = "pass" };

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        result.Conflicts.Should().BeEmpty();
        result.MergedFindings.Should().ContainSingle();
    }

    [Fact]
    public async Task MergeAsync_DuplicateVulnIdInExisting_FirstOccurrenceWins()
    {
        var finding = MakeFinding("V-001", "SV-001", "Open");
        var checklist = MakeChecklist(finding);
        var existing1 = MakeControlResult("SV-001", "V-001", "pass");
        var existing2 = MakeControlResult("SV-001b", "V-001", "fail");

        var result = await _sut.MergeAsync(checklist, new[] { existing1, existing2 }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        // No crash; exactly one match should be used
        result.MergedFindings.Should().ContainSingle(f => f.VulnId == "V-001");
    }

    // ── Conflict metadata ─────────────────────────────────────────────────────

    [Fact]
    public async Task MergeAsync_Conflict_RecordsCklAndStigForgeStatuses()
    {
        var finding = MakeFinding("V-001", "SV-001", "open");
        var checklist = MakeChecklist(finding);
        var existing = MakeControlResult("SV-001", "V-001", "pass");

        var result = await _sut.MergeAsync(checklist, new[] { existing }, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

        var conflict = result.Conflicts.Should().ContainSingle().Subject;
        conflict.CklStatus.Should().Be("open");
        conflict.StigForgeStatus.Should().Be("NotAFinding"); // pass → NotAFinding
        conflict.VulnId.Should().Be("V-001");
        conflict.CklImportedAt.Should().Be(_importedAt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CklChecklist MakeChecklist(params CklFinding[] findings)
        => MakeChecklist(findings, _importedAt);

    private static CklChecklist MakeChecklist(IEnumerable<CklFinding>? findings, DateTimeOffset importedAt = default)
    {
        if (importedAt == default) importedAt = _importedAt;
        return new CklChecklist
        {
            FilePath = "test.ckl",
            ImportedAt = importedAt,
            AssetName = "test-asset",
            StigTitle = "Test STIG",
            Findings = findings?.ToList() ?? new List<CklFinding>()
        };
    }

    private static CklFinding MakeFinding(string vulnId, string ruleId, string status) =>
        new()
        {
            VulnId = vulnId,
            RuleId = ruleId,
            RuleTitle = $"Title for {vulnId}",
            Severity = "medium",
            Status = status
        };

    private static ControlResult MakeControlResult(string ruleId, string vulnId, string status) =>
        new()
        {
            RuleId = ruleId,
            VulnId = vulnId,
            Status = status
        };
}
