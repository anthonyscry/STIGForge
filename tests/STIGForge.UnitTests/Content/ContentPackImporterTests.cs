using System.IO.Compression;
using System.Text.Json;
using Moq;
using STIGForge.Content.Import;
using STIGForge.Content.Models;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class ContentPackImporterTests : IDisposable
{
  private readonly string _tempRoot;

  public ContentPackImporterTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-importer-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task ImportZipAsync_RejectsPathTraversalEntries()
  {
    var zipPath = Path.Combine(_tempRoot, "unsafe-traversal.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var traversal = archive.CreateEntry("../outside.txt");
      await using (var writer = new StreamWriter(traversal.Open()))
      {
        await writer.WriteAsync("blocked");
      }

      var valid = archive.CreateEntry("valid-xccdf.xml");
      await using (var writer = new StreamWriter(valid.Open()))
      {
        await writer.WriteAsync(CreateMinimalXccdf());
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var act = () => importer.ImportZipAsync(zipPath, "unsafe", "unit-test", CancellationToken.None);

    var ex = await Assert.ThrowsAsync<ParsingException>(act);
    Assert.Contains("IMPORT-ARCHIVE-002", ex.Message, StringComparison.Ordinal);

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Never);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ControlRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ImportZipAsync_RejectsArchiveOverEntryCountLimit()
  {
    var zipPath = Path.Combine(_tempRoot, "too-many-entries.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      for (var i = 0; i < 4097; i++)
      {
        var entry = archive.CreateEntry($"entries/file-{i:D4}.txt");
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync("x");
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var act = () => importer.ImportZipAsync(zipPath, "too-many", "unit-test", CancellationToken.None);

    var ex = await Assert.ThrowsAsync<ParsingException>(act);
    Assert.Contains("IMPORT-ARCHIVE-001", ex.Message, StringComparison.Ordinal);

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Never);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<ControlRecord>>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task ImportZipAsync_ImportsValidXccdfBundle()
  {
    var zipPath = Path.Combine(_tempRoot, "valid-bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var xccdf = archive.CreateEntry("bundle-xccdf.xml");
      await using var writer = new StreamWriter(xccdf.Open());
      await writer.WriteAsync(CreateMinimalXccdf());
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var result = await importer.ImportZipAsync(zipPath, "valid-pack", "unit-test", CancellationToken.None);

    Assert.Equal("valid-pack", result.Name);
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Once);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ImportZipAsync_ParsesScapDataStreamBenchmarkXml()
  {
    var zipPath = Path.Combine(_tempRoot, "scap-datastream-benchmark.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var benchmark = archive.CreateEntry("U_MS_Windows_11_V2R7_STIG_SCAP_1-3_Benchmark.xml");
      await using var writer = new StreamWriter(benchmark.Open());
      await writer.WriteAsync(CreateMinimalScapDataStream("xccdf_org.test.win11_benchmark", "V2R7"));
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var result = await importer.ImportZipAsync(zipPath, "win11_scap", "scap_import", CancellationToken.None);

    Assert.Equal("win11_scap", result.Name);
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Once);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ImportZipAsync_ClassifiesScapDataStreamAsScapFormat()
  {
    var zipPath = Path.Combine(_tempRoot, "scap-datastream-format-detection.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var benchmark = archive.CreateEntry("U_MS_Windows_11_V2R7_STIG_SCAP_1-3_Benchmark.xml");
      await using var writer = new StreamWriter(benchmark.Open());
      await writer.WriteAsync(CreateMinimalScapDataStream("xccdf_org.test.win11_benchmark", "V2R7"));
    }

    var importer = CreateImporter(out _, out _);
    var result = await importer.ImportZipAsync(zipPath, "win11_scap", "scap_import", CancellationToken.None);

    var compatibilityPath = Path.Combine(_tempRoot, "packs", result.PackId, "compatibility_matrix.json");
    using var matrixDoc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    var root = matrixDoc.RootElement;

    Assert.Equal("Scap", root.GetProperty("detectedFormat").GetString());
    Assert.True(root.GetProperty("support").GetProperty("ovalMetadata").GetBoolean());
  }

  [Fact]
  public async Task ImportConsolidatedZipAsync_SplitsFlatScapDataStreamsIntoIndividualPacks()
  {
    var zipPath = Path.Combine(_tempRoot, "flat-scap-consolidated.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var benchmarkA = archive.CreateEntry("U_Product_A_V1R1_STIG_SCAP_1-3_Benchmark.xml");
      await using (var writer = new StreamWriter(benchmarkA.Open()))
      {
        await writer.WriteAsync(CreateMinimalScapDataStream("xccdf_org.test.product_a", "V1R1"));
      }

      var benchmarkB = archive.CreateEntry("U_Product_B_V1R1_STIG_SCAP_1-3_Benchmark.xml");
      await using (var writer = new StreamWriter(benchmarkB.Open()))
      {
        await writer.WriteAsync(CreateMinimalScapDataStream("xccdf_org.test.product_b", "V1R1"));
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var imported = await importer.ImportConsolidatedZipAsync(zipPath, "scap_import", CancellationToken.None);

    Assert.Equal(2, imported.Count);
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Exactly(2));
  }

  [Fact]
  public async Task ImportAdmxTemplatesFromZipAsync_ImportsOnePackPerTemplate()
  {
    var zipPath = Path.Combine(_tempRoot, "admx-templates.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var windowsAdmx = archive.CreateEntry("PolicyDefinitions/windows.admx");
      await using (var writer = new StreamWriter(windowsAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("WindowsPolicy"));
      }

      var officeAdmx = archive.CreateEntry("PolicyDefinitions/office.admx");
      await using (var writer = new StreamWriter(officeAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("OfficePolicy"));
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var imported = await importer.ImportAdmxTemplatesFromZipAsync(zipPath, "admx_template_import", CancellationToken.None);

    Assert.Single(imported);
    Assert.All(imported, pack => Assert.Equal("admx templates", pack.Name, ignoreCase: true));
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Once);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ImportAdmxTemplatesFromZipAsync_ImportsTemplatesFromNestedZip()
  {
    var nestedZipPath = Path.Combine(_tempRoot, "nested-admx.zip");
    using (var nested = ZipFile.Open(nestedZipPath, ZipArchiveMode.Create))
    {
      var edgeAdmx = nested.CreateEntry("PolicyDefinitions/msedge.admx");
      await using (var writer = new StreamWriter(edgeAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("EdgePolicy"));
      }

      var windowsAdmx = nested.CreateEntry("PolicyDefinitions/windows.admx");
      await using (var writer = new StreamWriter(windowsAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("WindowsPolicy"));
      }
    }

    var outerZipPath = Path.Combine(_tempRoot, "outer-admx-bundle.zip");
    using (var outer = ZipFile.Open(outerZipPath, ZipArchiveMode.Create))
    {
      outer.CreateEntryFromFile(nestedZipPath, "payload/admx-templates.zip");
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var imported = await importer.ImportAdmxTemplatesFromZipAsync(outerZipPath, "admx_template_import", CancellationToken.None);

    Assert.Single(imported);
    Assert.All(imported, pack => Assert.Equal("admx templates", pack.Name, ignoreCase: true));
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Once);
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task ImportAdmxTemplatesFromZipAsync_GroupsByFolderUnderAdmxTemplates()
  {
    var zipPath = Path.Combine(_tempRoot, "admx-templates-scoped.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var windowsAdmx = archive.CreateEntry("ADMX Templates/Microsoft/windows.admx");
      await using (var writer = new StreamWriter(windowsAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("WindowsPolicy"));
      }

      var edgeAdmx = archive.CreateEntry("ADMX Templates/Microsoft/edge.admx");
      await using (var writer = new StreamWriter(edgeAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("EdgePolicy"));
      }

      var adobeAdmx = archive.CreateEntry("ADMX Templates/Adobe/reader.admx");
      await using (var writer = new StreamWriter(adobeAdmx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("AdobePolicy"));
      }
    }

    var importer = CreateImporter(out var packsMock, out var controlsMock);
    var imported = await importer.ImportAdmxTemplatesFromZipAsync(zipPath, "admx_template_import", CancellationToken.None);

    Assert.Equal(2, imported.Count);
    Assert.Contains(imported, pack => string.Equals(pack.Name, "ADMX Templates - Adobe", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(imported, pack => string.Equals(pack.Name, "ADMX Templates - Microsoft", StringComparison.OrdinalIgnoreCase));

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.Exactly(2));
  }

  [Theory]
  [MemberData(nameof(CompatibilityMatrixScenarios))]
  public async Task ImportZipAsync_CompatibilityMatrixContractMatchesFixtureScenario(
    string scenario,
    string[] fixtureFiles,
    string expectedFormat,
    bool expectedFallback,
    int minimumParsingErrors,
    bool expectedOvalMetadata,
    bool expectedAdmx,
    int minimumWarnings,
    int expectedConflictErrors)
  {
    var zipPath = await CreateZipFromFixturesAsync($"{scenario}.zip", fixtureFiles);

    var importer = CreateImporter(out _, out _);
    var result = await importer.ImportZipAsync(zipPath, scenario, "compat-fixture", CancellationToken.None);

    var compatibilityPath = Path.Combine(_tempRoot, "packs", result.PackId, "compatibility_matrix.json");
    Assert.True(File.Exists(compatibilityPath), "compatibility_matrix.json should be emitted for every imported fixture scenario");

    using var matrixDoc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
    var root = matrixDoc.RootElement;

    AssertCompatibilityMatrixHasRequiredKeys(root);
    Assert.Equal(expectedFormat, root.GetProperty("detectedFormat").GetString());
    Assert.Equal(expectedFallback, root.GetProperty("usedFallbackParser").GetBoolean());
    Assert.Equal(expectedOvalMetadata, root.GetProperty("support").GetProperty("ovalMetadata").GetBoolean());
    Assert.Equal(expectedAdmx, root.GetProperty("support").GetProperty("admx").GetBoolean());

    var parsingErrors = root.GetProperty("parsingErrors");
    Assert.True(parsingErrors.GetArrayLength() >= minimumParsingErrors, $"Scenario '{scenario}' expected at least {minimumParsingErrors} parsing errors");

    var warnings = root.GetProperty("unsupportedMappings").GetArrayLength() + root.GetProperty("conflicts").GetProperty("bySeverity").GetProperty("warning").GetInt32();
    Assert.True(warnings >= minimumWarnings, $"Scenario '{scenario}' expected at least {minimumWarnings} warning classifications");

    var conflictErrors = root.GetProperty("conflicts").GetProperty("bySeverity").GetProperty("error").GetInt32();
    Assert.Equal(expectedConflictErrors, conflictErrors);
  }

  public static IEnumerable<object[]> CompatibilityMatrixScenarios()
  {
    yield return new object[]
    {
      "stig-baseline",
      new[] { "compat-stig-baseline-xccdf.xml" },
      "Stig",
      false,
      0,
      false,
      false,
      0,
      0
    };
    yield return new object[]
    {
      "stig-quarterly-delta",
      new[] { "compat-stig-quarterly-delta-xccdf.xml" },
      "Stig",
      false,
      0,
      false,
      false,
      0,
      0
    };
    yield return new object[]
    {
      "stig-malformed",
      new[] { "compat-stig-malformed-xccdf.xml" },
      "Stig",
      false,
      1,
      false,
      false,
      1,
      0
    };
    yield return new object[]
    {
      "scap-baseline",
      new[] { "compat-scap-baseline-xccdf.xml", "compat-scap-baseline-oval.xml" },
      "Scap",
      false,
      0,
      true,
      false,
      0,
      0
    };
    yield return new object[]
    {
      "scap-malformed-oval",
      new[] { "compat-scap-baseline-xccdf.xml", "compat-scap-malformed-oval.xml" },
      "Scap",
      false,
      1,
      true,
      false,
      0,
      0
    };
    yield return new object[]
    {
      "gpo-baseline",
      new[] { "compat-gpo-baseline.admx" },
      "Gpo",
      false,
      0,
      false,
      true,
      0,
      0
    };
    yield return new object[]
    {
      "gpo-quarterly-delta",
      new[] { "compat-gpo-quarterly-delta.admx" },
      "Gpo",
      false,
      0,
      false,
      true,
      0,
      0
    };
    yield return new object[]
    {
      "unknown-oval-only",
      new[] { "compat-unknown-oval-only.xml" },
      "Unknown",
      true,
      0,
      false,
      false,
      1,
      0
    };
  }

  [Fact]
  public async Task ImportZipAsync_CompatibilityMatrixPropertyOrderIsDeterministic()
  {
    var fixtureFiles = new[] { "compat-stig-quarterly-delta-xccdf.xml" };
    var firstZip = await CreateZipFromFixturesAsync("deterministic-first.zip", fixtureFiles);
    var secondZip = await CreateZipFromFixturesAsync("deterministic-second.zip", fixtureFiles);

    var importer = CreateImporter(out _, out _);
    var firstImport = await importer.ImportZipAsync(firstZip, "deterministic-1", "compat-fixture", CancellationToken.None);
    var secondImport = await importer.ImportZipAsync(secondZip, "deterministic-2", "compat-fixture", CancellationToken.None);

    var firstPath = Path.Combine(_tempRoot, "packs", firstImport.PackId, "compatibility_matrix.json");
    var secondPath = Path.Combine(_tempRoot, "packs", secondImport.PackId, "compatibility_matrix.json");

    using var firstDoc = JsonDocument.Parse(File.ReadAllText(firstPath));
    using var secondDoc = JsonDocument.Parse(File.ReadAllText(secondPath));

    var firstKeys = firstDoc.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    var secondKeys = secondDoc.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Assert.Equal(firstKeys, secondKeys);
  }

  [Fact]
  public async Task ImportZipRoutes_ForNoLeadingDotGpoSupportFiles_CompletesBothRoutesWithoutHang()
  {
    var zipPath = Path.Combine(_tempRoot, "mixed_gpo_admx_without_dot.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var localPolicies = archive.CreateEntry("Support Files/Local Policies/user.pol");
      await using (var writer = new StreamWriter(localPolicies.Open()))
      {
        await writer.WriteAsync("stub");
      }

      var admx = archive.CreateEntry("Policies/windows.admx");
      await using (var writer = new StreamWriter(admx.Open()))
      {
        await writer.WriteAsync(CreateMinimalAdmx("WindowsPolicy"));
      }
    }

    var scanner = new ImportInboxScanner(new NoopHashingService());
    var scan = await scanner.ScanAsync(_tempRoot, CancellationToken.None);
    var candidates = new ImportDedupService()
      .Resolve(scan.Candidates)
      .Winners
      .Where(c => c.ArtifactKind == ImportArtifactKind.Gpo || c.ArtifactKind == ImportArtifactKind.Admx)
      .ToList();

    var plan = ImportQueuePlanner.BuildContentImportPlan(candidates);
    Assert.Equal(2, plan.Count);
    Assert.Contains(plan, p => p.ArtifactKind == ImportArtifactKind.Gpo && p.Route == ContentImportRoute.ConsolidatedZip);
    Assert.Contains(plan, p => p.ArtifactKind == ImportArtifactKind.Admx && p.Route == ContentImportRoute.AdmxTemplatesFromZip);

    var importer = CreateImporter(out var packsMock, out var controlsMock);

    var totalImported = 0;
    foreach (var planned in plan)
    {
      var imported = planned.Route == ContentImportRoute.AdmxTemplatesFromZip
        ? await importer.ImportAdmxTemplatesFromZipAsync(planned.ZipPath, planned.SourceLabel, CancellationToken.None)
        : await importer.ImportConsolidatedZipAsync(planned.ZipPath, planned.SourceLabel, CancellationToken.None);

      totalImported += imported.Count;
    }

    Assert.Equal(2, totalImported);
    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    controlsMock.Verify(c => c.SaveControlsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<ControlRecord>>(list => list.Count > 0), It.IsAny<CancellationToken>()), Times.AtLeastOnce());
  }

  [Fact]
  public async Task ImportConsolidatedZipAsync_SplitsLocalAndDomainGpoRoots_WithoutOuterFallbackPack()
  {
    var zipPath = Path.Combine(_tempRoot, "consolidated-scoped-gpo.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var windows11 = archive.CreateEntry("Support Files/Local Policies/Windows 11/local.pol");
      await using (var writer = new StreamWriter(windows11.Open()))
      {
        await writer.WriteAsync("stub");
      }

      var windowsServer = archive.CreateEntry("Support Files/Local Policies/Windows Server 2022/local.pol");
      await using (var writer = new StreamWriter(windowsServer.Open()))
      {
        await writer.WriteAsync("stub");
      }

      var domainRegistry = archive.CreateEntry("gpos/example.com/DomainSysvol/GPO/{12345678-1234-1234-1234-1234567890AB}/Machine/registry.pol");
      await using (var writer = new StreamWriter(domainRegistry.Open()))
      {
        await writer.WriteAsync("stub");
      }
    }

    var importer = CreateImporter(out var packsMock, out _);
    var imported = await importer.ImportConsolidatedZipAsync(zipPath, "gpo_lgpo_import", CancellationToken.None);

    Assert.Equal(3, imported.Count);
    Assert.Contains(imported, pack => string.Equals(pack.Name, "Local Policy - Windows 11", StringComparison.Ordinal));
    Assert.Contains(imported, pack => string.Equals(pack.Name, "Local Policy - Windows Server 2022", StringComparison.Ordinal));
    Assert.Contains(imported, pack => string.Equals(pack.Name, "Domain GPO - example.com", StringComparison.Ordinal));

    Assert.Contains(imported, pack => string.Equals(pack.Name, "Domain GPO - example.com", StringComparison.Ordinal)
      && string.Equals(pack.SourceLabel, "gpo_domain_import", StringComparison.Ordinal));

    var outerBaseName = Path.GetFileNameWithoutExtension(zipPath);
    Assert.DoesNotContain(imported, pack => string.Equals(pack.Name, outerBaseName, StringComparison.OrdinalIgnoreCase));

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
  }

  [Fact]
  public async Task ImportConsolidatedZipAsync_DodStyleDomainGpoBundle_GroupsByParentFolder()
  {
    var zipPath = Path.Combine(_tempRoot, "consolidated-dod-domain-gpos.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var windows11Machine = archive.CreateEntry("DoD Windows 11 v2r6/GPOs/{11111111-1111-1111-1111-111111111111}/DomainSysvol/GPO/Machine/registry.pol");
      await using (var writer = new StreamWriter(windows11Machine.Open()))
      {
        await writer.WriteAsync("stub");
      }

      var windows11User = archive.CreateEntry("DoD Windows 11 v2r6/GPOs/{22222222-2222-2222-2222-222222222222}/DomainSysvol/GPO/User/registry.pol");
      await using (var writer = new StreamWriter(windows11User.Open()))
      {
        await writer.WriteAsync("stub");
      }

      var edgeMachine = archive.CreateEntry("DoD Microsoft Edge v2r4/GPOs/{33333333-3333-3333-3333-333333333333}/DomainSysvol/GPO/Machine/registry.pol");
      await using (var writer = new StreamWriter(edgeMachine.Open()))
      {
        await writer.WriteAsync("stub");
      }
    }

    var importer = CreateImporter(out var packsMock, out _);
    var imported = await importer.ImportConsolidatedZipAsync(zipPath, "gpo_lgpo_import", CancellationToken.None);

    Assert.Equal(2, imported.Count);
    Assert.Contains(imported, pack => string.Equals(pack.Name, "Domain GPO - DoD Windows 11 v2r6", StringComparison.Ordinal));
    Assert.Contains(imported, pack => string.Equals(pack.Name, "Domain GPO - DoD Microsoft Edge v2r4", StringComparison.Ordinal));
    Assert.DoesNotContain(imported, pack => pack.Name.IndexOf("{", StringComparison.Ordinal) >= 0);

    packsMock.Verify(p => p.SaveAsync(It.IsAny<ContentPack>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
  }

  [Fact]
  public async Task ExecutePlannedImportAsync_SuccessfulImport_TransitionsThroughStagedToCommitted()
  {
    var zipPath = Path.Combine(_tempRoot, "planned-stig.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var xccdf = archive.CreateEntry("bundle-xccdf.xml");
      await using var writer = new StreamWriter(xccdf.Open());
      await writer.WriteAsync(CreateMinimalXccdf());
    }

    var planned = new PlannedContentImport
    {
      ZipPath = zipPath,
      FileName = Path.GetFileName(zipPath),
      ArtifactKind = ImportArtifactKind.Stig,
      Route = ContentImportRoute.ConsolidatedZip,
      SourceLabel = "unit-test",
      State = ImportOperationState.Planned
    };

    var importer = CreateImporter(out _, out _);
    var result = await importer.ExecutePlannedImportAsync(planned, CancellationToken.None);

    Assert.NotEmpty(result);
    Assert.Equal(ImportOperationState.Committed, planned.State);
    Assert.Null(planned.FailureReason);
  }

  [Fact]
  public async Task ExecutePlannedImportAsync_FailedImport_TransitionsToFailedWithReason()
  {
    // Provide a non-existent ZIP to force failure
    var planned = new PlannedContentImport
    {
      ZipPath = Path.Combine(_tempRoot, "nonexistent.zip"),
      FileName = "nonexistent.zip",
      ArtifactKind = ImportArtifactKind.Stig,
      Route = ContentImportRoute.ConsolidatedZip,
      SourceLabel = "unit-test",
      State = ImportOperationState.Planned
    };

    var importer = CreateImporter(out _, out _);
    var act = () => importer.ExecutePlannedImportAsync(planned, CancellationToken.None);

    await Assert.ThrowsAnyAsync<Exception>(act);
    Assert.Equal(ImportOperationState.Failed, planned.State);
    Assert.NotNull(planned.FailureReason);
    Assert.False(string.IsNullOrWhiteSpace(planned.FailureReason));
  }

  [Fact]
  public async Task ExecutePlannedImportAsync_NullPlanned_ThrowsArgumentNullException()
  {
    var importer = CreateImporter(out _, out _);
    var act = () => importer.ExecutePlannedImportAsync(null!, CancellationToken.None);

    await Assert.ThrowsAsync<ArgumentNullException>(act);
  }

  private ContentPackImporter CreateImporter(out Mock<IContentPackRepository> packsMock, out Mock<IControlRepository> controlsMock)
  {
    var pathsMock = new Mock<IPathBuilder>();
    pathsMock
      .Setup(p => p.GetPackRoot(It.IsAny<string>()))
      .Returns<string>(packId => Path.Combine(_tempRoot, "packs", packId));

    var hashMock = new Mock<IHashingService>();
    hashMock
      .Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("fake-sha256");

    packsMock = new Mock<IContentPackRepository>();
    packsMock
      .Setup(p => p.ListAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ContentPack>());

    controlsMock = new Mock<IControlRepository>();
    controlsMock
      .Setup(c => c.ListControlsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(Array.Empty<ControlRecord>());
    controlsMock
      .Setup(c => c.VerifySchemaAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);

    return new ContentPackImporter(pathsMock.Object, hashMock.Object, packsMock.Object, controlsMock.Object);
  }

  private async Task<string> CreateZipFromFixturesAsync(string zipName, IReadOnlyList<string> fixtureFiles)
  {
    var zipPath = Path.Combine(_tempRoot, zipName);
    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

    foreach (var fixtureName in fixtureFiles)
    {
      var fixturePath = GetFixturePath(fixtureName);
      var entry = archive.CreateEntry(fixtureName);
      await using var entryStream = entry.Open();
      await using var fixtureStream = File.OpenRead(fixturePath);
      await fixtureStream.CopyToAsync(entryStream);
    }

    return zipPath;
  }

  private static string GetFixturePath(string fileName)
  {
    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
    return Path.Combine(baseDir, "..", "..", "..", "fixtures", fileName);
  }

  private static void AssertCompatibilityMatrixHasRequiredKeys(JsonElement root)
  {
    var expectedTopLevelKeys = new[]
    {
      "schemaVersion",
      "detectedFormat",
      "detectionConfidence",
      "detectionReasons",
      "parsedControls",
      "usedFallbackParser",
      "sourceArtifacts",
      "support",
      "lossyMappings",
      "unsupportedMappings",
      "parsingErrors",
      "conflicts"
    };

    var actualTopLevelKeys = root.EnumerateObject().Select(property => property.Name).ToArray();
    Assert.Equal(expectedTopLevelKeys, actualTopLevelKeys);

    Assert.True(root.TryGetProperty("sourceArtifacts", out var sourceArtifacts));
    Assert.True(sourceArtifacts.TryGetProperty("XccdfXmlCount", out _));
    Assert.True(sourceArtifacts.TryGetProperty("OvalXmlCount", out _));
    Assert.True(sourceArtifacts.TryGetProperty("AdmxCount", out _));
    Assert.True(sourceArtifacts.TryGetProperty("TotalXmlCount", out _));
    Assert.True(sourceArtifacts.TryGetProperty("expectedFormatFiles", out _));

    Assert.True(root.TryGetProperty("support", out var support));
    Assert.True(support.TryGetProperty("xccdf", out _));
    Assert.True(support.TryGetProperty("ovalMetadata", out _));
    Assert.True(support.TryGetProperty("admx", out _));

    Assert.True(root.TryGetProperty("conflicts", out var conflicts));
    Assert.True(conflicts.TryGetProperty("total", out _));
    Assert.True(conflicts.TryGetProperty("bySeverity", out var bySeverity));
    Assert.True(bySeverity.TryGetProperty("info", out _));
    Assert.True(bySeverity.TryGetProperty("warning", out _));
    Assert.True(bySeverity.TryGetProperty("error", out _));
    Assert.True(conflicts.TryGetProperty("details", out _));
  }

  private static string CreateMinimalXccdf()
  {
    return """
<Benchmark xmlns="http://checklists.nist.gov/xccdf/1.2" id="test-benchmark">
  <Rule id="SV-123456r1_rule" severity="medium">
    <title>Test Rule</title>
    <description>Test description.</description>
    <check system="manual">
      <check-content>Manually verify this setting.</check-content>
    </check>
    <fixtext>Apply fix.</fixtext>
  </Rule>
</Benchmark>
""";
  }

  private static string CreateMinimalAdmx(string policyName)
  {
    return $"""
<?xml version="1.0" encoding="utf-8"?>
<policyDefinitions revision="1.0" schemaVersion="1.0">
  <target namespace="Microsoft.Policies.Test"/>
  <policies>
    <policy name="{policyName}" class="Machine" displayName="{policyName}" key="Software\\Policies\\Test" valueName="Enabled">
      <enabledValue>
        <decimal value="1"/>
      </enabledValue>
    </policy>
  </policies>
</policyDefinitions>
""";
  }

  private static string CreateMinimalScapDataStream(string benchmarkId, string releaseTag)
  {
    return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      + "<data-stream-collection xmlns=\"http://scap.nist.gov/schema/scap/source/1.2\" xmlns:xccdf=\"http://checklists.nist.gov/xccdf/1.2\">"
      + "<data-stream id=\"scap_stream\">"
      + "<component id=\"xccdf_component\">"
      + "<xccdf:Benchmark id=\"" + benchmarkId + "\">"
      + "<xccdf:title>Windows 11 STIG</xccdf:title>"
      + "<xccdf:version>2.7</xccdf:version>"
      + "<xccdf:status date=\"2026-01-01\">accepted</xccdf:status>"
      + "<xccdf:plain-text>Release: " + releaseTag + " Benchmark Date: 2026-01-01</xccdf:plain-text>"
      + "<xccdf:Rule id=\"SV-1000r1_rule\" severity=\"medium\">"
      + "<xccdf:title>Sample Rule</xccdf:title>"
      + "<xccdf:check system=\"manual\"><xccdf:check-content>Verify manually.</xccdf:check-content></xccdf:check>"
      + "<xccdf:fixtext>Apply fix.</xccdf:fixtext>"
      + "</xccdf:Rule>"
      + "</xccdf:Benchmark>"
      + "</component>"
      + "</data-stream>"
      + "</data-stream-collection>";
  }

  private sealed class NoopHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult("sha-stub");

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult("sha-text");
  }
}
