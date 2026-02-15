using FluentAssertions;
using STIGForge.Core.Services;

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

    plan.Select(x => x.Id).Should().Equal("stig-a", "stig-b", "scap-a", "gpo-a", "admx-a");
  }
}
