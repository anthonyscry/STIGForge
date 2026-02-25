using System.IO.Compression;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.IntegrationTests.Content;

public sealed class ImportInboxScannerRegressionTests : IDisposable
{
  private readonly string _tempRoot;

  public ImportInboxScannerRegressionTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-import-scanner-regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, recursive: true); } catch { }
  }

  [Fact]
  public async Task ScanAsync_DetectsUppercaseZipExtensions()
  {
    var zipPath = Path.Combine(_tempRoot, "WINDOWS_11_BASELINE.ZIP");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("benchmark-xccdf.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateMinimalXccdf("xccdf_org.test.uppercase", "V1R1"));
    }

    var scanner = new ImportInboxScanner(new FixedHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Single(result.Candidates);
    Assert.Equal(ImportArtifactKind.Stig, result.Candidates[0].ArtifactKind);
  }

  [Fact]
  public async Task ScanAsync_DetectsNestedStigZipEvenWhenToolSignatureExists()
  {
    var zipPath = Path.Combine(_tempRoot, "content-and-tools.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "tools/Evaluate-STIG.ps1", "Write-Host 'ok'");
      await WriteEntryAsync(archive, "library/U_Windows_11_V2R7_STIG.zip", "PK");
    }

    var scanner = new ImportInboxScanner(new FixedHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Tool && c.ToolKind == ToolArtifactKind.EvaluateStig);
    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Stig);
  }

  [Fact]
  public async Task ScanAsync_DetectsNestedSccToolZip()
  {
    var zipPath = Path.Combine(_tempRoot, "nested-scc-bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "downloads/scc-5.14_Windows.zip", "PK");
    }

    var scanner = new ImportInboxScanner(new FixedHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Tool && c.ToolKind == ToolArtifactKind.Scc);
  }

  [Fact]
  public async Task ScanAsync_DetectsNestedEvaluateStigToolZip()
  {
    var zipPath = Path.Combine(_tempRoot, "nested-evaluate-bundle.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "downloads/Evaluate-STIG-v2.4.zip", "PK");
    }

    var scanner = new ImportInboxScanner(new FixedHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Tool && c.ToolKind == ToolArtifactKind.EvaluateStig);
  }

  [Fact]
  public async Task ScanAsync_DetectsCsccExecutableAsSccTool()
  {
    var zipPath = Path.Combine(_tempRoot, "scc-cli.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      await WriteEntryAsync(archive, "SCC/cscc.exe", "MZ");
    }

    var scanner = new ImportInboxScanner(new FixedHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Contains(result.Candidates, c => c.ArtifactKind == ImportArtifactKind.Tool && c.ToolKind == ToolArtifactKind.Scc);
  }

  private static async Task WriteEntryAsync(ZipArchive archive, string path, string content)
  {
    var entry = archive.CreateEntry(path);
    await using var writer = new StreamWriter(entry.Open());
    await writer.WriteAsync(content);
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

  private sealed class FixedHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult("sha-" + Path.GetFileName(path));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult("sha-text");
  }
}
