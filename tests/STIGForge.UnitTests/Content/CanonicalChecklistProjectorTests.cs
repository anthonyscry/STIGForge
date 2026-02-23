using System.IO.Compression;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Content;

public sealed class CanonicalChecklistProjectorTests : IDisposable
{
  private readonly string _tempRoot;

  public CanonicalChecklistProjectorTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-canonical-checklist-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempRoot))
      Directory.Delete(_tempRoot, true);
  }

  [Fact]
  public async Task ScanAsync_ProjectsCanonicalChecklistFromImportedStigContent()
  {
    var zipPath = Path.Combine(_tempRoot, "win11_stig_v2r1.zip");
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
      var entry = archive.CreateEntry("U_MS_Windows_11_STIG_V2R1_Manual-xccdf.xml");
      await using var writer = new StreamWriter(entry.Open());
      await writer.WriteAsync(CreateXccdfWithRules("xccdf_org.test.benchmark_win11", "SV-1000r1_rule", "SV-1001r1_rule"));
    }

    var scanner = new ImportInboxScanner(new TestHashingService());
    var result = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Equal(2, result.CanonicalChecklist.Count);
    Assert.Contains(result.CanonicalChecklist, i => i.StigId == "xccdf_org.test.benchmark_win11" && i.RuleId == "SV-1000r1_rule");
    Assert.Contains(result.CanonicalChecklist, i => i.StigId == "xccdf_org.test.benchmark_win11" && i.RuleId == "SV-1001r1_rule");
  }

  private static string CreateXccdfWithRules(string benchmarkId, string firstRuleId, string secondRuleId)
  {
    return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
      + "<Benchmark xmlns=\"http://checklists.nist.gov/xccdf/1.2\" id=\"" + benchmarkId + "\">"
      + "<title>Windows 11 STIG</title>"
      + "<version>2.1</version>"
      + "<status date=\"2026-01-01\">accepted</status>"
      + "<plain-text>Release: V2R1 Benchmark Date: 2026-01-01</plain-text>"
      + "<Rule id=\"" + firstRuleId + "\" severity=\"medium\"><title>Rule 1</title></Rule>"
      + "<Rule id=\"" + secondRuleId + "\" severity=\"medium\"><title>Rule 2</title></Rule>"
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
