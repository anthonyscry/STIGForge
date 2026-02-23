using System.Text.Json;
using STIGForge.Export;
using STIGForge.Infrastructure.System;
using Xunit;

namespace STIGForge.UnitTests.Infrastructure;

public class FleetArtifactCollectionTests
{
  [Fact]
  public void GeneratePerHostCkl_CreatesExportPerHost()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_ckl_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateHostWithResults(tempDir, "host-a", new[]
      {
        ("V-1001", "Pass", "Test Rule 1"),
        ("V-1002", "Open", "Test Rule 2")
      });
      CreateHostWithResults(tempDir, "host-b", new[]
      {
        ("V-1001", "NotAFinding", "Test Rule 1"),
        ("V-1003", "Open", "Test Rule 3")
      });

      FleetSummaryService.GeneratePerHostCkl(tempDir);

      Assert.True(Directory.Exists(Path.Combine(tempDir, "host-a", "Export")));
      Assert.True(Directory.Exists(Path.Combine(tempDir, "host-b", "Export")));
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void GeneratePerHostCkl_SkipsHostsWithoutVerifyResults()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "fleet_ckl_skip_" + Guid.NewGuid().ToString("N"));
    try
    {
      CreateHostWithResults(tempDir, "host-a", new[]
      {
        ("V-1001", "Pass", "Test Rule 1")
      });

      // host-b has no Verify directory
      Directory.CreateDirectory(Path.Combine(tempDir, "host-b"));

      FleetSummaryService.GeneratePerHostCkl(tempDir);

      Assert.True(Directory.Exists(Path.Combine(tempDir, "host-a", "Export")));
      Assert.False(Directory.Exists(Path.Combine(tempDir, "host-b", "Export")));
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void CollectionRequest_ModelProperties()
  {
    var request = new FleetCollectionRequest();
    Assert.Equal(5, request.MaxConcurrency);
    Assert.Equal(600, request.TimeoutSeconds);
    Assert.Empty(request.Targets);
    Assert.Equal(string.Empty, request.LocalResultsRoot);
  }

  [Fact]
  public void CollectionResult_ModelProperties()
  {
    var result = new FleetCollectionResult();
    Assert.Equal(0, result.TotalHosts);
    Assert.Equal(0, result.SuccessCount);
    Assert.Equal(0, result.FailureCount);
    Assert.Empty(result.HostResults);
  }

  private static void CreateHostWithResults(string root, string hostName, (string vulnId, string status, string title)[] controls)
  {
    var verifyDir = Path.Combine(root, hostName, "Verify");
    Directory.CreateDirectory(verifyDir);

    var results = controls.Select(c => new
    {
      vulnId = c.vulnId,
      ruleId = "SV-" + c.vulnId.Replace("V-", "") + "r1_rule",
      title = c.title,
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
