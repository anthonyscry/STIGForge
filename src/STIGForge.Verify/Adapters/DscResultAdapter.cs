using System.Text.Json;
using System.Text.RegularExpressions;

namespace STIGForge.Verify.Adapters;

public sealed class DscResultAdapter : IVerifyResultAdapter
{
  private static readonly Regex VulnIdPattern = new(@"\]V-(\d+)", RegexOptions.Compiled);

  public string ToolName => "PowerSTIG-DSC";

  public bool CanHandle(string filePath)
  {
    if (!File.Exists(filePath))
      return false;

    return Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
           && filePath.EndsWith(".dsc-test.json", StringComparison.OrdinalIgnoreCase);
  }

  public NormalizedVerifyReport ParseResults(string outputPath)
  {
    if (!File.Exists(outputPath))
      throw new FileNotFoundException("DSC test result file not found", outputPath);

    var json = File.ReadAllText(outputPath);
    var diagnostics = new List<string>();

    DscTestResult? testResult;
    try
    {
      testResult = JsonSerializer.Deserialize<DscTestResult>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });
    }
    catch (JsonException ex)
    {
      diagnostics.Add($"Failed to parse DSC test JSON: {ex.Message}");
      return CreateEmptyReport(outputPath, diagnostics);
    }

    if (testResult == null)
    {
      diagnostics.Add("DSC test JSON deserialized to null.");
      return CreateEmptyReport(outputPath, diagnostics);
    }

    var fileTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);
    var results = new List<NormalizedVerifyResult>();

    if (testResult.ResourcesInDesiredState != null)
    {
      foreach (var resource in testResult.ResourcesInDesiredState)
        results.Add(MapResource(resource, VerifyStatus.Pass, outputPath, fileTimestamp));
    }

    if (testResult.ResourcesNotInDesiredState != null)
    {
      foreach (var resource in testResult.ResourcesNotInDesiredState)
        results.Add(MapResource(resource, VerifyStatus.Fail, outputPath, fileTimestamp));
    }

    if (results.Count == 0)
      diagnostics.Add("No DSC resources found in test output.");

    return new NormalizedVerifyReport
    {
      Tool = ToolName,
      ToolVersion = "DSC",
      StartedAt = fileTimestamp,
      FinishedAt = fileTimestamp,
      OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
      Results = results,
      Summary = CalculateSummary(results),
      DiagnosticMessages = diagnostics
    };
  }

  private NormalizedVerifyResult MapResource(DscResource resource, VerifyStatus status, string sourcePath, DateTimeOffset verifiedAt)
  {
    var vulnId = ExtractVulnId(resource.ResourceId);

    return new NormalizedVerifyResult
    {
      ControlId = vulnId ?? resource.ResourceId ?? "unknown",
      VulnId = vulnId,
      RuleId = null,
      Title = resource.ResourceId,
      Severity = null,
      Status = status,
      FindingDetails = status == VerifyStatus.Fail
        ? $"Resource '{resource.ResourceId}' is not in desired state."
        : null,
      Comments = null,
      Tool = ToolName,
      SourceFile = sourcePath,
      VerifiedAt = verifiedAt,
      EvidencePaths = Array.Empty<string>(),
      Metadata = BuildMetadata(resource)
    };
  }

  private static string? ExtractVulnId(string? resourceId)
  {
    if (string.IsNullOrWhiteSpace(resourceId))
      return null;

    var match = VulnIdPattern.Match(resourceId);
    return match.Success ? "V-" + match.Groups[1].Value : null;
  }

  private static Dictionary<string, string> BuildMetadata(DscResource resource)
  {
    var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(resource.ResourceName))
      metadata["dsc_resource_name"] = resource.ResourceName!;

    if (!string.IsNullOrWhiteSpace(resource.ModuleName))
      metadata["dsc_module_name"] = resource.ModuleName!;

    if (!string.IsNullOrWhiteSpace(resource.ModuleVersion))
      metadata["dsc_module_version"] = resource.ModuleVersion!;

    if (!string.IsNullOrWhiteSpace(resource.ResourceId))
      metadata["dsc_resource_id"] = resource.ResourceId!;

    return metadata;
  }

  private static VerifySummary CalculateSummary(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var summary = new VerifySummary { TotalCount = results.Count };

    foreach (var result in results)
    {
      switch (result.Status)
      {
        case VerifyStatus.Pass: summary.PassCount++; break;
        case VerifyStatus.Fail: summary.FailCount++; break;
        case VerifyStatus.NotApplicable: summary.NotApplicableCount++; break;
        case VerifyStatus.NotReviewed: summary.NotReviewedCount++; break;
        case VerifyStatus.Informational: summary.InformationalCount++; break;
        case VerifyStatus.Error: summary.ErrorCount++; break;
      }
    }

    var evaluatedCount = summary.PassCount + summary.FailCount + summary.ErrorCount;
    summary.CompliancePercent = evaluatedCount > 0
      ? (summary.PassCount / (double)evaluatedCount) * 100.0
      : 0.0;

    return summary;
  }

  private NormalizedVerifyReport CreateEmptyReport(string outputPath, List<string> diagnostics)
  {
    var timestamp = File.Exists(outputPath)
      ? new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero)
      : DateTimeOffset.Now;

    return new NormalizedVerifyReport
    {
      Tool = ToolName,
      ToolVersion = "DSC",
      StartedAt = timestamp,
      FinishedAt = timestamp,
      OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
      Results = Array.Empty<NormalizedVerifyResult>(),
      Summary = new VerifySummary(),
      DiagnosticMessages = diagnostics
    };
  }

  private sealed class DscTestResult
  {
    public bool InDesiredState { get; set; }
    public List<DscResource>? ResourcesInDesiredState { get; set; }
    public List<DscResource>? ResourcesNotInDesiredState { get; set; }
  }

  private sealed class DscResource
  {
    public string? ResourceId { get; set; }
    public string? ResourceName { get; set; }
    public string? ModuleName { get; set; }
    public string? ModuleVersion { get; set; }
    public bool InDesiredState { get; set; }
  }
}
