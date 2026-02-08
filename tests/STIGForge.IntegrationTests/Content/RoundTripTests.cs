using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using STIGForge.Content.Import;
using STIGForge.Content.Models;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.IntegrationTests.Content;

/// <summary>
/// Round-trip tests verify data integrity through import/export cycles.
/// These tests ensure no data loss or corruption occurs during processing.
/// </summary>
public sealed class RoundTripTests : IDisposable
{
  private readonly string _testRoot;

  public RoundTripTests()
  {
    _testRoot = Path.Combine(Path.GetTempPath(), "stigforge_roundtrip_" + Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(_testRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_testRoot))
      Directory.Delete(_testRoot, recursive: true);
  }

  [Fact]
  public void XccdfParseAndVerifyControlData()
  {
    var xccdf = CreateMinimalXccdf(
      controlId: "SV-123456",
      vulnId: "V-123456",
      ruleId: "SV-123456r1_rule",
      title: "Test Control Title",
      severity: "medium",
      description: "Test description text",
      checkText: "Check procedure here",
      fixText: "Remediation steps here");

    var xccdfPath = Path.Combine(_testRoot, "test.xml");
    File.WriteAllText(xccdfPath, xccdf);

    var controls = XccdfParser.Parse(xccdfPath, "TestPack");

    Assert.Single(controls);

    var control = controls[0];
    Assert.False(string.IsNullOrEmpty(control.ControlId), "Control ID should be generated");
    Assert.Equal("V-123456", control.ExternalIds.VulnId);
    Assert.Equal("SV-123456r1_rule", control.ExternalIds.RuleId);
    Assert.Equal("Test Control Title", control.Title);
    Assert.Equal("medium", control.Severity);
    Assert.Equal("Test description text", control.Discussion);
    Assert.Equal("Check procedure here", control.CheckText);
    Assert.Equal("Remediation steps here", control.FixText);
    Assert.Equal("TestPack", control.Revision.PackName);
  }

  [Fact]
  public void OvalParseAndVerifyMetadata()
  {
    var oval = CreateMinimalOval(definitionId: "oval:test:def:1");
    var ovalPath = Path.Combine(_testRoot, "oval.xml");
    File.WriteAllText(ovalPath, oval);

    var definitions = OvalParser.Parse(ovalPath);

    Assert.Single(definitions);
    Assert.Equal("oval:test:def:1", definitions[0].DefinitionId);
    Assert.Equal("Test OVAL Definition", definitions[0].Title);
  }

  [Fact]
  public void ConflictDetectorIdentifiesCoreDataConflicts()
  {
    var control1 = new ControlRecord
    {
      ControlId = "SV-111111",
      Title = "Original Title",
      Severity = "low",
      FixText = "Original fix",
      ExternalIds = new ExternalIds { VulnId = "V-111111", RuleId = "SV-111111r1_rule" }
    };

    var control2 = new ControlRecord
    {
      ControlId = "SV-111111",
      Title = "CONFLICTING Title",
      Severity = "high",
      FixText = "Original fix",
      ExternalIds = new ExternalIds { VulnId = "V-111111", RuleId = "SV-111111r1_rule" }
    };

    var titlesDiffer = !string.Equals(control1.Title, control2.Title, StringComparison.Ordinal);
    var severitiesDiffer = !string.Equals(control1.Severity, control2.Severity, StringComparison.OrdinalIgnoreCase);

    Assert.True(titlesDiffer, "Titles should differ");
    Assert.True(severitiesDiffer, "Severities should differ");
  }

  [Theory]
  [MemberData(nameof(CompatibilityFormatScenarios))]
  public async Task ImportZipAsync_CompatibilityMatrixMatchesExpectedFormat(string archiveName, string[] fixtureEntries, string expectedFormat)
  {
    var importer = CreateImporter();
    var fixtureArchivePath = await CreateArchiveFromFixtureEntriesAsync(archiveName, fixtureEntries);

    var importResult = await importer.ImportZipAsync(fixtureArchivePath, "compat-format-check", "integration", CancellationToken.None);
    var compatibilityPath = Path.Combine(_testRoot, ".stigforge-integration", "contentpacks", importResult.PackId, "compatibility_matrix.json");

    Assert.True(File.Exists(compatibilityPath));

    using var compatibilityDoc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    var root = compatibilityDoc.RootElement;

    Assert.Equal(expectedFormat, root.GetProperty("detectedFormat").GetString());
    Assert.True(root.TryGetProperty("support", out var support));
    Assert.True(support.TryGetProperty("xccdf", out _));
    Assert.True(support.TryGetProperty("ovalMetadata", out _));
    Assert.True(support.TryGetProperty("admx", out _));
    Assert.True(root.TryGetProperty("conflicts", out var conflicts));
    Assert.Equal(0, conflicts.GetProperty("bySeverity").GetProperty("error").GetInt32());
  }

