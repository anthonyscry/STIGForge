using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class ImportQueuePlannerTests
{
  [Fact]
  public void BuildContentImportPlan_GpoAndAdmxFromSameZip_PlansBothRoutes()
  {
    var zipPath = @"C:\import\win11-gpo.zip";
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "win11-gpo.zip",
        ArtifactKind = ImportArtifactKind.Gpo,
        Confidence = DetectionConfidence.High,
        ContentKey = "gpo:win11"
      },
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "win11-gpo.zip",
        ArtifactKind = ImportArtifactKind.Admx,
        Confidence = DetectionConfidence.High,
        ContentKey = "admx:microsoft"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    Assert.Equal(2, plan.Count);

    var gpoOp = Assert.Single(plan, p => p.ArtifactKind == ImportArtifactKind.Gpo);
    Assert.Equal(ContentImportRoute.ConsolidatedZip, gpoOp.Route);
    Assert.Equal("gpo_lgpo_import", gpoOp.SourceLabel);

    var admxOp = Assert.Single(plan, p => p.ArtifactKind == ImportArtifactKind.Admx);
    Assert.Equal(ContentImportRoute.AdmxTemplatesFromZip, admxOp.Route);
    Assert.Equal("admx_template_import", admxOp.SourceLabel);
  }

  [Fact]
  public void BuildContentImportPlan_StigAndAdmxFromSameZip_UsesSinglePrimaryRoute()
  {
    var zipPath = @"C:\import\mixed.zip";
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "mixed.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:win11"
      },
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "mixed.zip",
        ArtifactKind = ImportArtifactKind.Admx,
        Confidence = DetectionConfidence.High,
        ContentKey = "admx:microsoft"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    var op = Assert.Single(plan);
    Assert.Equal(ImportArtifactKind.Stig, op.ArtifactKind);
    Assert.Equal(ContentImportRoute.ConsolidatedZip, op.Route);
    Assert.Equal("stig_import", op.SourceLabel);
  }

  [Fact]
  public void BuildContentImportPlan_FailedThenRetriedOnlySkipsAfterSuccessfulMarkProcessed()
  {
    var zipPath = @"C:\import\retryable.zip";
    var winners = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "retryable.zip",
        Sha256 = "retry-hash",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:retryable"
      }
    };

    var ledger = new ImportProcessedArtifactLedger();

    var firstPlan = ImportQueuePlanner.BuildContentImportPlan(winners, ledger);
    var secondPlan = ImportQueuePlanner.BuildContentImportPlan(winners, ledger);

    Assert.Single(firstPlan);
    Assert.Single(secondPlan);

    var planned = firstPlan.Single();
    ledger.MarkProcessed(planned.Sha256, planned.Route);

    var thirdPlan = ImportQueuePlanner.BuildContentImportPlan(winners, ledger);
    Assert.Empty(thirdPlan);
  }
}
