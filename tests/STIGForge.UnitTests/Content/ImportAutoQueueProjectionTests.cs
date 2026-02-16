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
}
