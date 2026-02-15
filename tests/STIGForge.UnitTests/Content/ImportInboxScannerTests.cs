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
