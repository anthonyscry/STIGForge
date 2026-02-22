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

  // --- New tests for run-scoped provenance fields ---

  [Fact]
  public void WriteEvidence_WithRunId_PersistedInMetadata()
  {
    var collector = new EvidenceCollector();
    var runId = Guid.NewGuid().ToString();

    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-22222",
      Type = EvidenceArtifactType.File,
      Source = "ApplyRunner",
      ContentText = "apply step output",
      RunId = runId,
      StepName = "apply_script"
    });

    var meta = JsonSerializer.Deserialize<EvidenceMetadata>(File.ReadAllText(result.MetadataPath));
    meta.Should().NotBeNull();
    meta!.RunId.Should().Be(runId, "RunId must be persisted in evidence metadata");
    meta.StepName.Should().Be("apply_script", "StepName must be persisted in evidence metadata");
  }

  [Fact]
  public void WriteEvidence_WithSupersedesEvidenceId_PersistedInMetadata()
  {
    var collector = new EvidenceCollector();
    var runId = Guid.NewGuid().ToString();
    var priorEvidenceId = "evidence_prior_run_step";

    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-33333",
      Type = EvidenceArtifactType.File,
      Source = "ApplyRunner",
      ContentText = "new artifact",
      RunId = runId,
      StepName = "powerstig_compile",
      SupersedesEvidenceId = priorEvidenceId
    });

    var meta = JsonSerializer.Deserialize<EvidenceMetadata>(File.ReadAllText(result.MetadataPath));
    meta.Should().NotBeNull();
    meta!.SupersedesEvidenceId.Should().Be(priorEvidenceId);
  }

  [Fact]
  public void WriteEvidence_ReturnsEvidenceIdInResult()
  {
    var collector = new EvidenceCollector();

    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-44444",
      Type = EvidenceArtifactType.File,
      ContentText = "data"
    });

    result.EvidenceId.Should().NotBeNullOrWhiteSpace("EvidenceId must be set for downstream lineage references");
  }

  [Fact]
  public void WriteEvidence_WithRunIdAndStepName_MetadataContainsSha256()
  {
    var collector = new EvidenceCollector();
    var runId = Guid.NewGuid().ToString();

    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-55555",
      Type = EvidenceArtifactType.File,
      Source = "ApplyRunner",
      ContentText = "stdout log content here",
      RunId = runId,
      StepName = "apply_dsc"
    });

    result.Sha256.Should().NotBeNullOrWhiteSpace("SHA-256 must be computed for all evidence artifacts");
    var meta = JsonSerializer.Deserialize<EvidenceMetadata>(File.ReadAllText(result.MetadataPath));
    meta!.Sha256.Should().Be(result.Sha256, "Metadata Sha256 must match the computed file hash");
  }

  [Fact]
  public void WriteEvidence_NullRunIdAndStepName_MetadataFieldsAreNull()
  {
    var collector = new EvidenceCollector();

    var result = collector.WriteEvidence(new EvidenceWriteRequest
    {
      BundleRoot = _bundleRoot,
      RuleId = "SV-66666",
      Type = EvidenceArtifactType.Command,
      ContentText = "manual evidence"
      // No RunId or StepName set
    });

    var meta = JsonSerializer.Deserialize<EvidenceMetadata>(File.ReadAllText(result.MetadataPath));
    meta.Should().NotBeNull();
    meta!.RunId.Should().BeNull("RunId must be null for non-apply evidence");
    meta.StepName.Should().BeNull("StepName must be null for non-apply evidence");
    meta.SupersedesEvidenceId.Should().BeNull();
  }
}
