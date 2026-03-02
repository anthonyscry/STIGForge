using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class StigReleaseMonitorServiceTests
{
  [Fact]
  public async Task CheckForNewReleases_NewerPackExists_ReturnsNewReleaseFound()
  {
    var current = BuildPack("pack-current", importedAt: new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero));
    var newer = BuildPack("pack-new", importedAt: new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero));

    var packRepo = new Mock<IContentPackRepository>();
    packRepo.Setup(r => r.GetAsync("pack-current", It.IsAny<CancellationToken>())).ReturnsAsync(current);
    packRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { current, newer });

    var releaseRepo = new Mock<IReleaseCheckRepository>();
    ReleaseCheck? saved = null;
    releaseRepo.Setup(r => r.SaveAsync(It.IsAny<ReleaseCheck>(), It.IsAny<CancellationToken>()))
      .Callback<ReleaseCheck, CancellationToken>((check, _) => saved = check)
      .Returns(Task.CompletedTask);

    var svc = CreateService(packRepo.Object, releaseRepo.Object, now: new DateTimeOffset(2026, 2, 3, 8, 0, 0, TimeSpan.Zero));
    var result = await svc.CheckForNewReleasesAsync("pack-current", CancellationToken.None);

    result.Status.Should().Be("NewReleaseFound");
    result.TargetPackId.Should().Be("pack-new");
    result.BaselinePackId.Should().Be("pack-current");
    saved.Should().NotBeNull();
    saved!.CheckId.Should().Be(result.CheckId);

    using var doc = JsonDocument.Parse(result.SummaryJson!);
    doc.RootElement.GetProperty("NewerPackCount").GetInt32().Should().Be(1);
    doc.RootElement.GetProperty("LatestPackId").GetString().Should().Be("pack-new");
  }

  [Fact]
  public async Task CheckForNewReleases_NoNewerPack_ReturnsNoNewRelease()
  {
    var current = BuildPack("pack-current", importedAt: new DateTimeOffset(2026, 2, 2, 9, 0, 0, TimeSpan.Zero));
    var older = BuildPack("pack-old", importedAt: new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero));

    var packRepo = new Mock<IContentPackRepository>();
    packRepo.Setup(r => r.GetAsync("pack-current", It.IsAny<CancellationToken>())).ReturnsAsync(current);
    packRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { current, older });

    var releaseRepo = new Mock<IReleaseCheckRepository>();
    var svc = CreateService(packRepo.Object, releaseRepo.Object, now: new DateTimeOffset(2026, 2, 3, 8, 0, 0, TimeSpan.Zero));

    var result = await svc.CheckForNewReleasesAsync("pack-current", CancellationToken.None);

    result.Status.Should().Be("NoNewRelease");
    result.TargetPackId.Should().BeNull();
    result.SummaryJson.Should().BeNull();
    releaseRepo.Verify(r => r.SaveAsync(It.IsAny<ReleaseCheck>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task CheckForNewReleases_PackNotFound_ThrowsArgumentException()
  {
    var packRepo = new Mock<IContentPackRepository>();
    packRepo.Setup(r => r.GetAsync("missing-pack", It.IsAny<CancellationToken>())).ReturnsAsync((ContentPack?)null);

    var releaseRepo = new Mock<IReleaseCheckRepository>();
    var svc = CreateService(packRepo.Object, releaseRepo.Object, now: DateTimeOffset.UtcNow);

    var act = () => svc.CheckForNewReleasesAsync("missing-pack", CancellationToken.None);
    await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Pack not found*");
    releaseRepo.Verify(r => r.SaveAsync(It.IsAny<ReleaseCheck>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task GenerateReleaseNotes_WithAddedAndRemoved_CalculatesCounts()
  {
    var controlsRepo = new Mock<IControlRepository>();
    controlsRepo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", severity: "medium"),
      BuildControl("C2", severity: "low")
    });
    controlsRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C2", severity: "low"),
      BuildControl("C3", severity: "medium")
    });

    var svc = CreateService(new Mock<IContentPackRepository>().Object, new Mock<IReleaseCheckRepository>().Object, DateTimeOffset.UtcNow, controlsRepo.Object);
    var notes = await svc.GenerateReleaseNotesAsync("baseline", "target", CancellationToken.None);

    notes.AddedCount.Should().Be(1);
    notes.RemovedCount.Should().Be(1);
    notes.ModifiedCount.Should().Be(0);
    notes.ComplianceImpact.Should().NotBeNull();
    notes.ComplianceImpact!.NewControlsRequiringReview.Should().Be(1);
    notes.ComplianceImpact.RemovedControlsAffectingScore.Should().Be(1);
  }

  [Fact]
  public async Task GenerateReleaseNotes_SeverityEscalation_Highlighted()
  {
    var controlsRepo = new Mock<IControlRepository>();
    controlsRepo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", severity: "medium")
    });
    controlsRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", severity: "high")
    });

    var svc = CreateService(new Mock<IContentPackRepository>().Object, new Mock<IReleaseCheckRepository>().Object, DateTimeOffset.UtcNow, controlsRepo.Object);
    var notes = await svc.GenerateReleaseNotesAsync("baseline", "target", CancellationToken.None);

    notes.SeverityChangedCount.Should().Be(1);
    notes.ComplianceImpact!.SeverityEscalations.Should().Be(1);
    notes.HighlightedChanges.Should().Contain(h => h.StartsWith("ESCALATION:", StringComparison.Ordinal));
  }

  [Fact]
  public async Task GenerateReleaseNotes_NewCatIRule_Highlighted()
  {
    var controlsRepo = new Mock<IControlRepository>();
    controlsRepo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ControlRecord>());
    controlsRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", title: "Critical control", severity: "high")
    });

    var svc = CreateService(new Mock<IContentPackRepository>().Object, new Mock<IReleaseCheckRepository>().Object, DateTimeOffset.UtcNow, controlsRepo.Object);
    var notes = await svc.GenerateReleaseNotesAsync("baseline", "target", CancellationToken.None);

    notes.AddedCount.Should().Be(1);
    notes.HighlightedChanges.Should().Contain(h => h.StartsWith("NEW CAT I:", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ImportAndDiff_PersistsReleaseCheck_WithDiffGeneratedStatus()
  {
    var controlsRepo = new Mock<IControlRepository>();
    controlsRepo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", severity: "medium")
    });
    controlsRepo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
    {
      BuildControl("C1", severity: "high")
    });

    var releaseRepo = new Mock<IReleaseCheckRepository>();
    ReleaseCheck? saved = null;
    releaseRepo.Setup(r => r.SaveAsync(It.IsAny<ReleaseCheck>(), It.IsAny<CancellationToken>()))
      .Callback<ReleaseCheck, CancellationToken>((check, _) => saved = check)
      .Returns(Task.CompletedTask);

    var svc = CreateService(new Mock<IContentPackRepository>().Object, releaseRepo.Object, DateTimeOffset.UtcNow, controlsRepo.Object);
    var check = await svc.ImportAndDiffAsync("baseline", "target", CancellationToken.None);

    check.Status.Should().Be("DiffGenerated");
    check.TargetPackId.Should().Be("target");
    check.SummaryJson.Should().NotBeNullOrWhiteSpace();
    saved.Should().NotBeNull();
    saved!.Status.Should().Be("DiffGenerated");
    saved.BaselinePackId.Should().Be("baseline");
    saved.TargetPackId.Should().Be("target");
  }

  private static StigReleaseMonitorService CreateService(
    IContentPackRepository packRepo,
    IReleaseCheckRepository releaseRepo,
    DateTimeOffset now,
    IControlRepository? controlsRepo = null)
  {
    var clock = new Mock<IClock>();
    clock.SetupGet(c => c.Now).Returns(now);
    var diff = new BaselineDiffService(controlsRepo ?? new Mock<IControlRepository>().Object);
    return new StigReleaseMonitorService(packRepo, diff, releaseRepo, complianceTrend: null, clock: clock.Object);
  }

  private static ContentPack BuildPack(string packId, DateTimeOffset importedAt) => new()
  {
    PackId = packId,
    Name = packId,
    ImportedAt = importedAt,
    SourceLabel = "local",
    Version = "V1R1",
    Release = "1"
  };

  private static ControlRecord BuildControl(string id, string title = "Title", string severity = "medium") => new()
  {
    ControlId = id,
    SourcePackId = "pack",
    Title = title,
    Severity = severity,
    CheckText = "Check " + id,
    FixText = "Fix " + id,
    IsManual = false,
    ExternalIds = new ExternalIds
    {
      RuleId = "SV-" + id + "_rule",
      VulnId = "V-" + id,
      BenchmarkId = "WIN11"
    },
    Applicability = new Applicability
    {
      OsTarget = OsTarget.Win11,
      RoleTags = Array.Empty<RoleTemplate>(),
      ClassificationScope = ScopeTag.Both,
      Confidence = Confidence.High
    },
    Revision = new RevisionInfo { PackName = "Pack" }
  };
}
