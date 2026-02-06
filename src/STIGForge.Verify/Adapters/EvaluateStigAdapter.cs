using System.Xml.Linq;

namespace STIGForge.Verify.Adapters;

/// <summary>
/// Adapter for Evaluate-STIG PowerShell module XML output.
/// Parses Evaluate-STIG result files from automated PowerShell-based verification.
/// </summary>
public sealed class EvaluateStigAdapter : IVerifyResultAdapter
{
  public string ToolName => "Evaluate-STIG";

  public bool CanHandle(string filePath)
  {
    if (!File.Exists(filePath))
      return false;

    var ext = Path.GetExtension(filePath);
    if (!ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
      return false;

    try
    {
      var doc = XDocument.Load(filePath);
      var root = doc.Root;

      // Evaluate-STIG uses custom XML format with STIGChecks root or similar
      // Check for characteristic elements like "STIGCheck", "Finding", etc.
      return root?.Name.LocalName == "STIGChecks"
             || root?.Descendants("STIGCheck").Any() == true
             || root?.Descendants("Finding").Any() == true;
    }
    catch
    {
      return false;
    }
  }

  public NormalizedVerifyReport ParseResults(string outputPath)
  {
    if (!File.Exists(outputPath))
      throw new FileNotFoundException("Evaluate-STIG result file not found", outputPath);

    var doc = XDocument.Load(outputPath);
    var diagnostics = new List<string>();

    // Evaluate-STIG format varies - try multiple structures
    var root = doc.Root;
    if (root == null)
    {
      diagnostics.Add("No root element found in Evaluate-STIG output");
      return CreateEmptyReport(outputPath, diagnostics);
    }

    // Extract metadata from root attributes/elements
    var toolVersion = root.Attribute("Version")?.Value ?? root.Element("Version")?.Value ?? "unknown";
    var startTimeStr = root.Attribute("StartTime")?.Value ?? root.Element("StartTime")?.Value;
    var endTimeStr = root.Attribute("EndTime")?.Value ?? root.Element("EndTime")?.Value;

    var fileTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);
    var startTime = DateTimeOffset.TryParse(startTimeStr, out var st) ? st : fileTimestamp;
    var endTime = DateTimeOffset.TryParse(endTimeStr, out var et) ? et : fileTimestamp;

    // Try multiple element names for findings
    var checkElements = root.Descendants("STIGCheck")
      .Concat(root.Descendants("Finding"))
      .Concat(root.Descendants("Check"))
      .ToList();

    if (checkElements.Count == 0)
    {
      diagnostics.Add("No check/finding elements found in Evaluate-STIG output");
      return CreateEmptyReport(outputPath, diagnostics, toolVersion, startTime, endTime);
    }

    var results = new List<NormalizedVerifyResult>(checkElements.Count);

    foreach (var check in checkElements)
    {
      try
      {
        var result = ParseCheckElement(check, outputPath, endTime);
        results.Add(result);
      }
      catch (Exception ex)
      {
        diagnostics.Add($"Failed to parse check element: {ex.Message}");
      }
    }

    var summary = CalculateSummary(results);

    return new NormalizedVerifyReport
    {
      Tool = ToolName,
      ToolVersion = toolVersion,
      StartedAt = startTime,
      FinishedAt = endTime,
      OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
      Results = results,
      Summary = summary,
      DiagnosticMessages = diagnostics
    };
  }

