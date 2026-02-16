using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class ImportProcessedArtifactLedgerTests
{
  [Fact]
  public void MarkProcessed_SameHashAndRoute_OnlyFirstRunProcesses()
  {
    var ledger = new ImportProcessedArtifactLedger();

    var first = ledger.MarkProcessed("ABC123", ContentImportRoute.ConsolidatedZip);
    var second = ledger.MarkProcessed(" abc123 ", ContentImportRoute.ConsolidatedZip);

    Assert.True(first);
    Assert.False(second);
  }

  [Fact]
  public void MarkProcessed_SameHashDifferentRoute_TreatsAsDistinctWorkItems()
  {
    var ledger = new ImportProcessedArtifactLedger();

    var first = ledger.MarkProcessed("ABC123", ContentImportRoute.ConsolidatedZip);
    var second = ledger.MarkProcessed("abc123", ContentImportRoute.AdmxTemplatesFromZip);

    Assert.True(first);
    Assert.True(second);
  }

  [Fact]
  public void MarkProcessed_NullHash_ThrowsArgumentException()
  {
    var ledger = new ImportProcessedArtifactLedger();

    Assert.Throws<ArgumentException>(() => ledger.MarkProcessed(null!, ContentImportRoute.ConsolidatedZip));
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void MarkProcessed_EmptyOrWhitespaceHash_ThrowsArgumentException(string sha256)
  {
    var ledger = new ImportProcessedArtifactLedger();

    Assert.Throws<ArgumentException>(() => ledger.MarkProcessed(sha256, ContentImportRoute.ConsolidatedZip));
  }

  [Fact]
  public void IsProcessed_ReturnsFalseUntilMarkProcessedRuns()
  {
    var ledger = new ImportProcessedArtifactLedger();

    Assert.False(ledger.IsProcessed("ABC123", ContentImportRoute.ConsolidatedZip));

    ledger.MarkProcessed("ABC123", ContentImportRoute.ConsolidatedZip);

    Assert.True(ledger.IsProcessed(" abc123 ", ContentImportRoute.ConsolidatedZip));
  }

  [Fact]
  public void Snapshot_ReturnsCaseInsensitiveSortedKeys()
  {
    var ledger = new ImportProcessedArtifactLedger();
    ledger.MarkProcessed("bbb", ContentImportRoute.ConsolidatedZip);
    ledger.MarkProcessed("AAA", ContentImportRoute.ConsolidatedZip);
    ledger.MarkProcessed("ccc", ContentImportRoute.AdmxTemplatesFromZip);

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
    ledger.MarkProcessed("existing", ContentImportRoute.ConsolidatedZip);

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
