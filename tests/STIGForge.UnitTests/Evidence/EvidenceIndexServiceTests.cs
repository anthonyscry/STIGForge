using System.Text.Json;
using FluentAssertions;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Evidence;

public class EvidenceIndexServiceTests : IDisposable
{
  private readonly string _bundleRoot;

  public EvidenceIndexServiceTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-eidx-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_bundleRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  private void CreateEvidenceEntry(string controlKey, string evidenceId, string type,
    string? runId = null, string? supersedesId = null, Dictionary<string, string>? tags = null)
  {
    var controlDir = Path.Combine(_bundleRoot, "Evidence", "by_control", controlKey);
    Directory.CreateDirectory(controlDir);

    // Write evidence file
    var evidencePath = Path.Combine(controlDir, evidenceId + ".txt");
    File.WriteAllText(evidencePath, $"Evidence content for {controlKey}");

    // Write metadata JSON
    var metadata = new EvidenceMetadata
    {
      ControlId = controlKey,
      RuleId = "RULE:" + controlKey,
      Title = "Evidence for " + controlKey,
      Type = type,
      Source = "TestSource",
      TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
      Host = "TestHost",
      User = "TestUser",
      BundleRoot = _bundleRoot,
      Sha256 = "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890",
      RunId = runId,
      StepName = runId != null ? "test_step" : null,
      SupersedesEvidenceId = supersedesId,
      Tags = tags
    };

    var metaPath = Path.Combine(controlDir, evidenceId + ".json");
    var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(metaPath, json);
  }

  [Fact]
  public async Task BuildIndex_ScansControlDirectories()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_20260101_001", "Command");
    CreateEvidenceEntry("SV-002r1", "evidence_20260101_002", "Registry");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();

    index.TotalEntries.Should().Be(2);
    index.Entries.Should().HaveCount(2);
    index.Entries.Should().Contain(e => e.ControlKey == "SV-001r1");
    index.Entries.Should().Contain(e => e.ControlKey == "SV-002r1");
    index.IndexedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task QueryByControl_ReturnsMatches()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_001a", "Command");
    CreateEvidenceEntry("SV-001r1", "evidence_001b", "Registry");
    CreateEvidenceEntry("SV-002r1", "evidence_002a", "File");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();

    var results = EvidenceIndexService.GetEvidenceForControl(index, "SV-001r1");
    results.Should().HaveCount(2);
    results.Should().OnlyContain(e => e.ControlKey == "SV-001r1");
  }

  [Fact]
  public async Task QueryByType_FiltersCorrectly()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_001", "Command");
    CreateEvidenceEntry("SV-002r1", "evidence_002", "Registry");
    CreateEvidenceEntry("SV-003r1", "evidence_003", "Command");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();

    var results = EvidenceIndexService.GetEvidenceByType(index, "Command");
    results.Should().HaveCount(2);
    results.Should().OnlyContain(e => e.Type == "Command");
  }

  [Fact]
  public async Task Lineage_FollowsSupersedesChain()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_A", "Command");
    CreateEvidenceEntry("SV-001r1", "evidence_B", "Command", supersedesId: "evidence_A");
    CreateEvidenceEntry("SV-001r1", "evidence_C", "Command", supersedesId: "evidence_B");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();

    var chain = EvidenceIndexService.GetLineageChain(index, "evidence_C");
    chain.Should().HaveCount(3);
    chain[0].EvidenceId.Should().Be("evidence_C");
    chain[1].EvidenceId.Should().Be("evidence_B");
    chain[2].EvidenceId.Should().Be("evidence_A");
  }

  [Fact]
  public async Task WriteIndex_CreatesJsonFile()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_001", "Command");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();
    await svc.WriteIndexAsync(index);

    var indexPath = Path.Combine(_bundleRoot, "Evidence", "evidence_index.json");
    File.Exists(indexPath).Should().BeTrue();

    var json = await File.ReadAllTextAsync(indexPath);
    var loaded = JsonSerializer.Deserialize<EvidenceIndex>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    loaded.Should().NotBeNull();
    loaded!.TotalEntries.Should().Be(1);
    loaded.Entries.Should().HaveCount(1);
  }

  [Fact]
  public async Task QueryByRun_FiltersCorrectly()
  {
    CreateEvidenceEntry("SV-001r1", "evidence_001", "Command", runId: "run-001");
    CreateEvidenceEntry("SV-002r1", "evidence_002", "Registry", runId: "run-002");
    CreateEvidenceEntry("SV-003r1", "evidence_003", "Command", runId: "run-001");

    var svc = new EvidenceIndexService(_bundleRoot);
    var index = await svc.BuildIndexAsync();

    var results = EvidenceIndexService.GetEvidenceByRun(index, "run-001");
    results.Should().HaveCount(2);
    results.Should().OnlyContain(e => e.RunId == "run-001");
  }
}
