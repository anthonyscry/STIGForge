using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class ImportDedupServiceTests
{
  [Fact]
  public void Resolve_SuppressesExactShaDuplicates()
  {
    var service = new ImportDedupService();
    var a = new ImportInboxCandidate
    {
      ZipPath = "C:/import/a.zip",
      FileName = "a.zip",
      Sha256 = "same",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      VersionTag = "V1R1",
      Confidence = DetectionConfidence.High
    };
    var b = new ImportInboxCandidate
    {
      ZipPath = "C:/import/b.zip",
      FileName = "b.zip",
      Sha256 = "same",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      VersionTag = "V1R1",
      Confidence = DetectionConfidence.High
    };

    var outcome = service.Resolve(new[] { a, b });

    Assert.Single(outcome.Winners);
    Assert.Single(outcome.Suppressed);
  }

  [Fact]
  public void Resolve_PrefersHigherStigVersionForSameContentKey()
  {
    var service = new ImportDedupService();
    var low = new ImportInboxCandidate
    {
      ZipPath = "C:/import/win11_v1r9.zip",
      FileName = "win11_v1r9.zip",
      Sha256 = "sha-1",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      VersionTag = "V1R9",
      Confidence = DetectionConfidence.High
    };
    var high = new ImportInboxCandidate
    {
      ZipPath = "C:/import/win11_v2r1.zip",
      FileName = "win11_v2r1.zip",
      Sha256 = "sha-2",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      VersionTag = "V2R1",
      Confidence = DetectionConfidence.High
    };

    var outcome = service.Resolve(new[] { low, high });

    var winner = Assert.Single(outcome.Winners);
    Assert.Equal("V2R1", winner.VersionTag);
    Assert.Single(outcome.Suppressed);
    Assert.Equal("V1R9", outcome.Suppressed[0].VersionTag);
  }

  [Fact]
  public void Resolve_KeepsDifferentArtifactKindsFromSameArchive()
  {
    var service = new ImportDedupService();
    var scap = new ImportInboxCandidate
    {
      ZipPath = "C:/import/mixed.zip",
      FileName = "mixed.zip",
      Sha256 = "same-archive-sha",
      ArtifactKind = ImportArtifactKind.Scap,
      ContentKey = "scap:benchmark-win11",
      Confidence = DetectionConfidence.High
    };
    var gpo = new ImportInboxCandidate
    {
      ZipPath = "C:/import/mixed.zip",
      FileName = "mixed.zip",
      Sha256 = "same-archive-sha",
      ArtifactKind = ImportArtifactKind.Gpo,
      ContentKey = "gpo:win11-baseline",
      Confidence = DetectionConfidence.High
    };

    var outcome = service.Resolve(new[] { scap, gpo });

    Assert.Equal(2, outcome.Winners.Count);
    Assert.Contains(outcome.Winners, c => c.ArtifactKind == ImportArtifactKind.Scap);
    Assert.Contains(outcome.Winners, c => c.ArtifactKind == ImportArtifactKind.Gpo);
    Assert.Empty(outcome.Suppressed);
  }

  [Fact]
  public void Resolve_UsesDeterministicFilenameTieBreakWhenVersionAndDateMissing()
  {
    var service = new ImportDedupService();
    var zulu = new ImportInboxCandidate
    {
      ZipPath = "C:/import/zulu.zip",
      FileName = "zulu.zip",
      Sha256 = "sha-z",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      Confidence = DetectionConfidence.High,
      VersionTag = string.Empty,
      BenchmarkDate = null
    };
    var alpha = new ImportInboxCandidate
    {
      ZipPath = "C:/import/alpha.zip",
      FileName = "alpha.zip",
      Sha256 = "sha-a",
      ArtifactKind = ImportArtifactKind.Stig,
      ContentKey = "stig:win11",
      Confidence = DetectionConfidence.High,
      VersionTag = string.Empty,
      BenchmarkDate = null
    };

    var outcome = service.Resolve(new[] { zulu, alpha });

    var winner = Assert.Single(outcome.Winners);
    Assert.Equal("alpha.zip", winner.FileName);
    Assert.Single(outcome.Suppressed);
    Assert.Equal("zulu.zip", outcome.Suppressed[0].FileName);
  }

  [Fact]
  public void Resolve_ScapSameVersionDifferentHash_PrefersNiwcEnhancedFromConsolidatedBundle()
  {
    var service = new ImportDedupService();
    var standalone = new ImportInboxCandidate
    {
      ZipPath = "C:/import/scap_standalone.zip",
      FileName = "scap_standalone.zip",
      Sha256 = "sha-standalone",
      ArtifactKind = ImportArtifactKind.Scap,
      ContentKey = "scap:win11-benchmark",
      VersionTag = "V2R1",
      ImportedFrom = ImportProvenance.StandaloneZip,
      IsNiwcEnhanced = false,
      Confidence = DetectionConfidence.High
    };
    var niwc = new ImportInboxCandidate
    {
      ZipPath = "C:/import/niwc_consolidated.zip",
      FileName = "niwc_consolidated.zip",
      Sha256 = "sha-niwc",
      ArtifactKind = ImportArtifactKind.Scap,
      ContentKey = "scap:win11-benchmark",
      VersionTag = "V2R1",
      ImportedFrom = ImportProvenance.ConsolidatedBundle,
      IsNiwcEnhanced = true,
      Confidence = DetectionConfidence.High
    };

    var outcome = service.Resolve(new[] { standalone, niwc });

    var winner = Assert.Single(outcome.Winners);
    Assert.Equal("niwc_consolidated.zip", winner.FileName);
    Assert.Single(outcome.Suppressed);
    Assert.Contains(outcome.Decisions, d => d.Contains("selected NIWC Enhanced", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Resolve_ScapSameVersionDifferentHash_UsesDeterministicFallbackWhenNiwcMissing()
  {
    var service = new ImportDedupService();
    var oldDate = new ImportInboxCandidate
    {
      ZipPath = "C:/import/scap_old.zip",
      FileName = "scap_old.zip",
      Sha256 = "sha-old",
      ArtifactKind = ImportArtifactKind.Scap,
      ContentKey = "scap:win11-benchmark",
      VersionTag = "V2R1",
      ImportedFrom = ImportProvenance.StandaloneZip,
      IsNiwcEnhanced = false,
      BenchmarkDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
      Confidence = DetectionConfidence.High
    };
    var newDate = new ImportInboxCandidate
    {
      ZipPath = "C:/import/scap_new.zip",
      FileName = "scap_new.zip",
      Sha256 = "sha-new",
      ArtifactKind = ImportArtifactKind.Scap,
      ContentKey = "scap:win11-benchmark",
      VersionTag = "V2R1",
      ImportedFrom = ImportProvenance.StandaloneZip,
      IsNiwcEnhanced = false,
      BenchmarkDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
      Confidence = DetectionConfidence.High
    };

    var outcome = service.Resolve(new[] { oldDate, newDate });

    var winner = Assert.Single(outcome.Winners);
    Assert.Equal("scap_new.zip", winner.FileName);
    Assert.Contains(outcome.Decisions, d => d.Contains("deterministic fallback", StringComparison.OrdinalIgnoreCase));
  }
}
