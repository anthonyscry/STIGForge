using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class ImportAutoQueueProjectionTests
{
  [Fact]
  public void Project_SplitsRowsIntoCommittedAndExceptions()
  {
    var planned = new List<PlannedContentImport>
    {
      new()
      {
        ZipPath = @"C:\import\alpha.zip",
        FileName = "alpha.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "stig_import"
      },
      new()
      {
        ZipPath = @"C:\import\bravo.zip",
        FileName = "bravo.zip",
        ArtifactKind = ImportArtifactKind.Admx,
        Route = ContentImportRoute.AdmxTemplatesFromZip,
        SourceLabel = "admx_template_import"
      }
    };

    var failures = new[]
    {
      "bravo.zip (Parse error: invalid XML)",
      "ignored-entry"
    };

    var result = ImportAutoQueueProjection.Project(planned, failures);

    var committed = Assert.Single(result.AutoCommitted);
    Assert.Equal("alpha.zip", committed.Planned.FileName);
    Assert.Equal("AutoCommitted", committed.StateLabel);

    var failed = Assert.Single(result.Exceptions);
    Assert.Equal("bravo.zip", failed.Planned.FileName);
    Assert.Equal("Failed", failed.StateLabel);
  }

  [Fact]
  public void Project_ParsesFailureFileNamesContainingParentheses()
  {
    var planned = new List<PlannedContentImport>
    {
      new()
      {
        ZipPath = @"C:\import\baseline (1).zip",
        FileName = "baseline (1).zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "stig_import"
      }
    };

    var failures = new[]
    {
      "baseline (1).zip (ConsolidatedZip): Parse error"
    };

    var result = ImportAutoQueueProjection.Project(planned, failures);

    Assert.Empty(result.AutoCommitted);
    var failed = Assert.Single(result.Exceptions);
    Assert.Equal("baseline (1).zip", failed.Planned.FileName);
    Assert.Equal("Failed", failed.StateLabel);
  }

  [Fact]
  public void Project_WhenDuplicateFileNamesExist_UsesRouteToCorrelateFailures()
  {
    var planned = new List<PlannedContentImport>
    {
      new()
      {
        ZipPath = @"C:\import\duplicate.zip",
        FileName = "duplicate.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "stig_import"
      },
      new()
      {
        ZipPath = @"C:\import\duplicate.zip",
        FileName = "duplicate.zip",
        ArtifactKind = ImportArtifactKind.Admx,
        Route = ContentImportRoute.AdmxTemplatesFromZip,
        SourceLabel = "admx_template_import"
      }
    };

    var failures = new[]
    {
      "duplicate.zip (AdmxTemplatesFromZip): Parse error"
    };

    var result = ImportAutoQueueProjection.Project(planned, failures);

    var committed = Assert.Single(result.AutoCommitted);
    Assert.Equal(ContentImportRoute.ConsolidatedZip, committed.Planned.Route);

    var failed = Assert.Single(result.Exceptions);
    Assert.Equal(ContentImportRoute.AdmxTemplatesFromZip, failed.Planned.Route);
  }

  [Fact]
  public void Project_WhenDuplicateFileNamesAndRouteExist_UsesZipPathToCorrelateFailures()
  {
    var planned = new List<PlannedContentImport>
    {
      new()
      {
        ZipPath = @"C:\import\a\duplicate.zip",
        FileName = "duplicate.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "stig_import"
      },
      new()
      {
        ZipPath = @"C:\import\b\duplicate.zip",
        FileName = "duplicate.zip",
        ArtifactKind = ImportArtifactKind.Stig,
        Route = ContentImportRoute.ConsolidatedZip,
        SourceLabel = "stig_import"
      }
    };

    var failures = new[]
    {
      @"C:\import\b\duplicate.zip (ConsolidatedZip): Parse error"
    };

    var result = ImportAutoQueueProjection.Project(planned, failures);

    var committed = Assert.Single(result.AutoCommitted);
    Assert.Equal(@"C:\import\a\duplicate.zip", committed.Planned.ZipPath);

    var failed = Assert.Single(result.Exceptions);
    Assert.Equal(@"C:\import\b\duplicate.zip", failed.Planned.ZipPath);
  }
}
