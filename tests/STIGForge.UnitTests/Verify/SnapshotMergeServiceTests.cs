using System;
using System.Linq;
using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public class SnapshotMergeServiceTests
{
  [Fact]
  public void Merge_PrefersSccOverEvaluate_ForSameBenchmarkAndControl()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = "V-1001", RuleId = "SV-1001", BenchmarkId = "Windows_11", Status = "Open", Tool = "Evaluate-STIG", FindingDetails = "eval evidence", Comments = "eval comment" },
      new ControlResult { VulnId = "V-1001", RuleId = "SV-1001", BenchmarkId = "Windows_11", Status = "NotAFinding", Tool = "SCC", FindingDetails = "scc evidence", Comments = "scc comment" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "MINI-TONY");

    merged.Should().ContainSingle();
    var result = merged[0];
    result.Status.Should().Be("NotAFinding");
    result.Tool.Should().Be("SCC");
    result.AssetId.Should().Be("MINI-TONY");
    result.FindingDetails.Should().Contain("[SCC] scc evidence").And.Contain("[Evaluate-STIG] eval evidence");
    result.Comments.Should().Contain("[SCC] scc comment").And.Contain("[Evaluate-STIG] eval comment");
  }

  [Fact]
  public void Merge_ManualToolOverridesLowerPrecedence()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = "V-1002", RuleId = "SV-1002", BenchmarkId = "Windows_11", Status = "Open", Tool = "SCC", FindingDetails = "scc evidence" },
      new ControlResult { VulnId = "V-1002", RuleId = "SV-1002", BenchmarkId = "Windows_11", Status = "NotAFinding", Tool = "Manual CKL", FindingDetails = "manual evidence", Comments = "manual comment" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-1");

    merged.Should().ContainSingle();
    var result = merged[0];
    result.Status.Should().Be("NotAFinding");
    result.Tool.Should().Be("Manual CKL");
    result.Comments.Should().StartWith("[Manual CKL]");
  }

  [Fact]
  public void Merge_KeyFallsBackToRuleAndTitleAcrossSources()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = null, RuleId = "SV-2000", Title = "Rule Title", BenchmarkId = "Windows_11", Status = "Open", Tool = "Evaluate-STIG" },
      new ControlResult { VulnId = null, RuleId = "SV-2000", Title = "Rule Title", BenchmarkId = "Windows_11", Status = "NotAFinding", Tool = "SCC" },
      new ControlResult { VulnId = null, RuleId = null, Title = "Rule Title", BenchmarkId = "Windows_11", Status = "NotAFinding", Tool = "Manual", Comments = "manual" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-2");
    merged.Should().ContainSingle();
  }

  [Fact]
  public void Merge_AppendsEvidenceCommentsDeterministically()
  {
    var controls = new[]
    {
      new ControlResult { VulnId = "V-3000", RuleId = "SV-3000", BenchmarkId = "Windows_11", Tool = "Evaluate-STIG", Status = "Fail", FindingDetails = "eval details", Comments = "eval comment" },
      new ControlResult { VulnId = "V-3000", RuleId = "SV-3000", BenchmarkId = "Windows_11", Tool = "SCC", Status = "NotAFinding", FindingDetails = "scc details", Comments = "scc comment" },
      new ControlResult { VulnId = "V-3000", RuleId = "SV-3000", BenchmarkId = "Windows_11", Tool = "Manual", Status = "NotAFinding", Comments = "manual comment" },
      new ControlResult { VulnId = "V-4000", RuleId = "SV-4000", BenchmarkId = "Windows_11", Tool = "Evaluate-STIG", Status = "Open", FindingDetails = "second details" }
    }.Reverse().ToArray();

    var merged = SnapshotMergeService.Merge(controls, "HOST-3");

    merged.Select(r => r.VulnId).Should().Equal(new[] { "V-3000", "V-4000" });
    var primary = merged.First(r => r.VulnId == "V-3000");
    primary.FindingDetails.Should().StartWith("[SCC]").And.Contain("[Evaluate-STIG]");
    primary.Comments.Should().StartWith("[Manual]").And.Contain("[SCC]").And.Contain("[Evaluate-STIG]");
  }

  [Fact]
  public void Merge_SameTitleDifferentVulnId_DoesNotCombine()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = "V-9000", RuleId = "SV-9000", Title = "Shared Title", BenchmarkId = "Windows_11", Tool = "SCC", Status = "Open" },
      new ControlResult { VulnId = "V-9001", RuleId = "SV-9001", Title = "Shared Title", BenchmarkId = "Windows_11", Tool = "Evaluate-STIG", Status = "Open" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-4");

    merged.Should().HaveCount(2);
    merged.Select(r => r.VulnId).Should().BeEquivalentTo(new[] { "V-9000", "V-9001" });
  }

  [Fact]
  public void Merge_WinnerMissingIdentifier_RetainsLowerPrecedenceValue()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = null, RuleId = "SV-1003", Title = "Title", BenchmarkId = "Windows_11", Tool = "Manual CKL", Status = "NotAFinding" },
      new ControlResult { VulnId = "V-1003", RuleId = "SV-1003", Title = "Title", BenchmarkId = "Windows_11", Tool = "SCC", Status = "Open" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-5");

    merged.Should().ContainSingle();
    merged[0].VulnId.Should().Be("V-1003");
  }

  [Fact]
  public void Merge_SameToolPrefersLatestVerifiedAt()
  {
    var inputs = new[]
    {
      new ControlResult { VulnId = "V-1100", RuleId = "SV-1100", Title = "Tie", BenchmarkId = "Windows_11", Tool = "SCC", Status = "Open", VerifiedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z") },
      new ControlResult { VulnId = "V-1100", RuleId = "SV-1100", Title = "Tie", BenchmarkId = "Windows_11", Tool = "SCC", Status = "NotAFinding", VerifiedAt = DateTimeOffset.Parse("2025-02-01T00:00:00Z") }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-6");

    merged.Should().ContainSingle();
    merged[0].Status.Should().Be("NotAFinding");
  }

  [Fact]
  public void Merge_SameControlDifferentAssets_RemainsSeparate()
  {
    var inputs = new[]
    {
      new ControlResult { AssetId = "HOST-A", VulnId = "V-1500", RuleId = "SV-1500", BenchmarkId = "Windows_11", Tool = "SCC", Status = "Open" },
      new ControlResult { AssetId = "HOST-B", VulnId = "V-1500", RuleId = "SV-1500", BenchmarkId = "Windows_11", Tool = "SCC", Status = "NotAFinding" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "DEFAULT");

    merged.Should().HaveCount(2);
    merged.Select(r => r.AssetId).Should().BeEquivalentTo(new[] { "HOST-A", "HOST-B" });
  }

  [Fact]
  public void Merge_TieWithSameToolAndVerifiedAt_OrdersByStableTieBreakers()
  {
    var tieTime = DateTimeOffset.Parse("2025-03-01T00:00:00Z");
    var inputs = new[]
    {
      new ControlResult { VulnId = "V-1600", RuleId = "SV-1600", BenchmarkId = "Windows_11", Tool = "SCC", Status = "Open", VerifiedAt = tieTime, SourceFile = "source-b" },
      new ControlResult { VulnId = "V-1600", RuleId = "SV-1600", BenchmarkId = "Windows_11", Tool = "SCC", Status = "NotAFinding", VerifiedAt = tieTime, SourceFile = "source-a" }
    };

    var merged = SnapshotMergeService.Merge(inputs, "HOST-7");

    merged.Should().ContainSingle();
    merged[0].SourceFile.Should().Be("source-a");
    merged[0].Status.Should().Be("NotAFinding");
  }
}
