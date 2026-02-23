using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Core;

public sealed class LocalWorkflowMissionContractTests
{
  [Fact]
  public void Mission_Defaults_IncludeCanonicalChecklistAndUnmappedCollections()
  {
    var mission = new LocalWorkflowMission();

    Assert.NotNull(mission.CanonicalChecklist);
    Assert.Empty(mission.CanonicalChecklist);
    Assert.NotNull(mission.Unmapped);
    Assert.Empty(mission.Unmapped);
  }

  [Fact]
  public void WorkflowResult_Defaults_IncludeMissionContract()
  {
    var result = new LocalWorkflowResult();

    Assert.NotNull(result.Mission);
    Assert.NotNull(result.Mission.CanonicalChecklist);
    Assert.NotNull(result.Mission.Unmapped);
  }
}