  private NormalizedVerifyResult ParseCheckElement(XElement check, string sourcePath, DateTimeOffset verifiedAt)
  {
    // Try common attribute/element names
    var vulnId = check.Attribute("VulnID")?.Value ?? check.Element("VulnID")?.Value ?? check.Attribute("ID")?.Value;
    var ruleId = check.Attribute("RuleID")?.Value ?? check.Element("RuleID")?.Value;
    var title = check.Attribute("Title")?.Value ?? check.Element("Title")?.Value;
    var severity = check.Attribute("Severity")?.Value ?? check.Element("Severity")?.Value;
    var statusText = check.Attribute("Status")?.Value ?? check.Element("Status")?.Value ?? check.Attribute("Result")?.Value ?? check.Element("Result")?.Value;
    var findingDetails = check.Element("FindingDetails")?.Value?.Trim() ?? check.Element("Details")?.Value?.Trim();
    var comments = check.Element("Comments")?.Value?.Trim();

    // Extract metadata
    var metadata = new Dictionary<string, string>();
    var testId = check.Attribute("TestID")?.Value ?? check.Element("TestID")?.Value;
    if (!string.IsNullOrWhiteSpace(testId))
      metadata["test_id"] = testId;

    var checkContent = check.Element("CheckContent")?.Value?.Trim();
    if (!string.IsNullOrWhiteSpace(checkContent))
      metadata["check_content"] = checkContent;

    var fixText = check.Element("FixText")?.Value?.Trim();
    if (!string.IsNullOrWhiteSpace(fixText))
      metadata["fix_text"] = fixText;

    return new NormalizedVerifyResult
    {
      ControlId = vulnId ?? ruleId ?? "unknown",
      VulnId = vulnId,
      RuleId = ruleId,
      Title = title,
      Severity = NormalizeSeverity(severity),
      Status = MapEvaluateStigStatus(statusText),
      FindingDetails = findingDetails,
      Comments = comments,
      Tool = ToolName,
      SourceFile = sourcePath,
      VerifiedAt = verifiedAt,
      EvidencePaths = Array.Empty<string>(),
      Metadata = metadata
    };
  }

  private static VerifyStatus MapEvaluateStigStatus(string? stigStatus)
  {
    if (string.IsNullOrWhiteSpace(stigStatus))
      return VerifyStatus.NotReviewed;

    var normalized = stigStatus.Replace("_", "").Replace("-", "").ToLowerInvariant();

    return normalized switch
    {
      "compliant" => VerifyStatus.Pass,
      "pass" => VerifyStatus.Pass,
      "noncompliant" => VerifyStatus.Fail,
      "fail" => VerifyStatus.Fail,
      "open" => VerifyStatus.Fail,
      "notapplicable" => VerifyStatus.NotApplicable,
      "na" => VerifyStatus.NotApplicable,
      "notreviewed" => VerifyStatus.NotReviewed,
      "informational" => VerifyStatus.Informational,
      "error" => VerifyStatus.Error,
      _ => VerifyStatus.Unknown
    };
  }

  private static string? NormalizeSeverity(string? severity)
  {
    if (string.IsNullOrWhiteSpace(severity))
      return null;

    var normalized = severity.ToLowerInvariant();
    return normalized switch
    {
      "cat i" => "high",
      "cati" => "high",
      "high" => "high",
      "cat ii" => "medium",
      "catii" => "medium",
      "medium" => "medium",
      "cat iii" => "low",
      "catiii" => "low",
      "low" => "low",
      _ => severity
    };
  }

  private static VerifySummary CalculateSummary(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var summary = new VerifySummary
    {
      TotalCount = results.Count
    };

    foreach (var result in results)
    {
      switch (result.Status)
      {
        case VerifyStatus.Pass:
          summary.PassCount++;
          break;
        case VerifyStatus.Fail:
          summary.FailCount++;
          break;
        case VerifyStatus.NotApplicable:
          summary.NotApplicableCount++;
          break;
        case VerifyStatus.NotReviewed:
          summary.NotReviewedCount++;
          break;
        case VerifyStatus.Informational:
          summary.InformationalCount++;
          break;
        case VerifyStatus.Error:
          summary.ErrorCount++;
          break;
      }
    }

    var evaluatedCount = summary.PassCount + summary.FailCount + summary.ErrorCount;
    summary.CompliancePercent = evaluatedCount > 0
      ? (summary.PassCount / (double)evaluatedCount) * 100.0
      : 0.0;

    return summary;
  }

  private NormalizedVerifyReport CreateEmptyReport(string outputPath, List<string> diagnostics, string toolVersion = "unknown", DateTimeOffset? startTime = null, DateTimeOffset? endTime = null)
  {
    var timestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);
    return new NormalizedVerifyReport
    {
      Tool = ToolName,
      ToolVersion = toolVersion,
      StartedAt = startTime ?? timestamp,
      FinishedAt = endTime ?? timestamp,
      OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
      Results = Array.Empty<NormalizedVerifyResult>(),
      Summary = new VerifySummary(),
      DiagnosticMessages = diagnostics
    };
  }
}
