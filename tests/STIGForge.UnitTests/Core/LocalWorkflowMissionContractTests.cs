using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Core;

public sealed class LocalWorkflowMissionContractTests
{
  [Fact]
  public void Mission_Defaults_IncludeCanonicalChecklistScannerEvidenceUnmappedAndMetadataCollections()
  {
    var mission = new LocalWorkflowMission();

    Assert.NotNull(mission.CanonicalChecklist);
    Assert.Empty(mission.CanonicalChecklist);
    Assert.NotNull(mission.ScannerEvidence);
    Assert.Empty(mission.ScannerEvidence);
    Assert.NotNull(mission.Unmapped);
    Assert.Empty(mission.Unmapped);
    Assert.NotNull(mission.Diagnostics);
    Assert.Empty(mission.Diagnostics);
    Assert.NotNull(mission.StageMetadata);
    Assert.Equal(string.Empty, mission.StageMetadata.MissionJsonPath);
  }

  [Fact]
  public void WorkflowResult_Defaults_IncludeMissionContract()
  {
    var result = new LocalWorkflowResult();

    Assert.NotNull(result.Mission);
    Assert.NotNull(result.Mission.CanonicalChecklist);
    Assert.NotNull(result.Mission.ScannerEvidence);
    Assert.NotNull(result.Mission.Unmapped);
    Assert.NotNull(result.Mission.Diagnostics);
    Assert.NotNull(result.Mission.StageMetadata);
  }
}
