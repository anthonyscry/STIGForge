using System.Xml.Linq;

namespace STIGForge.Verify.Adapters;

/// <summary>
/// Adapter for SCAP Compliance Checker (SCC) XCCDF results.
/// Parses XCCDF result files from automated SCAP scans.
/// </summary>
public sealed class ScapResultAdapter : IVerifyResultAdapter
{
  private static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2";

  public string ToolName => "SCAP";

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

      // SCAP results use XCCDF namespace with TestResult element
      return root?.Name.LocalName == "Benchmark" && root.Name.Namespace == XccdfNs
             || root?.Name.LocalName == "TestResult" && root.Name.Namespace == XccdfNs;
    }
    catch
    {
      return false;
    }
  }

  public NormalizedVerifyReport ParseResults(string outputPath)
  {
    if (!File.Exists(outputPath))
      throw new FileNotFoundException("SCAP result file not found", outputPath);

    var doc = XDocument.Load(outputPath);
    var diagnostics = new List<string>();

    // SCAP results can be in Benchmark/TestResult or standalone TestResult
    var testResult = doc.Descendants(XccdfNs + "TestResult").FirstOrDefault();
    if (testResult == null)
    {
      diagnostics.Add("No TestResult element found in SCAP output");
      return new NormalizedVerifyReport
      {
        Tool = ToolName,
        ToolVersion = "unknown",
        StartedAt = DateTimeOffset.Now,
        FinishedAt = DateTimeOffset.Now,
        OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
        Results = Array.Empty<NormalizedVerifyResult>(),
        Summary = new VerifySummary(),
        DiagnosticMessages = diagnostics
      };
    }

    var toolVersion = testResult.Attribute("version")?.Value ?? "unknown";
    var startTimeStr = testResult.Element(XccdfNs + "start-time")?.Value;
    var endTimeStr = testResult.Element(XccdfNs + "end-time")?.Value;

    var startTime = DateTimeOffset.TryParse(startTimeStr, out var st) ? st : new DateTimeOffset(File.GetCreationTimeUtc(outputPath), TimeSpan.Zero);
    var endTime = DateTimeOffset.TryParse(endTimeStr, out var et) ? et : new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);

    var ruleResults = testResult.Elements(XccdfNs + "rule-result").ToList();
    var results = new List<NormalizedVerifyResult>(ruleResults.Count);

    foreach (var ruleResult in ruleResults)
    {
      try
      {
        var result = ParseRuleResult(ruleResult, outputPath, endTime);
        results.Add(result);
      }
      catch (Exception ex)
      {
        diagnostics.Add($"Failed to parse rule-result: {ex.Message}");
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

  private NormalizedVerifyResult ParseRuleResult(XElement ruleResult, string sourcePath, DateTimeOffset verifiedAt)
  {
    var ruleIdAttr = ruleResult.Attribute("idref")?.Value;
    var timeAttr = ruleResult.Attribute("time")?.Value;
    var resultElement = ruleResult.Element(XccdfNs + "result");
    var statusText = resultElement?.Value?.Trim();

    // Extract VulnId and Title from check-content-ref or ident elements
    var identElements = ruleResult.Elements(XccdfNs + "ident").ToList();
    var vulnId = identElements.FirstOrDefault(i => i.Attribute("system")?.Value?.Contains("cce") == false)?.Value?.Trim();
    var cceId = identElements.FirstOrDefault(i => i.Attribute("system")?.Value?.Contains("cce") == true)?.Value?.Trim();

    var checkElement = ruleResult.Element(XccdfNs + "check");
    var checkContentRef = checkElement?.Element(XccdfNs + "check-content-ref");
    var checkHref = checkContentRef?.Attribute("href")?.Value;
    var checkName = checkContentRef?.Attribute("name")?.Value;

    var metadata = new Dictionary<string, string>();
    if (!string.IsNullOrWhiteSpace(ruleIdAttr))
      metadata["rule_id"] = ruleIdAttr;
    if (!string.IsNullOrWhiteSpace(cceId))
      metadata["cce_id"] = cceId;
    if (!string.IsNullOrWhiteSpace(checkHref))
      metadata["check_href"] = checkHref;
    if (!string.IsNullOrWhiteSpace(checkName))
      metadata["check_name"] = checkName;
    if (!string.IsNullOrWhiteSpace(timeAttr))
      metadata["check_time"] = timeAttr;

    // Extract severity from weight attribute if present
    var weight = ruleResult.Attribute("weight")?.Value;
    var severity = weight switch
    {
      "10.0" => "high",
      "5.0" => "medium",
      "1.0" => "low",
      _ => null
    };

    // Extract finding details from message elements
    var messages = ruleResult.Elements(XccdfNs + "message").Select(m => m.Value?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
    var findingDetails = messages.Count > 0 ? string.Join("\n", messages) : null;

    return new NormalizedVerifyResult
    {
      ControlId = vulnId ?? ruleIdAttr ?? "unknown",
      VulnId = vulnId,
      RuleId = ruleIdAttr,
      Title = checkName, // SCAP doesn't always include full title in results
      Severity = severity,
      Status = MapScapStatus(statusText),
      FindingDetails = findingDetails,
      Comments = null,
      Tool = ToolName,
      SourceFile = sourcePath,
      VerifiedAt = verifiedAt,
      EvidencePaths = Array.Empty<string>(),
      Metadata = metadata
    };
  }

  private static VerifyStatus MapScapStatus(string? scapStatus)
  {
    if (string.IsNullOrWhiteSpace(scapStatus))
      return VerifyStatus.Unknown;

    return scapStatus.ToLowerInvariant() switch
    {
      "pass" => VerifyStatus.Pass,
      "fail" => VerifyStatus.Fail,
      "notapplicable" => VerifyStatus.NotApplicable,
      "notchecked" => VerifyStatus.NotReviewed,
      "notselected" => VerifyStatus.NotReviewed,
      "informational" => VerifyStatus.Informational,
      "error" => VerifyStatus.Error,
      "unknown" => VerifyStatus.Unknown,
      _ => VerifyStatus.Unknown
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
}
