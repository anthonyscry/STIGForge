using System.Text.Json;
using FluentAssertions;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Evidence;

public class EvidenceCollectorTests : IDisposable
{
  private readonly string _bundleRoot;

  public EvidenceCollectorTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-ev-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_bundleRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  [Fact]
  public void WriteEvidence_TextContent_CreatesFileAndMetadata()
  {
    var collector = new EvidenceCollector();
    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-12345",
      Title = "Test Control",
      Type = EvidenceArtifactType.Command,
      Source = "CLI",
      ContentText = "Get-Service | Where-Object { $_.Status -eq 'Running' }"
    });

    File.Exists(result.EvidencePath).Should().BeTrue();
    File.Exists(result.MetadataPath).Should().BeTrue();
    result.Sha256.Should().NotBeNullOrWhiteSpace();

    var content = File.ReadAllText(result.EvidencePath);
    content.Should().Contain("Get-Service");
  }

  [Fact]
  public void WriteEvidence_FileSource_CopiesFile()
  {
    var sourceFile = Path.Combine(_bundleRoot, "source.txt");
    File.WriteAllText(sourceFile, "evidence content");

    var collector = new EvidenceCollector();
    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-99999",
      Type = EvidenceArtifactType.File,
      SourceFilePath = sourceFile
    });

    File.Exists(result.EvidencePath).Should().BeTrue();
    File.ReadAllText(result.EvidencePath).Should().Be("evidence content");
  }

  [Fact]
  public void WriteEvidence_Metadata_ContainsExpectedFields()
  {
    var collector = new EvidenceCollector();
    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-11111",
      ControlId = "C-11111",
      Title = "Metadata Test",
      Type = EvidenceArtifactType.Registry,
      Source = "CLI",
      Command = "reg query HKLM\\...",
      ContentText = "test data"
    });

    var metaJson = File.ReadAllText(result.MetadataPath);
    var meta = JsonSerializer.Deserialize<EvidenceMetadata>(metaJson);

    meta.Should().NotBeNull();
    meta!.RuleId.Should().Be("SV-11111");
    meta.ControlId.Should().Be("C-11111");
    meta.Type.Should().Be("Registry");
    meta.Sha256.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void WriteEvidence_MissingBundleRoot_Throws()
  {
    var collector = new EvidenceCollector();

    var act = () => collector.WriteEvidence(new EvidenceWriteRequest { BundleRoot = "" });

    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void WriteEvidence_NonExistentBundleRoot_Throws()
  {
    var collector = new EvidenceCollector();

    var act = () => collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = Path.Combine(_bundleRoot, "does-not-exist"),
      RuleId = "SV-1",
      ContentText = "test"
    });

    act.Should().Throw<DirectoryNotFoundException>();
  }
}