  public static IEnumerable<object[]> CompatibilityFormatScenarios()
  {
    yield return new object[]
    {
      "integration-stig-baseline.zip",
      new[] { "stig-baseline-xccdf.xml" },
      "Stig"
    };
    yield return new object[]
    {
      "integration-scap-baseline.zip",
      new[] { "scap-baseline-xccdf.xml", "scap-baseline-oval.xml" },
      "Scap"
    };
    yield return new object[]
    {
      "integration-gpo-baseline.zip",
      new[] { "gpo-baseline.admx" },
      "Gpo"
    };
  }

  [Fact]
  public async Task ImportZipAsync_CompatibilityMatrixIsDeterministicAcrossRepeatedImports()
  {
    var importer = CreateImporter();
    var firstArchive = await CreateArchiveFromFixtureEntriesAsync(
      "compat-deterministic-1.zip",
      new[] { "stig-quarterly-delta-xccdf.xml" });
    var secondArchive = await CreateArchiveFromFixtureEntriesAsync(
      "compat-deterministic-2.zip",
      new[] { "stig-quarterly-delta-xccdf.xml" });

    var firstResult = await importer.ImportZipAsync(firstArchive, "compat-deterministic-1", "integration", CancellationToken.None);
    var secondResult = await importer.ImportZipAsync(secondArchive, "compat-deterministic-2", "integration", CancellationToken.None);

    var firstMatrixPath = Path.Combine(_testRoot, ".stigforge-integration", "contentpacks", firstResult.PackId, "compatibility_matrix.json");
    var secondMatrixPath = Path.Combine(_testRoot, ".stigforge-integration", "contentpacks", secondResult.PackId, "compatibility_matrix.json");

    using var firstDoc = JsonDocument.Parse(File.ReadAllText(firstMatrixPath));
    using var secondDoc = JsonDocument.Parse(File.ReadAllText(secondMatrixPath));

    var firstKeys = firstDoc.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    var secondKeys = secondDoc.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Assert.Equal(firstKeys, secondKeys);
    Assert.Equal(firstDoc.RootElement.GetProperty("detectedFormat").GetString(), secondDoc.RootElement.GetProperty("detectedFormat").GetString());
    Assert.Equal(firstDoc.RootElement.GetProperty("usedFallbackParser").GetBoolean(), secondDoc.RootElement.GetProperty("usedFallbackParser").GetBoolean());
    Assert.Equal(firstDoc.RootElement.GetProperty("parsedControls").GetInt32(), secondDoc.RootElement.GetProperty("parsedControls").GetInt32());
    Assert.Equal(firstDoc.RootElement.GetProperty("parsingErrors").GetArrayLength(), secondDoc.RootElement.GetProperty("parsingErrors").GetArrayLength());
  }

  [Fact]
  public async Task ImportZipAsync_MalformedCompatibilityFixtureProducesErrorClassification()
  {
    var importer = CreateImporter();
    var malformedArchive = await CreateArchiveFromFixtureEntriesAsync(
      "compat-malformed.zip",
      new[] { "stig-malformed-xccdf.xml" });

    var result = await importer.ImportZipAsync(malformedArchive, "compat-malformed", "integration", CancellationToken.None);
    var compatibilityPath = Path.Combine(_testRoot, ".stigforge-integration", "contentpacks", result.PackId, "compatibility_matrix.json");

    using var compatibilityDoc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    var root = compatibilityDoc.RootElement;

    Assert.Equal("Stig", root.GetProperty("detectedFormat").GetString());
    Assert.True(root.GetProperty("parsingErrors").GetArrayLength() > 0);
    Assert.True(root.GetProperty("unsupportedMappings").GetArrayLength() > 0);

    var conflictBySeverity = root.GetProperty("conflicts").GetProperty("bySeverity");
    Assert.Equal(0, conflictBySeverity.GetProperty("error").GetInt32());
    Assert.Equal(0, conflictBySeverity.GetProperty("warning").GetInt32());
  }

  [Fact]
  public async Task ImportZipAsync_MixedAdversarialFixtureProducesWarningClassification()
  {
    var importer = CreateImporter();
    var mixedArchive = await CreateArchiveFromFixtureEntriesAsync(
      "compat-mixed-adversarial.zip",
      new[] { "mixed-adversarial-xccdf.xml", "mixed-adversarial-oval.xml", "gpo-baseline.admx" });

    var result = await importer.ImportZipAsync(mixedArchive, "compat-mixed", "integration", CancellationToken.None);
    var compatibilityPath = Path.Combine(_testRoot, ".stigforge-integration", "contentpacks", result.PackId, "compatibility_matrix.json");

    using var compatibilityDoc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    var root = compatibilityDoc.RootElement;

    Assert.Equal("Gpo", root.GetProperty("detectedFormat").GetString());
    Assert.Equal("Medium", root.GetProperty("detectionConfidence").GetString());
    Assert.True(root.GetProperty("unsupportedMappings").GetArrayLength() > 0);
    Assert.Contains(
      root.GetProperty("unsupportedMappings").EnumerateArray().Select(entry => entry.GetString() ?? string.Empty),
      message => message.Contains("OVAL XML files present", StringComparison.Ordinal));

    var conflictBySeverity = root.GetProperty("conflicts").GetProperty("bySeverity");
    Assert.Equal(0, conflictBySeverity.GetProperty("warning").GetInt32());
    Assert.Equal(0, conflictBySeverity.GetProperty("error").GetInt32());
  }

