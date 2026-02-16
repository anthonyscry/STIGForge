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

  [Fact]
  public void TryBegin_NullHash_ThrowsArgumentException()
  {
    var ledger = new ImportProcessedArtifactLedger();

    Assert.Throws<ArgumentException>(() => ledger.TryBegin(null!, ContentImportRoute.ConsolidatedZip));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void TryBegin_EmptyOrWhitespaceHash_ThrowsArgumentException(string sha256)
  {
    var ledger = new ImportProcessedArtifactLedger();

    Assert.Throws<ArgumentException>(() => ledger.TryBegin(sha256, ContentImportRoute.ConsolidatedZip));
  }
}
