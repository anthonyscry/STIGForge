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
  public void BuildContentImportPlan_AllPlannedOperations_EmitPlannedState()
  {
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\stig.zip",
        FileName = "stig.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:win11"
      },
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\scap.zip",
        FileName = "scap.zip",
        ArtifactKind = ImportArtifactKind.Scap,
        Confidence = DetectionConfidence.High,
        ContentKey = "scap:win11"
      },
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\gpo.zip",
        FileName = "gpo.zip",
        ArtifactKind = ImportArtifactKind.Gpo,
        Confidence = DetectionConfidence.High,
        ContentKey = "gpo:win11"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    Assert.All(plan, op => Assert.Equal(ImportOperationState.Planned, op.State));
  }

  [Fact]
  public void BuildContentImportPlan_PlannedOperations_HaveNullFailureReason()
  {
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\stig.zip",
        FileName = "stig.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:win11"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    var op = Assert.Single(plan);
    Assert.Null(op.FailureReason);
  }

  [Fact]
  public void BuildContentImportPlan_GpoAndAdmxDualRoute_BothOperationsEmitPlannedState()
  {
    var zipPath = @"C:\import\dual-route.zip";
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "dual-route.zip",
        ArtifactKind = ImportArtifactKind.Gpo,
        Confidence = DetectionConfidence.High,
        ContentKey = "gpo:dual"
      },
      new ImportInboxCandidate
      {
        ZipPath = zipPath,
        FileName = "dual-route.zip",
        ArtifactKind = ImportArtifactKind.Admx,
        Confidence = DetectionConfidence.High,
        ContentKey = "admx:dual"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    Assert.Equal(2, plan.Count);
    Assert.All(plan, op => Assert.Equal(ImportOperationState.Planned, op.State));
  }

  [Fact]
  public void BuildContentImportPlan_MultipleZips_IsStablyOrderedByZipPath()
  {
    var candidates = new[]
    {
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\z-last.zip",
        FileName = "z-last.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:z"
      },
      new ImportInboxCandidate
      {
        ZipPath = @"C:\import\a-first.zip",
        FileName = "a-first.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Confidence = DetectionConfidence.High,
        ContentKey = "stig:a"
      }
    };

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);

    Assert.Equal(2, plan.Count);
    Assert.Contains("a-first.zip", plan[0].ZipPath, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("z-last.zip", plan[1].ZipPath, StringComparison.OrdinalIgnoreCase);
    Assert.All(plan, op => Assert.Equal(ImportOperationState.Planned, op.State));
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
