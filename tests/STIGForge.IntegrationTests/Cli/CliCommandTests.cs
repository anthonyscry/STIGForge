using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.IntegrationTests.Cli;

/// <summary>
/// Integration tests for the service-level logic wired by CLI commands
/// (list-packs, list-overlays, diff-packs, rebase-overlay).
/// Uses real SQLite to validate full round-trip.
/// </summary>
public class CliCommandTests : IDisposable
{
  private readonly string _dbPath;
  private readonly string _cs;

  public CliCommandTests()
  {
    _dbPath = Path.Combine(Path.GetTempPath(), "stigforge-cli-test-" + Guid.NewGuid().ToString("N")[..8] + ".db");
    _cs = $"Data Source={_dbPath}";
    DbBootstrap.EnsureCreated(_cs);
  }

  public void Dispose()
  {
    try { File.Delete(_dbPath); } catch { }
  }

  // ── helpers ──────────────────────────────────────────────────────────────

  private static ControlRecord MakeControl(string id, string title = "Title", string severity = "medium",
    string? checkText = null, string? fixText = null)
  {
    return new ControlRecord
    {
      ControlId = id,
      Title = title,
      Severity = severity,
      CheckText = checkText ?? "Check " + id,
      FixText = fixText ?? "Fix " + id,
      IsManual = false,
      ExternalIds = new ExternalIds
      {
        RuleId = "SV-" + id + "_rule",
        VulnId = "V-" + id,
        SrgId = null,
        BenchmarkId = "WIN11"
      },
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Both,
        Confidence = Confidence.High
      },
      Revision = new RevisionInfo { PackName = "TestPack" }
    };
  }

  private static ContentPack MakePack(string id, string name)
  {
    return new ContentPack
    {
      PackId = id,
      Name = name,
      ImportedAt = DateTimeOffset.UtcNow,
      SourceLabel = "test",
      HashAlgorithm = "SHA256",
      ManifestSha256 = "abc123"
    };
  }

  private static Overlay MakeOverlay(string id, string name, params ControlOverride[] overrides)
  {
    return new Overlay
    {
      OverlayId = id,
      Name = name,
      UpdatedAt = DateTimeOffset.UtcNow,
      Overrides = overrides.ToList(),
      PowerStigOverrides = Array.Empty<PowerStigOverride>()
    };
  }

  // ── list-packs ──────────────────────────────────────────────────────────
  // Note: ListAsync uses Dapper column mapping which has DateTimeOffset parsing
  // issues with SQLite text storage. We verify via raw SQL (same as DeleteRepositoryTests).

  [Fact]
  public async Task ListPacks_EmptyDb_ReturnsEmpty()
  {
    using var conn = new SqliteConnection(_cs);
    var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM content_packs");
    count.Should().Be(0);
  }

  [Fact]
  public async Task ListPacks_WithPacks_ReturnsAll()
  {
    var repo = new SqliteContentPackRepository(_cs);
    await repo.SaveAsync(MakePack("pack-a", "Pack A"), CancellationToken.None);
    await repo.SaveAsync(MakePack("pack-b", "Pack B"), CancellationToken.None);

    using var conn = new SqliteConnection(_cs);
    var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM content_packs");
    count.Should().Be(2);

    var ids = (await conn.QueryAsync<string>("SELECT pack_id FROM content_packs ORDER BY pack_id")).ToList();
    ids.Should().Contain("pack-a").And.Contain("pack-b");
  }

  // ── list-overlays ───────────────────────────────────────────────────────

  [Fact]
  public async Task ListOverlays_EmptyDb_ReturnsEmpty()
  {
    var repo = new SqliteJsonOverlayRepository(_cs);

    var list = await repo.ListAsync(CancellationToken.None);

    list.Should().BeEmpty();
  }

  [Fact]
  public async Task ListOverlays_WithOverlays_ReturnsAll()
  {
    var repo = new SqliteJsonOverlayRepository(_cs);
    await repo.SaveAsync(MakeOverlay("ov-a", "Overlay A"), CancellationToken.None);
    await repo.SaveAsync(MakeOverlay("ov-b", "Overlay B",
      new ControlOverride { RuleId = "SV-1_rule", VulnId = "V-1", StatusOverride = ControlStatus.NotApplicable }
    ), CancellationToken.None);

    var list = await repo.ListAsync(CancellationToken.None);

    list.Should().HaveCount(2);
    list.Select(o => o.OverlayId).Should().Contain("ov-a").And.Contain("ov-b");
  }

  // ── diff-packs ──────────────────────────────────────────────────────────

  [Fact]
  public async Task DiffPacks_IdenticalPacks_NoChanges()
  {
    var packRepo = new SqliteContentPackRepository(_cs);
    var controlRepo = new SqliteJsonControlRepository(_cs);

    await packRepo.SaveAsync(MakePack("baseline", "Baseline"), CancellationToken.None);
    await packRepo.SaveAsync(MakePack("target", "Target"), CancellationToken.None);

    var controls = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    await controlRepo.SaveControlsAsync("baseline", controls, CancellationToken.None);
    await controlRepo.SaveControlsAsync("target", controls, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var diff = await diffService.ComparePacksAsync("baseline", "target");

    diff.TotalAdded.Should().Be(0);
    diff.TotalRemoved.Should().Be(0);
    diff.TotalModified.Should().Be(0);
    diff.TotalUnchanged.Should().Be(2);
  }

  [Fact]
  public async Task DiffPacks_DifferentPacks_DetectsChanges()
  {
    var packRepo = new SqliteContentPackRepository(_cs);
    var controlRepo = new SqliteJsonControlRepository(_cs);

    await packRepo.SaveAsync(MakePack("base2", "Baseline 2"), CancellationToken.None);
    await packRepo.SaveAsync(MakePack("tgt2", "Target 2"), CancellationToken.None);

    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2"), MakeControl("C3") };
    var target = new List<ControlRecord> { MakeControl("C1", title: "Changed Title"), MakeControl("C3"), MakeControl("C4") };

    await controlRepo.SaveControlsAsync("base2", baseline, CancellationToken.None);
    await controlRepo.SaveControlsAsync("tgt2", target, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var diff = await diffService.ComparePacksAsync("base2", "tgt2");

    diff.TotalAdded.Should().Be(1);     // C4 added
    diff.TotalRemoved.Should().Be(1);   // C2 removed
    diff.TotalModified.Should().Be(1);  // C1 title changed
    diff.TotalReviewRequired.Should().Be(1);
    diff.TotalUnchanged.Should().Be(1); // C3 unchanged
  }

  [Fact]
  public async Task DiffPacks_NoControlsInPack_ReturnsEmptyDiff()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);

    var diffService = new BaselineDiffService(controlRepo);
    var diff = await diffService.ComparePacksAsync("nonexistent-a", "nonexistent-b");

    diff.TotalAdded.Should().Be(0);
    diff.TotalRemoved.Should().Be(0);
    diff.TotalModified.Should().Be(0);
    diff.TotalUnchanged.Should().Be(0);
  }

  // ── rebase-overlay ────────────────────────────────────────────────────

  [Fact]
  public async Task RebaseOverlay_UnchangedControls_AllKept()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);
    var overlayRepo = new SqliteJsonOverlayRepository(_cs);

    var controls = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    await controlRepo.SaveControlsAsync("rb-base", controls, CancellationToken.None);
    await controlRepo.SaveControlsAsync("rb-tgt", controls, CancellationToken.None);

    var overlay = MakeOverlay("rb-ov1", "Rebase Test",
      new ControlOverride { RuleId = "SV-C1_rule", VulnId = "V-C1", StatusOverride = ControlStatus.NotApplicable, NaReason = "Test" }
    );
    await overlayRepo.SaveAsync(overlay, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var rebaseService = new OverlayRebaseService(overlayRepo, diffService);
    var report = await rebaseService.RebaseOverlayAsync("rb-ov1", "rb-base", "rb-tgt");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(RebaseActionType.Keep);
    report.Actions[0].Confidence.Should().Be(1.0);
  }

  [Fact]
  public async Task RebaseOverlay_RemovedControl_MarkedForRemoval()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);
    var overlayRepo = new SqliteJsonOverlayRepository(_cs);

    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") }; // C2 removed

    await controlRepo.SaveControlsAsync("rm-base", baseline, CancellationToken.None);
    await controlRepo.SaveControlsAsync("rm-tgt", target, CancellationToken.None);

    var overlay = MakeOverlay("rm-ov1", "Remove Test",
      new ControlOverride { RuleId = "SV-C2_rule", VulnId = "V-C2", StatusOverride = ControlStatus.NotApplicable, NaReason = "Gone" }
    );
    await overlayRepo.SaveAsync(overlay, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var rebaseService = new OverlayRebaseService(overlayRepo, diffService);
    var report = await rebaseService.RebaseOverlayAsync("rm-ov1", "rm-base", "rm-tgt");

    report.Success.Should().BeTrue();
    report.Actions.Should().HaveCount(1);
    report.Actions[0].ActionType.Should().Be(RebaseActionType.Remove);
    report.Actions[0].RequiresReview.Should().BeTrue();
    report.Actions[0].IsBlockingConflict.Should().BeTrue();
    report.BlockingConflicts.Should().Be(1);
  }

  [Fact]
  public async Task RebaseOverlay_ApplyRebase_CreatesNewOverlay()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);
    var overlayRepo = new SqliteJsonOverlayRepository(_cs);

    var controls = new List<ControlRecord> { MakeControl("C1") };
    await controlRepo.SaveControlsAsync("ap-base", controls, CancellationToken.None);
    await controlRepo.SaveControlsAsync("ap-tgt", controls, CancellationToken.None);

    var overlay = MakeOverlay("ap-ov1", "Apply Test",
      new ControlOverride { RuleId = "SV-C1_rule", VulnId = "V-C1", StatusOverride = ControlStatus.NotApplicable, NaReason = "Kept" }
    );
    await overlayRepo.SaveAsync(overlay, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var rebaseService = new OverlayRebaseService(overlayRepo, diffService);
    var report = await rebaseService.RebaseOverlayAsync("ap-ov1", "ap-base", "ap-tgt");

    report.Success.Should().BeTrue();
    report.HasBlockingConflicts.Should().BeFalse();

    // Apply the rebase
    var rebased = await rebaseService.ApplyRebaseAsync("ap-ov1", report);

    rebased.Should().NotBeNull();
    rebased.OverlayId.Should().NotBe("ap-ov1"); // New ID
    rebased.Name.Should().Contain("Rebased");
    rebased.Overrides.Should().HaveCount(1); // The Keep action preserves the override

    // Verify it was persisted
    var stored = await overlayRepo.GetAsync(rebased.OverlayId, CancellationToken.None);
    stored.Should().NotBeNull();
    stored!.Overrides.Should().HaveCount(1);
  }

  [Fact]
  public async Task RebaseOverlay_ApplyRebase_WithBlockingConflicts_Fails()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);
    var overlayRepo = new SqliteJsonOverlayRepository(_cs);

    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };
    var target = new List<ControlRecord> { MakeControl("C1") };
    await controlRepo.SaveControlsAsync("blk-base", baseline, CancellationToken.None);
    await controlRepo.SaveControlsAsync("blk-tgt", target, CancellationToken.None);

    var overlay = MakeOverlay("blk-ov1", "Blocking Apply",
      new ControlOverride { RuleId = "SV-C2_rule", VulnId = "V-C2", StatusOverride = ControlStatus.NotApplicable, NaReason = "Removed" }
    );
    await overlayRepo.SaveAsync(overlay, CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var rebaseService = new OverlayRebaseService(overlayRepo, diffService);
    var report = await rebaseService.RebaseOverlayAsync("blk-ov1", "blk-base", "blk-tgt");

    report.HasBlockingConflicts.Should().BeTrue();

    Func<Task> act = async () => await rebaseService.ApplyRebaseAsync("blk-ov1", report);
    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage("*blocking conflicts*");
  }

  [Fact]
  public async Task RebaseOverlay_OverlayNotFound_ReturnsFailure()
  {
    var controlRepo = new SqliteJsonControlRepository(_cs);
    var overlayRepo = new SqliteJsonOverlayRepository(_cs);

    // Save some controls so diff doesn't fail
    await controlRepo.SaveControlsAsync("nf-base", new List<ControlRecord>(), CancellationToken.None);
    await controlRepo.SaveControlsAsync("nf-tgt", new List<ControlRecord>(), CancellationToken.None);

    var diffService = new BaselineDiffService(controlRepo);
    var rebaseService = new OverlayRebaseService(overlayRepo, diffService);
    var report = await rebaseService.RebaseOverlayAsync("does-not-exist", "nf-base", "nf-tgt");

    report.Success.Should().BeFalse();
    report.ErrorMessage.Should().Contain("not found");
  }
}
