using System.Xml;
using System.Xml.Linq;

namespace STIGForge.Verify.Adapters;

/// <summary>
/// Adapter for DISA STIG Viewer .ckl checklist files.
/// Parses manual review checklists into normalized verify results.
/// </summary>
public sealed class CklAdapter : IVerifyResultAdapter
{
  public string ToolName => "Manual CKL";

  public bool CanHandle(string filePath)
  {
    if (!File.Exists(filePath))
      return false;

    var ext = Path.GetExtension(filePath);
    if (!ext.Equals(".ckl", StringComparison.OrdinalIgnoreCase))
      return false;

    try
    {
      var doc = LoadSecureXml(filePath);
      return doc.Root?.Name.LocalName == "CHECKLIST";
    }
    catch
    {
      return false;
    }
  }

  public NormalizedVerifyReport ParseResults(string outputPath)
  {
    if (!File.Exists(outputPath))
      throw new FileNotFoundException("CKL file not found", outputPath);

    var doc = LoadSecureXml(outputPath);
    var vulnNodes = doc.Descendants("VULN").ToList();
    var results = new List<NormalizedVerifyResult>(vulnNodes.Count);
    var diagnostics = new List<string>();

    // Extract metadata from CHECKLIST header
    var stigInfoElement = doc.Descendants("STIG_INFO").FirstOrDefault();

    var toolVersion = ExtractStigInfoValue(stigInfoElement, "version") ?? "unknown";
    foreach (var vuln in vulnNodes)
    {
      try
      {
        var result = ParseVulnNode(vuln, outputPath);
        results.Add(result);
      }
      catch (Exception ex)
      {
        diagnostics.Add($"Failed to parse VULN node: {ex.Message}");
      }
    }

    var fileTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);

    var summary = CalculateSummary(results);

    return new NormalizedVerifyReport
    {
      Tool = ToolName,
      ToolVersion = toolVersion,
      StartedAt = fileTimestamp, // CKL doesn't have explicit timestamps
      FinishedAt = fileTimestamp,
      OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
      Results = results,
      Summary = summary,
      DiagnosticMessages = diagnostics
    };
  }

  private NormalizedVerifyResult ParseVulnNode(XElement vuln, string sourcePath)
  {
    var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var sd in vuln.Elements("STIG_DATA"))
    {
      var key = sd.Element("VULN_ATTRIBUTE")?.Value?.Trim() ?? string.Empty;
      var val = sd.Element("ATTRIBUTE_DATA")?.Value?.Trim() ?? string.Empty;
      if (key.Length > 0)
        data[key] = val;
    }

    var statusText = vuln.Element("STATUS")?.Value?.Trim();
    var finding = vuln.Element("FINDING_DETAILS")?.Value?.Trim();
    var comments = vuln.Element("COMMENTS")?.Value?.Trim();
    var severityOverride = vuln.Element("SEVERITY_OVERRIDE")?.Value?.Trim();
    var severityJustification = vuln.Element("SEVERITY_JUSTIFICATION")?.Value?.Trim();

    var vulnId = GetValue(data, "Vuln_Num");
    var ruleId = GetValue(data, "Rule_ID");
    var title = GetValue(data, "Rule_Title") ?? GetValue(data, "Vuln_Title");
    var severity = severityOverride ?? GetValue(data, "Severity");

    var metadata = new Dictionary<string, string>();
    if (!string.IsNullOrWhiteSpace(severityOverride))
      metadata["severity_override"] = severityOverride!;
    if (!string.IsNullOrWhiteSpace(severityJustification))
      metadata["severity_justification"] = severityJustification!;

    // Add all STIG_DATA to metadata for forensics
    foreach (var kvp in data)
      metadata[$"ckl_{kvp.Key}"] = kvp.Value;

    return new NormalizedVerifyResult
    {
      ControlId = vulnId ?? ruleId ?? "unknown",
      VulnId = vulnId,
      RuleId = ruleId,
      Title = title,
      Severity = severity,
      Status = MapCklStatus(statusText),
      FindingDetails = finding,
      Comments = comments,
      Tool = ToolName,
      SourceFile = sourcePath,
      VerifiedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(sourcePath), TimeSpan.Zero),
      EvidencePaths = Array.Empty<string>(),
      Metadata = metadata
    };
  }

  private static VerifyStatus MapCklStatus(string? cklStatus)
  {
    if (string.IsNullOrWhiteSpace(cklStatus))
      return VerifyStatus.NotReviewed;

    var normalized = (cklStatus ?? string.Empty)
      .Trim()
      .Replace("_", string.Empty)
      .Replace("-", string.Empty)
      .Replace(" ", string.Empty)
      .ToLowerInvariant();

    return normalized switch
    {
      "notafinding" => VerifyStatus.Pass,
      "pass" => VerifyStatus.Pass,
      "open" => VerifyStatus.Fail,
      "fail" => VerifyStatus.Fail,
      "notapplicable" => VerifyStatus.NotApplicable,
      "na" => VerifyStatus.NotApplicable,
      "notreviewed" => VerifyStatus.NotReviewed,
      "notchecked" => VerifyStatus.NotReviewed,
      "informational" => VerifyStatus.Informational,
      "error" => VerifyStatus.Error,
      "unknown" => VerifyStatus.Unknown,
      _ => VerifyStatus.Unknown
    };
  }

  private static string? GetValue(IReadOnlyDictionary<string, string> dict, string key)
  {
    return dict.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : null;
  }

  private static string? ExtractStigInfoValue(XElement? stigInfo, string attributeName)
  {
    if (stigInfo == null)
      return null;

    var dataElements = stigInfo.Elements("SI_DATA");
    foreach (var data in dataElements)
    {
      var name = data.Element("SID_NAME")?.Value?.Trim();
      if (name?.Equals(attributeName, StringComparison.OrdinalIgnoreCase) == true)
        return data.Element("SID_DATA")?.Value?.Trim();
    }

    return null;
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

  private static XDocument LoadSecureXml(string filePath)
  {
    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreWhitespace = true,
      MaxCharactersFromEntities = 1024,
      MaxCharactersInDocument = 20_000_000,
      Async = false
    };

    try
    {
      using var reader = XmlReader.Create(filePath, settings);
      return XDocument.Load(reader, LoadOptions.None);
    }
    catch (XmlException ex)
    {
      throw new InvalidDataException($"[VERIFY-CKL-XML-001] Failed to parse CKL XML '{filePath}': {ex.Message}", ex);
    }
  }
}
