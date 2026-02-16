using System.IO.Compression;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Content;

public sealed class ImportInboxScannerTests : IDisposable
{
  private readonly string _tempRoot;

  public ImportInboxScannerTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-inbox-scan-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task ScanAsync_ClassifiesStigZip()
  {
    var zipPath = Path.Combine(_tempRoot, "win11_stig_v2r1.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("U_MS_Windows_11_STIG_V2R1_Manual-xccdf.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateMinimalXccdf("xccdf_org.ssgproject.content_benchmark_WIN11", "V2R1"));
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Stig, candidate.ArtifactKind);
    Assert.Equal("V2R1", candidate.VersionTag);
    Assert.StartsWith("stig:", candidate.ContentKey, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ScanAsync_ClassifiesScapDataStreamBenchmarkWithoutXccdfFilename()
  {
    var zipPath = Path.Combine(_tempRoot, "win11_scap_benchmark.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("U_MS_Windows_11_V2R7_STIG_SCAP_1-3_Benchmark.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateMinimalScapDataStream("xccdf_org.test.win11_benchmark", "V2R7"));
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Scap, candidate.ArtifactKind);
    Assert.Equal("V2R7", candidate.VersionTag);
    Assert.StartsWith("scap:", candidate.ContentKey, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ScanAsync_DedupesEnhancedAndStandardScapByBenchmarkIdentity()
  {
    var standardZipPath = Path.Combine(_tempRoot, "U_CAN_Ubuntu_22-04_LTS_V2R5_STIG_SCAP_1-3_Benchmark.zip");
    using (var archive = ZipFile.Open(standardZipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("U_CAN_Ubuntu_22-04_LTS_V2R5_STIG_SCAP_1-3_Benchmark.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateMinimalScapDataStream(
        "xccdf_mil.disa.stig_benchmark_CAN_Ubuntu_22-04_LTS_STIG",
        "V2R5",
        "scap_mil.disa.stig_collection_U_CAN_Ubuntu_22-04_LTS_V2R5_STIG_SCAP_1-3_Benchmark",
        "Canonical Ubuntu 22.04 LTS STIG SCAP Benchmark"));
    }

    var enhancedZipPath = Path.Combine(_tempRoot, "NIWC_Consolidated_Enhanced_Bundle.zip");
    using (var archive = ZipFile.Open(enhancedZipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("U_CAN_Ubuntu_22-04_LTS_V2R5_STIG_SCAP_1-4_Benchmark-enhancedV8-signed.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateMinimalScapDataStream(
        "xccdf_mil.disa.stig_benchmark_CAN_Ubuntu_22-04_LTS_STIG",
        "V2R5",
        "scap_navy.navwar.niwcatlantic.scc_collection_U_CAN_Ubuntu_22-04_LTS_V2R5_STIG_SCAP_1-4_Benchmark-enhancedV8",
        "Canonical Ubuntu 22.04 LTS STIG SCAP Benchmark - NIWC Enhanced with Manual Questions"));
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var deduped = new ImportDedupService().Resolve(result.Candidates);
    var scapWinners = deduped.Winners.Where(c => c.ArtifactKind == ImportArtifactKind.Scap).ToList();

    var winner = Assert.Single(scapWinners);
    Assert.Equal(ImportProvenance.ConsolidatedBundle, winner.ImportedFrom);
    Assert.True(winner.IsNiwcEnhanced);
  }

  [Fact]
  public async Task ScanAsync_ClassifiesToolZip()
  {
    var zipPath = Path.Combine(_tempRoot, "Evaluate-STIG.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("Evaluate-STIG/Evaluate-STIG.ps1");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync("Write-Host 'ok'");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Tool, candidate.ArtifactKind);
    Assert.Equal(ToolArtifactKind.EvaluateStig, candidate.ToolKind);
    Assert.Equal("tool:evaluate-stig", candidate.ContentKey);
  }

  [Fact]
  public async Task ScanAsync_EmitsCandidatesForMultipleArtifactTypesInSingleArchive()
  {
    var zipPath = Path.Combine(_tempRoot, "mixed_bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "TOOLS/Evaluate-STIG.ps1", "Write-Host 'ok'");
      await WriteEntryAsync(archive, ".Support Files/Local Policies/machine.pol", "stub");
      await WriteEntryAsync(archive, "ADMX Templates/windows.admx", "<policyDefinitions revision=\"1.0\" />");
      await WriteEntryAsync(archive, "SCAP/one-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.benchmark_one", "V1R1"));
      await WriteEntryAsync(archive, "SCAP/two-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.benchmark_two", "V1R2"));
      await WriteEntryAsync(archive, "SCAP/benchmark-oval.xml", "<oval_definitions />");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Tool && c.ToolKind == ToolArtifactKind.EvaluateStig);
    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Gpo);
    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Admx);

    var scapCandidates = result.Candidates.Where(c => c.ArtifactKind == ImportArtifactKind.Scap).ToList();
    Assert.Equal(2, scapCandidates.Count);
  }

