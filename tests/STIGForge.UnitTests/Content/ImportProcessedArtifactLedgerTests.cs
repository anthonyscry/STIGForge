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

  [Fact]
  public void Snapshot_ReturnsCaseInsensitiveSortedKeys()
  {
    var ledger = new ImportProcessedArtifactLedger();
    ledger.TryBegin("bbb", ContentImportRoute.ConsolidatedZip);
    ledger.TryBegin("AAA", ContentImportRoute.ConsolidatedZip);
    ledger.TryBegin("ccc", ContentImportRoute.AdmxTemplatesFromZip);

    var snapshot = ledger.Snapshot();

    Assert.Equal(
      new[]
      {
        "AdmxTemplatesFromZip:ccc",
        "ConsolidatedZip:AAA",
        "ConsolidatedZip:bbb"
      },
      snapshot);
  }

  [Fact]
  public void Load_ClearsExistingAndNormalizesKeys()
  {
    var ledger = new ImportProcessedArtifactLedger();
    ledger.TryBegin("existing", ContentImportRoute.ConsolidatedZip);

    ledger.Load(new[]
    {
      " ConsolidatedZip:ABC123 ",
      "consolidatedzip:abc123",
      "AdmxTemplatesFromZip:DEF456",
      " ",
      null!
    });

    var snapshot = ledger.Snapshot();
    Assert.Equal(2, snapshot.Count);
    Assert.Contains("ConsolidatedZip:ABC123", snapshot, StringComparer.OrdinalIgnoreCase);
    Assert.Contains("AdmxTemplatesFromZip:DEF456", snapshot, StringComparer.OrdinalIgnoreCase);
    Assert.DoesNotContain("ConsolidatedZip:existing", snapshot, StringComparer.OrdinalIgnoreCase);
  }

  [Fact]
  public void Load_IgnoresMalformedKeys()
  {
    var ledger = new ImportProcessedArtifactLedger();

    ledger.Load(new[]
    {
      "ConsolidatedZip:ABC123",
      "ConsolidatedZip",
      "ConsolidatedZip:",
      ":ABC123",
      "UnknownRoute:ABC123",
      "AdmxTemplatesFromZip:DEF456:extra",
      "AdmxTemplatesFromZip:DEF456"
    });

    var snapshot = ledger.Snapshot();
    Assert.Equal(2, snapshot.Count);
    Assert.Contains("ConsolidatedZip:ABC123", snapshot);
    Assert.Contains("AdmxTemplatesFromZip:DEF456", snapshot);
  }
}
