using System.Text.Json;
using STIGForge.Export;
using Xunit;

namespace STIGForge.UnitTests.Export;

public class FleetSummaryServiceTests
{
  [Fact]
  public void Summary_AggregatesPerHostCompliance()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_sum_" + Guid.NewGuid().ToString("N"));
    try
    {
      // host-a: 8 pass, 2 fail = 80%
      CreateHostWithControls(tempDir, "host-a",
        Enumerable.Range(1, 8).Select(i => ($"V-{1000 + i}", "Pass")).Concat(
        Enumerable.Range(9, 2).Select(i => ($"V-{1000 + i}", "Open"))).ToArray());

      // host-b: 6 pass, 4 fail = 60%
      CreateHostWithControls(tempDir, "host-b",
        Enumerable.Range(1, 6).Select(i => ($"V-{1000 + i}", "Pass")).Concat(
        Enumerable.Range(7, 4).Select(i => ($"V-{1000 + i}", "Open"))).ToArray());

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(tempDir);

      Assert.Equal(2, summary.PerHostStats.Count);

      var hostA = summary.PerHostStats.First(h => h.HostName == "host-a");
      Assert.Equal(80.0, hostA.CompliancePercentage);
      Assert.Equal(8, hostA.PassCount);
      Assert.Equal(2, hostA.FailCount);

      var hostB = summary.PerHostStats.First(h => h.HostName == "host-b");
      Assert.Equal(60.0, hostB.CompliancePercentage);
      Assert.Equal(6, hostB.PassCount);
      Assert.Equal(4, hostB.FailCount);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void ControlStatusMatrix_RowsAreControls_ColumnsAreHosts()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_matrix_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateHostWithControls(tempDir, "host-a", new[] { ("V-1001", "Pass"), ("V-1002", "Open") });
      CreateHostWithControls(tempDir, "host-b", new[] { ("V-1001", "Open"), ("V-1002", "Pass") });
      CreateHostWithControls(tempDir, "host-c", new[] { ("V-1001", "Pass"), ("V-1003", "Open") });

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(tempDir);

      // V-1001 is on all 3 hosts
      Assert.True(summary.ControlStatusMatrix.ContainsKey("V-1001"));
      Assert.Equal("Pass", summary.ControlStatusMatrix["V-1001"]["host-a"]);
      Assert.Equal("Fail", summary.ControlStatusMatrix["V-1001"]["host-b"]);
      Assert.Equal("Pass", summary.ControlStatusMatrix["V-1001"]["host-c"]);

      // V-1002 is on host-a and host-b
      Assert.True(summary.ControlStatusMatrix.ContainsKey("V-1002"));
      Assert.Equal("Fail", summary.ControlStatusMatrix["V-1002"]["host-a"]);
      Assert.Equal("Pass", summary.ControlStatusMatrix["V-1002"]["host-b"]);

      // V-1003 is on host-c only
      Assert.True(summary.ControlStatusMatrix.ContainsKey("V-1003"));
      Assert.Equal("Fail", summary.ControlStatusMatrix["V-1003"]["host-c"]);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void ControlStatusMatrix_SortedByControlId()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_sort_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateHostWithControls(tempDir, "host-a", new[]
      {
        ("V-2003", "Pass"), ("V-1001", "Pass"), ("V-3002", "Open")
      });

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(tempDir);

      var keys = summary.ControlStatusMatrix.Keys.ToList();
      var sorted = keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
      Assert.Equal(sorted, keys);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FleetPoam_IncludesHostColumn()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_poam_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateHostWithControls(tempDir, "host-a", new[] { ("V-1234", "Open"), ("V-1235", "Pass") });
      CreateHostWithControls(tempDir, "host-b", new[] { ("V-1234", "Open"), ("V-1235", "Open") });
      CreateHostWithControls(tempDir, "host-c", new[] { ("V-1234", "Pass"), ("V-1235", "Pass") });

      var svc = new FleetSummaryService();
      var poam = svc.GenerateFleetPoam(tempDir, "TestFleet");

      // V-1234 fails on host-a and host-b
      var item1234 = poam.Items.First(i => i.ControlId == "V-1234");
      Assert.Equal("host-a,host-b", item1234.HostsAffected);

      // V-1235 fails on host-b only
      var item1235 = poam.Items.First(i => i.ControlId == "V-1235");
      Assert.Equal("host-b", item1235.HostsAffected);

      Assert.Equal(2, poam.Items.Count);
      Assert.Equal("TestFleet", poam.Summary.SystemName);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void WriteSummary_CreatesAllThreeFormats()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_write_" + Guid.NewGuid().ToString("N"));
    var outputDir = Path.Combine(tempDir, "output");
    try
    {
      CreateHostWithControls(tempDir, "host-a", new[] { ("V-1001", "Pass") });

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(tempDir);
      svc.WriteSummaryFiles(summary, outputDir);

      Assert.True(File.Exists(Path.Combine(outputDir, "fleet_summary.json")));
      Assert.True(File.Exists(Path.Combine(outputDir, "fleet_summary.csv")));
      Assert.True(File.Exists(Path.Combine(outputDir, "fleet_summary.txt")));

      // Verify CSV has correct structure
      var csvLines = File.ReadAllLines(Path.Combine(outputDir, "fleet_summary.csv"));
      Assert.Contains("ControlId", csvLines[0]);
      Assert.Contains("host-a", csvLines[0]);

      // Verify TXT has expected sections
      var txt = File.ReadAllText(Path.Combine(outputDir, "fleet_summary.txt"));
      Assert.Contains("FLEET COMPLIANCE SUMMARY", txt);
      Assert.Contains("Per-Host Compliance", txt);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void FleetWideCompliance_CalculatedCorrectly()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_wide_" + Guid.NewGuid().ToString("N"));
    try
    {
      // host-a: 8 pass, 2 fail
      // host-b: 6 pass, 4 fail
      // Fleet-wide: 14 pass / 20 applicable = 70%
      CreateHostWithControls(tempDir, "host-a",
        Enumerable.Range(1, 8).Select(i => ($"V-{1000 + i}", "Pass")).Concat(
        Enumerable.Range(9, 2).Select(i => ($"V-{1000 + i}", "Open"))).ToArray());

      CreateHostWithControls(tempDir, "host-b",
        Enumerable.Range(1, 6).Select(i => ($"V-{2000 + i}", "Pass")).Concat(
        Enumerable.Range(7, 4).Select(i => ($"V-{2000 + i}", "Open"))).ToArray());

      var svc = new FleetSummaryService();
      var summary = svc.GenerateSummary(tempDir);

      Assert.Equal(70.0, summary.FleetWideCompliance);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  private static void CreateHostWithControls(string root, string hostName, (string vulnId, string status)[] controls)
  {
    var verifyDir = Path.Combine(root, hostName, "Verify");
    Directory.CreateDirectory(verifyDir);

    var results = controls.Select(c => new
    {
      vulnId = c.vulnId,
      ruleId = "SV-" + c.vulnId.Replace("V-", "") + "r1_rule",
      title = "Test " + c.vulnId,
      severity = "medium",
      status = c.status,
      findingDetails = "",
      comments = "",
      tool = "test",
      sourceFile = "test.json"
    }).ToArray();

    var report = new
    {
      tool = "test",
      toolVersion = "1.0",
      startedAt = DateTimeOffset.Now,
      finishedAt = DateTimeOffset.Now,
      outputRoot = verifyDir,
      results
    };

    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"),
      JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
  }
}