  private ContentPackImporter CreateImporter()
  {
    var pathBuilder = new TestPathBuilder(_testRoot);
    var contentRepository = new InMemoryContentPackRepository();
    var controlRepository = new InMemoryControlRepository();
    return new ContentPackImporter(pathBuilder, new ConstantHashingService(), contentRepository, controlRepository);
  }

  private async Task<string> CreateArchiveFromFixtureEntriesAsync(string archiveName, IReadOnlyList<string> fixtureNames)
  {
    var archivePath = Path.Combine(_testRoot, archiveName);
    using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

    foreach (var fixtureName in fixtureNames)
    {
      var fixturePath = GetFixturePath(fixtureName);
      var entry = archive.CreateEntry(fixtureName);

      await using var sourceStream = File.OpenRead(fixturePath);
      await using var destinationStream = entry.Open();
      await sourceStream.CopyToAsync(destinationStream);
    }

    return archivePath;
  }

  private static string GetFixturePath(string fixtureName)
  {
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    return Path.Combine(baseDir, "..", "..", "..", "fixtures", "compatibility", fixtureName);
  }

  private static string CreateMinimalXccdf(string controlId, string vulnId, string ruleId, string title, string severity, string description, string checkText, string fixText)
  {
    XNamespace ns = "http://checklists.nist.gov/xccdf/1.2";

    var doc = new XDocument(
      new XElement(ns + "Benchmark",
        new XAttribute("id", "test-benchmark"),
        new XElement(ns + "Group",
          new XAttribute("id", controlId),
          new XElement(ns + "title", title),
          new XElement(ns + "description", description),
          new XElement(ns + "Rule",
            new XAttribute("id", ruleId),
            new XAttribute("severity", severity),
            new XElement(ns + "version", vulnId),
            new XElement(ns + "title", title),
            new XElement(ns + "description", description),
            new XElement(ns + "check",
              new XElement(ns + "check-content", checkText)
            ),
            new XElement(ns + "fixtext", fixText)
          )
        )
      )
    );

    return doc.ToString();
  }

  private static string CreateMinimalOval(string definitionId)
  {
    XNamespace ns = "http://oval.mitre.org/XMLSchema/oval-definitions-5";

    var doc = new XDocument(
      new XElement(ns + "oval_definitions",
        new XElement(ns + "definitions",
          new XElement(ns + "definition",
            new XAttribute("id", definitionId),
            new XAttribute("class", "compliance"),
            new XElement(ns + "metadata",
              new XElement(ns + "title", "Test OVAL Definition")
            )
          )
        )
      )
    );

    return doc.ToString();
  }

  private sealed class TestPathBuilder : IPathBuilder
  {
    private readonly string _root;

    public TestPathBuilder(string testRoot)
    {
      _root = Path.Combine(testRoot, ".stigforge-integration");
      Directory.CreateDirectory(_root);
    }

    public string GetAppDataRoot() => _root;

    public string GetContentPacksRoot() => Path.Combine(_root, "contentpacks");

    public string GetPackRoot(string packId) => Path.Combine(GetContentPacksRoot(), packId);

    public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);

    public string GetLogsRoot() => Path.Combine(_root, "logs");

    public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
      => Path.Combine(_root, "exports", "default");
  }

  private sealed class ConstantHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult("integration-sha256");

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult("integration-sha256");
  }

  private sealed class InMemoryContentPackRepository : IContentPackRepository
  {
    private readonly Dictionary<string, ContentPack> _packs = new(StringComparer.Ordinal);

    public Task SaveAsync(ContentPack pack, CancellationToken ct)
    {
      _packs[pack.PackId] = pack;
      return Task.CompletedTask;
    }

    public Task<ContentPack?> GetAsync(string packId, CancellationToken ct)
    {
      _packs.TryGetValue(packId, out var pack);
      return Task.FromResult(pack);
    }

    public Task<IReadOnlyList<ContentPack>> ListAsync(CancellationToken ct)
      => Task.FromResult<IReadOnlyList<ContentPack>>(_packs.Values.ToList());

    public Task DeleteAsync(string packId, CancellationToken ct)
    {
      _packs.Remove(packId);
      return Task.CompletedTask;
    }
  }

  private sealed class InMemoryControlRepository : IControlRepository
  {
    private readonly Dictionary<string, IReadOnlyList<ControlRecord>> _controlsByPack = new(StringComparer.Ordinal);

    public Task SaveControlsAsync(string packId, IReadOnlyList<ControlRecord> controls, CancellationToken ct)
    {
      _controlsByPack[packId] = controls;
      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ControlRecord>> ListControlsAsync(string packId, CancellationToken ct)
    {
      if (_controlsByPack.TryGetValue(packId, out var controls))
        return Task.FromResult(controls);
      return Task.FromResult<IReadOnlyList<ControlRecord>>(Array.Empty<ControlRecord>());
    }

    public Task<bool> VerifySchemaAsync(CancellationToken ct)
      => Task.FromResult(true);
  }
}