  [Fact]
  public async Task ScanAsync_DetectsGpoSupportFilesWithoutLeadingDot()
  {
    var zipPath = Path.Combine(_tempRoot, "gpo_without_dot.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Support Files/Local Policies/machine.pol", "stub");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Gpo, candidate.ArtifactKind);
  }

  [Fact]
  public async Task ScanAsync_DetectsDomainGpoStructureUnderGpos()
  {
    var zipPath = Path.Combine(_tempRoot, "domain_gpo_bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(
        archive,
        "gpos/example.com/DomainSysvol/GPO/{12345678-1234-1234-1234-1234567890AB}/Machine/registry.pol",
        "stub");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Gpo);
  }

  [Fact]
  public async Task ScanAsync_EmitsBothGpoAndAdmx_WhenSupportFilesFolderHasNoLeadingDot()
  {
    var zipPath = Path.Combine(_tempRoot, "mixed_gpo_admx_without_dot.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Support Files/Local Policies/User/registry.pol", "stub");
      await WriteEntryAsync(archive, "Policies/windows.admx", "<policyDefinitions revision=\"1.0\" />");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Gpo);
    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Admx);

    var winners = new ImportDedupService().Resolve(result.Candidates).Winners
      .Where(c => c.ArtifactKind == ImportArtifactKind.Gpo || c.ArtifactKind == ImportArtifactKind.Admx)
      .ToList();

    var plan = ImportQueuePlanner.BuildContentImportPlan(winners);
    Assert.Equal(2, plan.Count);
    Assert.Contains(plan, p => p.ArtifactKind == ImportArtifactKind.Gpo && p.Route == ContentImportRoute.ConsolidatedZip);
    Assert.Contains(plan, p => p.ArtifactKind == ImportArtifactKind.Admx && p.Route == ContentImportRoute.AdmxTemplatesFromZip);
  }

  [Fact]
  public async Task ScanAsync_UsesPolicyNamespaceForAdmxContentKey()
  {
    var zipPath = Path.Combine(_tempRoot, "admx_namespace.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(
        archive,
        "PolicyDefinitions/windows.admx",
        "<policyDefinitions revision=\"1.0\"><policyNamespaces><target prefix=\"windows\" namespace=\"Microsoft.Policies.Windows\" /></policyNamespaces></policyDefinitions>");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = result.Candidates.Single(c => c.FileName == "admx_namespace.zip" && c.ArtifactKind == ImportArtifactKind.Admx);
    Assert.Equal("admx:microsoft.policies.windows:1.0", candidate.ContentKey);
  }

  [Fact]
  public async Task ScanAsync_DoesNotClassifyXccdfAsScapWhenOnlyUnrelatedOvalExists()
  {
    var zipPath = Path.Combine(_tempRoot, "unrelated_oval_bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/benchmark-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.benchmark_three", "V3R1"));
      await WriteEntryAsync(archive, "Other/other-oval.xml", "<oval_definitions />");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var target = result.Candidates.Single(c => c.FileName == "unrelated_oval_bundle.zip");
    Assert.Equal(ImportArtifactKind.Stig, target.ArtifactKind);
  }

  [Fact]
  public async Task ScanAsync_PropagatesCancellationFromCandidateBuild()
  {
    var zipPath = Path.Combine(_tempRoot, "cancel-me.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/benchmark-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.cancel", "V1R1"));
    }

    var scanner = new ImportInboxScanner(new CancelingHashingService());

    await Assert.ThrowsAsync<OperationCanceledException>(() => scanner.ScanAsync(_tempRoot, CancellationToken.None));
  }

  [Fact]
  public async Task ScanAsync_UsesFilenameFallbackWhenXccdfXmlIsMalformed()
  {
    var zipPath = Path.Combine(_tempRoot, "malformed_xccdf.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/broken-xccdf.xml", "<Benchmark><title>");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Stig, candidate.ArtifactKind);
    Assert.Equal("stig:broken-xccdf", candidate.ContentKey);
    Assert.True(string.IsNullOrWhiteSpace(candidate.VersionTag));
  }

  [Fact]
  public async Task ScanAsync_RepeatedScanWithLedger_RequeuesUntilMarkedProcessed()
  {
    var zipPath = Path.Combine(_tempRoot, "repeatable.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/benchmark-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.repeatable", "V1R1"));
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var firstScan = await scanner.ScanAsync(_tempRoot, CancellationToken.None);
    var secondScan = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var dedup = new ImportDedupService();
    var firstWinners = dedup.Resolve(firstScan.Candidates).Winners
      .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
      .ToList();
    var secondWinners = dedup.Resolve(secondScan.Candidates).Winners
      .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
      .ToList();

    var ledger = new ImportProcessedArtifactLedger();
    var firstPlan = ImportQueuePlanner.BuildContentImportPlan(firstWinners, ledger);
    var secondPlan = ImportQueuePlanner.BuildContentImportPlan(secondWinners, ledger);

    Assert.Single(firstPlan);
    Assert.Single(secondPlan);

    var first = firstPlan.Single();
    ledger.MarkProcessed(first.Sha256, first.Route);

    var thirdPlan = ImportQueuePlanner.BuildContentImportPlan(secondWinners, ledger);
    Assert.Empty(thirdPlan);
  }

  [Fact]
  public async Task ScanAsync_ClassifiesNestedStigZipLibraryAsStig()
  {
    var zipPath = Path.Combine(_tempRoot, "srg_library.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "U_Windows_11_V2R7_STIG.zip", "PK");
      await WriteEntryAsync(archive, "U_RHEL_9_V2R7_STIG.zip", "PK");
      await WriteEntryAsync(archive, "U_DBMS_V1R1_SRG.zip", "PK");
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var candidate = Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Stig, candidate.ArtifactKind);
    Assert.StartsWith("stig:", candidate.ContentKey, StringComparison.OrdinalIgnoreCase);

    var winners = new ImportDedupService()
      .Resolve(result.Candidates)
      .Winners
      .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
      .ToList();

    var plan = Assert.Single(ImportQueuePlanner.BuildContentImportPlan(winners));
    Assert.Equal(ContentImportRoute.ConsolidatedZip, plan.Route);
  }

  [Fact]
  public async Task ScanAsync_SkipsInaccessibleSubdirectoryAndContinues()
  {
    if (OperatingSystem.IsWindows())
      return;

    var visibleZip = Path.Combine(_tempRoot, "visible.zip");
    using (var archive = ZipFile.Open(visibleZip, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/visible-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.visible", "V1R1"));
    }

    var blockedDir = Path.Combine(_tempRoot, "blocked");
    Directory.CreateDirectory(blockedDir);
    var blockedZip = Path.Combine(blockedDir, "hidden.zip");
    using (var archive = ZipFile.Open(blockedZip, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "Windows/hidden-xccdf.xml", CreateMinimalXccdf("xccdf_org.test.hidden", "V1R1"));
    }

    try
    {
      File.SetUnixFileMode(blockedDir, UnixFileMode.UserWrite | UnixFileMode.UserExecute);

      if (CanEnumerateDirectory(blockedDir))
        return;

      var scanner = new ImportInboxScanner(new TestHashingService());
      var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

      Assert.Contains(result.Candidates, c => c.FileName == "visible.zip");
      Assert.DoesNotContain(result.Candidates, c => c.FileName == "hidden.zip");
      Assert.Contains(result.Warnings, w => w.Contains("Skipping inaccessible", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
      File.SetUnixFileMode(blockedDir,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }
  }

  private static bool CanEnumerateDirectory(string path)
  {
    try
    {
      _ = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
      return true;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
    catch (IOException)
    {
      return false;
    }
  }

  private static string CreateMinimalXccdf(string benchmarkId, string releaseTag)
  {
    return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      + "<Benchmark xmlns=\"http://checklists.nist.gov/xccdf/1.2\" id=\"" + benchmarkId + "\">"
      + "<title>Windows 11 STIG</title>"
      + "<version>2.1</version>"
      + "<status date=\"2026-01-01\">accepted</status>"
      + "<plain-text>Release: " + releaseTag + " Benchmark Date: 2026-01-01</plain-text>"
      + "</Benchmark>";
  }

  private static string CreateMinimalScapDataStream(
    string benchmarkId,
    string releaseTag,
    string collectionId = "",
    string benchmarkTitle = "Windows 11 STIG")
  {
    var collectionAttribute = string.IsNullOrWhiteSpace(collectionId)
      ? string.Empty
      : " id=\"" + collectionId + "\"";

    return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      + "<data-stream-collection xmlns=\"http://scap.nist.gov/schema/scap/source/1.2\" xmlns:xccdf=\"http://checklists.nist.gov/xccdf/1.2\"" + collectionAttribute + ">"
      + "<data-stream id=\"scap_stream\">"
      + "<component id=\"xccdf_component\">"
      + "<xccdf:Benchmark id=\"" + benchmarkId + "\">"
      + "<xccdf:title>" + benchmarkTitle + "</xccdf:title>"
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

  private static async Task WriteEntryAsync(ZipArchive archive, string entryPath, string content)
  {
    var entry = archive.CreateEntry(entryPath);
    await using var writer = new StreamWriter(entry.Open());
    await writer.WriteAsync(content);
  }

  private sealed class TestHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult("sha-" + Path.GetFileName(path));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult("sha-text");
  }

  private sealed class CancelingHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => throw new OperationCanceledException();

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => throw new OperationCanceledException();
  }
}
