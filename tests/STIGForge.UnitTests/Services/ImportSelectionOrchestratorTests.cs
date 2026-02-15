using FluentAssertions;
using STIGForge.Core.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace STIGForge.UnitTests.Services;

public sealed class ImportSelectionOrchestratorTests
{
  [Fact]
  public void BuildPlan_WithShuffledInput_ProducesStableOrdering()
  {
    var orchestrator = new ImportSelectionOrchestrator();
    var input = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Gpo, Id = "gpo-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-b" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Admx, Id = "admx-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a" }
    };

    var plan = orchestrator.BuildPlan(input);

    plan.Rows.Select(x => x.Id).Should().Equal("stig-a", "stig-b", "scap-a", "gpo-a", "admx-a");
  }

  [Fact]
  public void BuildPlan_MissingScapForSelectedStig_KeepsStigAndEmitsWarning()
  {
    var orchestrator = new ImportSelectionOrchestrator();
    var input = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Gpo, Id = "gpo-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Admx, Id = "admx-a" }
    };

    var plan = orchestrator.BuildPlan(input);

    plan.Rows.Should().ContainSingle(x => x.Id == "stig-a" && x.IsSelected);
    plan.Warnings.Should().ContainSingle(x => x.Code == "missing_scap_dependency" && x.Severity == "warning");
  }

  [Fact]
  public void BuildPlan_SelectedStig_AutoIncludesScapGpoAndAdmxAsLocked()
  {
    var orchestrator = new ImportSelectionOrchestrator();
    var input = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Gpo, Id = "gpo-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Admx, Id = "admx-a" }
    };

    var plan = orchestrator.BuildPlan(input);

    plan.Rows.Should().ContainSingle(x => x.ArtifactType == ImportSelectionArtifactType.Scap && x.IsSelected && x.IsLocked);
    plan.Rows.Should().ContainSingle(x => x.ArtifactType == ImportSelectionArtifactType.Gpo && x.IsSelected && x.IsLocked);
    plan.Rows.Should().ContainSingle(x => x.ArtifactType == ImportSelectionArtifactType.Admx && x.IsSelected && x.IsLocked);
  }

  [Fact]
  public void BuildPlan_CountsUseOnlySelectedStigControlsAndRules()
  {
    var orchestrator = new ImportSelectionOrchestrator();
    var input = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true, StigRuleCount = 200 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-b", IsSelected = true, StigRuleCount = 220 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-c", IsSelected = false, StigRuleCount = 180 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-b" }
    };

    var plan = orchestrator.BuildPlan(input);

    plan.Counts.StigSelected.Should().Be(2);
    plan.Counts.ScapAutoIncluded.Should().Be(2);
    plan.Counts.RuleCount.Should().Be(420);
  }

  [Fact]
  public void BuildPlan_EquivalentLogicalInput_ProducesSameFingerprint()
  {
    var orchestrator = new ImportSelectionOrchestrator();

    var inputA = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-b", IsSelected = true, StigRuleCount = 220 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true, StigRuleCount = 200 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-b" }
    };

    var inputB = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-b" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true, StigRuleCount = 200 },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-b", IsSelected = true, StigRuleCount = 220 }
    };

    var planA = orchestrator.BuildPlan(inputA);
    var planB = orchestrator.BuildPlan(inputB);

    planA.Fingerprint.Should().Be(planB.Fingerprint);
  }

  [Fact]
  public void BuildPlan_DelimiterRichDistinctPlans_ProduceDistinctFingerprints()
  {
    var orchestrator = new ImportSelectionOrchestrator();

    var inputA = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "aaa", IsSelected = true },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Gpo, Id = "bbb" }
    };

    var inputB = new[]
    {
      new ImportSelectionCandidate
      {
        ArtifactType = ImportSelectionArtifactType.Scap,
        Id = "aaa:True:False|row:2:bbb",
        IsSelected = false
      }
    };

    var planA = orchestrator.BuildPlan(inputA);
    var planB = orchestrator.BuildPlan(inputB);

    planA.Rows.Should().NotBeEquivalentTo(planB.Rows);
    planA.Fingerprint.Should().NotBe(planB.Fingerprint);
    planA.Fingerprint.Should().Be(BuildExpectedCanonicalFingerprint(planA));
    planB.Fingerprint.Should().Be(BuildExpectedCanonicalFingerprint(planB));
  }

  [Fact]
  public void BuildPlan_ProducesStatusSummaryTextUsedByViewModel()
  {
    var orchestrator = new ImportSelectionOrchestrator();
    var input = new[]
    {
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Stig, Id = "stig-a", IsSelected = true },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Scap, Id = "scap-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Gpo, Id = "gpo-a" },
      new ImportSelectionCandidate { ArtifactType = ImportSelectionArtifactType.Admx, Id = "admx-a" }
    };

    var plan = orchestrator.BuildPlan(input);

    plan.StatusSummaryText.Should().Be("STIG: 1 | Auto SCAP: 1 | Auto GPO: 1 | Auto ADMX: 1");
  }

  private static string BuildExpectedCanonicalFingerprint(ImportSelectionPlan plan)
  {
    var canonical = new
    {
      rows = plan.Rows.Select(x => new
      {
        artifactType = (int)x.ArtifactType,
        id = x.Id.ToUpperInvariant(),
        isSelected = x.IsSelected,
        isLocked = x.IsLocked
      }),
      warnings = plan.Warnings
        .OrderBy(x => x.Code, StringComparer.Ordinal)
        .ThenBy(x => x.Severity, StringComparer.Ordinal)
        .Select(x => new
        {
          code = x.Code,
          severity = x.Severity
        }),
      counts = new
      {
        stigSelected = plan.Counts.StigSelected,
        scapAutoIncluded = plan.Counts.ScapAutoIncluded,
        ruleCount = plan.Counts.RuleCount
      }
    };

    var json = JsonSerializer.Serialize(canonical);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
