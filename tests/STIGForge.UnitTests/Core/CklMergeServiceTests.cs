using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class CklMergeServiceTests : IDisposable
{
  private readonly string _tempDir;

  public CklMergeServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-ckl-merge-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);
    }
    catch
    {
    }
  }

  [Fact]
  public async Task MergeAsync_AlignedStatuses_ProducesNoConflicts()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-1000",
      RuleId = "SV-1000",
      RuleTitle = "Sample",
      Severity = "medium",
      Status = "NotAFinding"
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-1000",
        RuleId = "SV-1000",
        Status = "Pass",
        SourceFile = CreateResultFile("aligned.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

    merged.MergedFindings.Should().HaveCount(1);
    merged.Conflicts.Should().BeEmpty();
    merged.MergedFindings[0].Status.Should().Be("NotAFinding");
  }

  [Fact]
  public async Task MergeAsync_ManualStrategy_RecordsConflictAndKeepsCklStatus()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-2000",
      RuleId = "SV-2000",
      RuleTitle = "ManualConflict",
      Severity = "high",
      Status = "Open"
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-2000",
        RuleId = "SV-2000",
        Status = "Pass",
        SourceFile = CreateResultFile("manual.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.Manual, CancellationToken.None);

    merged.Conflicts.Should().HaveCount(1);
    merged.Conflicts[0].RequiresManualResolution.Should().BeTrue();
    merged.MergedFindings[0].Status.Should().Be("Open");
  }

  [Fact]
  public async Task MergeAsync_StigForgeWins_UsesStigForgeStatus()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-3000",
      RuleId = "SV-3000",
      RuleTitle = "StigWins",
      Severity = "low",
      Status = "Open"
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-3000",
        RuleId = "SV-3000",
        Status = "NotApplicable",
        SourceFile = CreateResultFile("stigwins.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.StigForgeWins, CancellationToken.None);

    merged.MergedFindings[0].Status.Should().Be("Not_Applicable");
    merged.Conflicts.Should().HaveCount(1);
  }

  [Fact]
  public async Task MergeAsync_CklWins_UsesCklStatus()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-4000",
      RuleId = "SV-4000",
      RuleTitle = "CklWins",
      Severity = "medium",
      Status = "Not_Reviewed"
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-4000",
        RuleId = "SV-4000",
        Status = "Fail",
        SourceFile = CreateResultFile("cklwins.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.CklWins, CancellationToken.None);

    merged.MergedFindings[0].Status.Should().Be("Not_Reviewed");
    merged.Conflicts.Should().HaveCount(1);
  }

  [Fact]
  public async Task MergeAsync_MostRecent_UsesStigForgeWhenVerifyFileIsNewer()
  {
    var service = new CklMergeService();
    var importedAt = new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-5000",
      RuleId = "SV-5000",
      RuleTitle = "MostRecent",
      Severity = "medium",
      Status = "Open"
    }, importedAt);

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-5000",
        RuleId = "SV-5000",
        Status = "Pass",
        SourceFile = CreateResultFile("recent.json", importedAt.AddHours(2))
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

    merged.MergedFindings[0].Status.Should().Be("NotAFinding");
  }

  [Fact]
  public async Task MergeAsync_AddsResultsMissingFromCkl()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-6000",
      RuleId = "SV-6000",
      RuleTitle = "Baseline",
      Severity = "medium",
      Status = "NotAFinding"
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-7000",
        RuleId = "SV-7000",
        Status = "Fail",
        Title = "New Verify Finding",
        Severity = "high",
        SourceFile = CreateResultFile("extra.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

    merged.MergedFindings.Should().HaveCount(2);
    merged.MergedFindings.Should().Contain(f => f.VulnId == "V-7000" && f.Status == "Open");
  }

  [Fact]
  public async Task MergeAsync_UsesVerifyCommentsWhenCklCommentMissing()
  {
    var service = new CklMergeService();
    var checklist = BuildChecklist(new CklFinding
    {
      VulnId = "V-8000",
      RuleId = "SV-8000",
      RuleTitle = "CommentMerge",
      Severity = "medium",
      Status = "Not_Reviewed",
      Comments = null
    });

    var results = new[]
    {
      new ControlResult
      {
        VulnId = "V-8000",
        RuleId = "SV-8000",
        Status = "NotReviewed",
        Comments = "Captured in verify run",
        SourceFile = CreateResultFile("comments.json", DateTimeOffset.UtcNow)
      }
    };

    var merged = await service.MergeAsync(checklist, results, CklConflictResolutionStrategy.MostRecent, CancellationToken.None);

    merged.MergedFindings[0].Comments.Should().Be("Captured in verify run");
  }

  private static CklChecklist BuildChecklist(CklFinding finding, DateTimeOffset? importedAt = null)
  {
    return new CklChecklist
    {
      ImportedAt = importedAt ?? DateTimeOffset.UtcNow,
      AssetName = "host",
      HostName = "host",
      StigTitle = "Test STIG",
      StigVersion = "1",
      Findings = [finding]
    };
  }

  private string CreateResultFile(string fileName, DateTimeOffset timestamp)
  {
    var path = Path.Combine(_tempDir, fileName);
    File.WriteAllText(path, "{}");
    File.SetLastWriteTimeUtc(path, timestamp.UtcDateTime);
    return path;
  }
}
