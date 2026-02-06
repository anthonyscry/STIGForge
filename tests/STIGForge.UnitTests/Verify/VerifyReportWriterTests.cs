using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public class VerifyReportWriterTests : IDisposable
{
  private readonly string _tempDir;

  public VerifyReportWriterTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-verify-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempDir, true); } catch { }
  }

  // ── BuildCoverageSummary ─────────────────────────────────────────────

  [Fact]
  public void BuildCoverageSummary_EmptyResults_ReturnsEmpty()
  {
    var summary = VerifyReportWriter.BuildCoverageSummary(Array.Empty<ControlResult>());
    summary.Should().BeEmpty();
  }

  [Fact]
  public void BuildCoverageSummary_MixedResults_GroupsByTool()
  {
    var results = new List<ControlResult>
    {
      new() { VulnId = "V-1", Status = "NotAFinding", Tool = "SCAP" },
      new() { VulnId = "V-2", Status = "Open", Tool = "SCAP" },
      new() { VulnId = "V-3", Status = "NotAFinding", Tool = "CKL" }
    };

    var summary = VerifyReportWriter.BuildCoverageSummary(results);

    summary.Should().HaveCount(2);
    var scap = summary.First(s => s.Tool == "SCAP");
    scap.TotalCount.Should().Be(2);
    scap.ClosedCount.Should().Be(1);
    scap.OpenCount.Should().Be(1);
  }

  // ── BuildOverlapSummary ──────────────────────────────────────────────

  [Fact]
  public void BuildOverlapSummary_SingleTool_SingleGroup()
  {
    var results = new List<ControlResult>
    {
      new() { VulnId = "V-1", RuleId = "SV-1", Status = "NotAFinding", Tool = "SCAP" },
      new() { VulnId = "V-2", RuleId = "SV-2", Status = "Open", Tool = "SCAP" }
    };

    var overlaps = VerifyReportWriter.BuildOverlapSummary(results);
    overlaps.Should().NotBeEmpty();
  }

  [Fact]
  public void BuildOverlapSummary_MultipleTools_IdentifiesOverlap()
  {
    var results = new List<ControlResult>
    {
      new() { VulnId = "V-1", RuleId = "SV-1", Status = "NotAFinding", Tool = "SCAP" },
      new() { VulnId = "V-1", RuleId = "SV-1", Status = "NotAFinding", Tool = "CKL" },
      new() { VulnId = "V-2", RuleId = "SV-2", Status = "Open", Tool = "SCAP" }
    };

    var overlaps = VerifyReportWriter.BuildOverlapSummary(results);
    overlaps.Should().NotBeEmpty();
    // V-1 covered by 2 tools, V-2 by 1
    overlaps.Sum(o => o.ControlsCount).Should().Be(2);
  }

  // ── BuildControlSourceMap ────────────────────────────────────────────

  [Fact]
  public void BuildControlSourceMap_MapsControlsToSources()
  {
    var results = new List<ControlResult>
    {
      new() { VulnId = "V-1", RuleId = "SV-1", Title = "Control 1", Status = "NotAFinding", Tool = "SCAP" },
      new() { VulnId = "V-2", RuleId = "SV-2", Title = "Control 2", Status = "Open", Tool = "CKL" }
    };

    var maps = VerifyReportWriter.BuildControlSourceMap(results);

    maps.Should().HaveCount(2);
    maps.Should().Contain(m => m.VulnId == "V-1" && m.IsClosed);
    maps.Should().Contain(m => m.VulnId == "V-2" && !m.IsClosed);
  }

  // ── WriteJson / WriteCsv ─────────────────────────────────────────────

  [Fact]
  public void WriteJson_CreatesValidFile()
  {
    var report = new VerifyReport
    {
      Tool = "TestTool",
      ToolVersion = "1.0",
      StartedAt = DateTimeOffset.UtcNow,
      FinishedAt = DateTimeOffset.UtcNow,
      OutputRoot = _tempDir,
      Results = new List<ControlResult>
      {
        new() { VulnId = "V-1", Status = "NotAFinding", Tool = "TestTool" }
      }
    };

    var path = Path.Combine(_tempDir, "report.json");
    VerifyReportWriter.WriteJson(path, report);

    File.Exists(path).Should().BeTrue();
    var content = File.ReadAllText(path);
    content.Should().Contain("TestTool");
    content.Should().Contain("V-1");
  }

  [Fact]
  public void WriteCsv_CreatesValidFile()
  {
    var results = new List<ControlResult>
    {
      new() { VulnId = "V-1", RuleId = "SV-1", Title = "Test", Severity = "high", Status = "Open", Tool = "SCAP", SourceFile = "test.ckl" }
    };

    var path = Path.Combine(_tempDir, "results.csv");
    VerifyReportWriter.WriteCsv(path, results);

    File.Exists(path).Should().BeTrue();
    var lines = File.ReadAllLines(path);
    lines.Length.Should().Be(2); // header + 1 row
    lines[0].Should().StartWith("VulnId,RuleId");
    lines[1].Should().Contain("V-1");
  }

  // ── WriteCoverageSummary ─────────────────────────────────────────────

  [Fact]
  public void WriteCoverageSummary_CreatesBothFiles()
  {
    var summaries = new List<CoverageSummary>
    {
      new() { Tool = "SCAP", ClosedCount = 10, OpenCount = 5, TotalCount = 15, ClosedPercent = 66.7 }
    };

    var csvPath = Path.Combine(_tempDir, "summary.csv");
    var jsonPath = Path.Combine(_tempDir, "summary.json");
    VerifyReportWriter.WriteCoverageSummary(csvPath, jsonPath, summaries);

    File.Exists(csvPath).Should().BeTrue();
    File.Exists(jsonPath).Should().BeTrue();
  }

  // ── WriteOverlapSummary ──────────────────────────────────────────────

  [Fact]
  public void WriteOverlapSummary_CreatesBothFiles()
  {
    var overlaps = new List<CoverageOverlap>
    {
      new() { SourcesKey = "SCAP", SourceCount = 1, ControlsCount = 5, ClosedCount = 3, OpenCount = 2 }
    };

    var csvPath = Path.Combine(_tempDir, "overlap.csv");
    var jsonPath = Path.Combine(_tempDir, "overlap.json");
    VerifyReportWriter.WriteOverlapSummary(csvPath, jsonPath, overlaps);

    File.Exists(csvPath).Should().BeTrue();
    File.Exists(jsonPath).Should().BeTrue();
  }

  // ── WriteControlSourceMap ────────────────────────────────────────────

  [Fact]
  public void WriteControlSourceMap_CreatesCsvFile()
  {
    var maps = new List<ControlSourceMap>
    {
      new() { ControlKey = "V-1", VulnId = "V-1", RuleId = "SV-1", Title = "Test", SourcesKey = "SCAP", IsClosed = true }
    };

    var csvPath = Path.Combine(_tempDir, "sources.csv");
    VerifyReportWriter.WriteControlSourceMap(csvPath, maps);

    File.Exists(csvPath).Should().BeTrue();
    var content = File.ReadAllText(csvPath);
    content.Should().Contain("V-1");
  }

  // ── VerifyReportReader ───────────────────────────────────────────────

  [Fact]
  public void LoadFromJson_RoundTrip_PreservesData()
  {
    var report = new VerifyReport
    {
      Tool = "RoundTrip",
      ToolVersion = "2.0",
      StartedAt = DateTimeOffset.UtcNow,
      FinishedAt = DateTimeOffset.UtcNow,
      OutputRoot = _tempDir,
      Results = new List<ControlResult>
      {
        new() { VulnId = "V-99", RuleId = "SV-99", Status = "Open", Tool = "RoundTrip" }
      }
    };

    var path = Path.Combine(_tempDir, "roundtrip.json");
    VerifyReportWriter.WriteJson(path, report);
    var loaded = VerifyReportReader.LoadFromJson(path);

    loaded.Tool.Should().Be("RoundTrip");
    loaded.Results.Should().HaveCount(1);
    loaded.Results[0].VulnId.Should().Be("V-99");
  }
}
