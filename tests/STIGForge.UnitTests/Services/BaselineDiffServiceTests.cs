using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class BaselineDiffServiceTests
{
  private static ControlRecord MakeControl(string id, string title = "Title", string severity = "medium",
    string? checkText = null, string? fixText = null, string? ruleId = null, string? vulnId = null)
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
        RuleId = ruleId ?? "SV-" + id + "_rule",
        VulnId = vulnId ?? "V-" + id,
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

  private static (BaselineDiffService svc, Mock<IControlRepository> repo) CreateService()
  {
    var repo = new Mock<IControlRepository>();
    var svc = new BaselineDiffService(repo.Object);
    return (svc, repo);
  }

  [Fact]
  public async Task ComparePacks_IdenticalPacks_NoChanges()
  {
    var (svc, repo) = CreateService();
    var controls = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(controls);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(controls);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalAdded.Should().Be(0);
    diff.TotalRemoved.Should().Be(0);
    diff.TotalModified.Should().Be(0);
    diff.TotalUnchanged.Should().Be(2);
  }

  [Fact]
  public async Task ComparePacks_AddedControls_DetectsAdditions()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1") };
    var target = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2"), MakeControl("C3") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalAdded.Should().Be(2);
    diff.TotalRemoved.Should().Be(0);
    diff.TotalUnchanged.Should().Be(1);
    diff.AddedControls.Should().HaveCount(2);
  }

  [Fact]
  public async Task ComparePacks_RemovedControls_DetectsRemovals()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1"), MakeControl("C2"), MakeControl("C3") };
    var target = new List<ControlRecord> { MakeControl("C1") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalRemoved.Should().Be(2);
    diff.TotalAdded.Should().Be(0);
    diff.TotalUnchanged.Should().Be(1);
    diff.RemovedControls.Should().HaveCount(2);
  }

  [Fact]
  public async Task ComparePacks_ModifiedTitle_DetectedAsLowImpact()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1", title: "Old Title") };
    var target = new List<ControlRecord> { MakeControl("C1", title: "New Title") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalModified.Should().Be(1);
    diff.ModifiedControls.Should().HaveCount(1);

    var modified = diff.ModifiedControls[0];
    modified.Changes.Should().ContainSingle(c => c.FieldName == "Title");
    modified.Changes[0].Impact.Should().Be(FieldChangeImpact.Low);
    modified.Changes[0].OldValue.Should().Be("Old Title");
    modified.Changes[0].NewValue.Should().Be("New Title");
  }

  [Fact]
  public async Task ComparePacks_ModifiedSeverity_DetectedAsHighImpact()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1", severity: "medium") };
    var target = new List<ControlRecord> { MakeControl("C1", severity: "high") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalModified.Should().Be(1);
    var changes = diff.ModifiedControls[0].Changes;
    changes.Should().ContainSingle(c => c.FieldName == "Severity" && c.Impact == FieldChangeImpact.High);
  }

  [Fact]
  public async Task ComparePacks_ModifiedCheckText_DetectedAsHighImpact()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1", checkText: "Old check") };
    var target = new List<ControlRecord> { MakeControl("C1", checkText: "New check instructions") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.ModifiedControls[0].Changes.Should().ContainSingle(c => c.FieldName == "CheckText" && c.Impact == FieldChangeImpact.High);
  }

  [Fact]
  public async Task ComparePacks_MultipleChanges_AllFieldsDetected()
  {
    var (svc, repo) = CreateService();
    var baseline = new List<ControlRecord> { MakeControl("C1", title: "Old", severity: "low", checkText: "Old check") };
    var target = new List<ControlRecord> { MakeControl("C1", title: "New", severity: "high", checkText: "New check") };

    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(baseline);
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(target);

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.ModifiedControls[0].Changes.Should().HaveCount(3);
    diff.ModifiedControls[0].Changes.Select(c => c.FieldName).Should().Contain("Title", "Severity", "CheckText");
  }

  [Fact]
  public async Task ComparePacks_EmptyBaseline_AllAdditions()
  {
    var (svc, repo) = CreateService();
    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlRecord>());
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") });

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalAdded.Should().Be(2);
    diff.TotalRemoved.Should().Be(0);
    diff.TotalUnchanged.Should().Be(0);
  }

  [Fact]
  public async Task ComparePacks_EmptyTarget_AllRemovals()
  {
    var (svc, repo) = CreateService();
    repo.Setup(r => r.ListControlsAsync("baseline", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlRecord> { MakeControl("C1"), MakeControl("C2") });
    repo.Setup(r => r.ListControlsAsync("target", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ControlRecord>());

    var diff = await svc.ComparePacksAsync("baseline", "target");

    diff.TotalRemoved.Should().Be(2);
    diff.TotalAdded.Should().Be(0);
    diff.TotalUnchanged.Should().Be(0);
  }
}
