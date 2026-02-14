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

  private sealed class TestHashingService : IHashingService
  {
    public Task<string> Sha256FileAsync(string path, CancellationToken ct)
      => Task.FromResult("sha-" + Path.GetFileName(path));

    public Task<string> Sha256TextAsync(string content, CancellationToken ct)
      => Task.FromResult("sha-text");
  }
}
