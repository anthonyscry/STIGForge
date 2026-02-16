using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class ImportProcessedArtifactLedgerTests
{
  [Fact]
  public void TryBegin_SameHashAndRoute_OnlyFirstRunProcesses()
  {
    var ledger = new ImportProcessedArtifactLedger();

    var first = ledger.TryBegin("ABC123", ContentImportRoute.ConsolidatedZip);
    var second = ledger.TryBegin(" abc123 ", ContentImportRoute.ConsolidatedZip);

    Assert.True(first);
    Assert.False(second);
  }

  [Fact]
  public void TryBegin_SameHashDifferentRoute_TreatsAsDistinctWorkItems()
  {
    var ledger = new ImportProcessedArtifactLedger();

    var first = ledger.TryBegin("ABC123", ContentImportRoute.ConsolidatedZip);
    var second = ledger.TryBegin("abc123", ContentImportRoute.AdmxTemplatesFromZip);

    Assert.True(first);
    Assert.True(second);
  }
}
