using System.Xml.Linq;
using System.Globalization;
using System.Xml;
using PackTypes = STIGForge.Core.Constants.PackTypes;

namespace STIGForge.Verify.Adapters;

/// <summary>
/// Adapter for SCAP Compliance Checker (SCC) XCCDF results.
/// Parses XCCDF result files from automated SCAP scans.
/// </summary>
public sealed class ScapResultAdapter : IVerifyResultAdapter
{
  private static readonly XNamespace Xccdf12Ns = "http://checklists.nist.gov/xccdf/1.2";
  private static readonly XNamespace Xccdf11Ns = "http://checklists.nist.gov/xccdf/1.1";
  private static readonly string[] SearchPatterns =
  {
    "*.xccdf.xml",
    "*.xccdf",
    "XCCDF_Results*.xml",
    "SCC_Results/**/*.xccdf.xml",
    "SCC_Results/**/*.xccdf",
    "SCC_Results/**/XCCDF_Results*.xml"
  };

  public string ToolName => PackTypes.Scap;

  public static IReadOnlyList<string> GetSearchPatterns()
  {
    return SearchPatterns;
  }

  public static IReadOnlyList<string> EnumerateCandidateFiles(string rootPath)
  {
    if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
      return Array.Empty<string>();

    var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    AddMatches(candidates, rootPath, "*.xccdf.xml");
    AddMatches(candidates, rootPath, "*.xccdf");
    AddMatches(candidates, rootPath, "XCCDF_Results*.xml");

    string[] sccResultsDirs;
    try
    {
      sccResultsDirs = Directory.GetDirectories(rootPath, "SCC_Results", SearchOption.AllDirectories);
    }
    catch
    {
      sccResultsDirs = Array.Empty<string>();
    }

    foreach (var sccResultsDir in sccResultsDirs)
    {
      AddMatches(candidates, sccResultsDir, "*.xccdf.xml");
      AddMatches(candidates, sccResultsDir, "*.xccdf");
      AddMatches(candidates, sccResultsDir, "XCCDF_Results*.xml");
    }

    return candidates
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public bool CanHandle(string filePath)
  {
    if (!File.Exists(filePath))
      return false;

    var ext = Path.GetExtension(filePath);
    if (!ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
        && !ext.Equals(".xccdf", StringComparison.OrdinalIgnoreCase))
      return false;

    try
    {
      var doc = LoadSecureXml(filePath);
      var root = doc.Root;
      if (root == null)
        return false;

      var ns = root.Name.Namespace;
      var localName = root.Name.LocalName;

      return (localName == "Benchmark" || localName == "TestResult")
             && (ns == Xccdf12Ns || ns == Xccdf11Ns);
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

    var doc = LoadSecureXml(outputPath);
    var diagnostics = new List<string>();

    var xccdfNs = ResolveXccdfNamespace(doc);

    var testResult = doc.Descendants(xccdfNs + "TestResult").FirstOrDefault();
    if (testResult == null)
    {
      diagnostics.Add("No TestResult element found in SCAP output");
      var fileTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);
      return new NormalizedVerifyReport
      {
        Tool = ToolName,
        ToolVersion = "unknown",
        StartedAt = fileTimestamp,
        FinishedAt = fileTimestamp,
        OutputRoot = Path.GetDirectoryName(outputPath) ?? string.Empty,
        Results = Array.Empty<NormalizedVerifyResult>(),
        Summary = new VerifySummary(),
        DiagnosticMessages = diagnostics
      };
    }

    var toolVersion = testResult.Attribute("version")?.Value ?? "unknown";
    var startTimeStr = testResult.Element(xccdfNs + "start-time")?.Value;
    var endTimeStr = testResult.Element(xccdfNs + "end-time")?.Value;

    var startFallback = new DateTimeOffset(File.GetCreationTimeUtc(outputPath), TimeSpan.Zero);
    var endFallback = new DateTimeOffset(File.GetLastWriteTimeUtc(outputPath), TimeSpan.Zero);
    var startTime = ParseTimestamp(startTimeStr, startFallback);
    var endTime = ParseTimestamp(endTimeStr, endFallback);

    var ruleResults = testResult.Elements(xccdfNs + "rule-result").ToList();
    var results = new List<NormalizedVerifyResult>(ruleResults.Count);

    foreach (var ruleResult in ruleResults)
    {
      try
      {
        var result = ParseRuleResult(ruleResult, xccdfNs, outputPath, endTime);
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

  private NormalizedVerifyResult ParseRuleResult(XElement ruleResult, XNamespace xccdfNs, string sourcePath, DateTimeOffset verifiedAt)
  {
    var ruleIdAttr = ruleResult.Attribute("idref")?.Value;
    var timeAttr = ruleResult.Attribute("time")?.Value;
    var resultElement = ruleResult.Element(xccdfNs + "result");
    var statusText = resultElement?.Value?.Trim();
    var itemVerifiedAt = ParseTimestamp(timeAttr, verifiedAt);

    var identElements = ruleResult.Elements(xccdfNs + "ident").ToList();
    var vulnId = identElements.FirstOrDefault(i => i.Attribute("system")?.Value?.Contains("cce") == false)?.Value?.Trim();
    var cceId = identElements.FirstOrDefault(i => i.Attribute("system")?.Value?.Contains("cce") == true)?.Value?.Trim();

    var checkElement = ruleResult.Element(xccdfNs + "check");
    var checkContentRef = checkElement?.Element(xccdfNs + "check-content-ref");
    var checkHref = checkContentRef?.Attribute("href")?.Value;
    var checkName = checkContentRef?.Attribute("name")?.Value;

    var metadata = new Dictionary<string, string>();
    if (!string.IsNullOrWhiteSpace(ruleIdAttr))
      metadata["rule_id"] = ruleIdAttr!;
    if (!string.IsNullOrWhiteSpace(cceId))
      metadata["cce_id"] = cceId!;
    if (!string.IsNullOrWhiteSpace(checkHref))
      metadata["check_href"] = checkHref!;
    if (!string.IsNullOrWhiteSpace(checkName))
      metadata["check_name"] = checkName!;
    if (!string.IsNullOrWhiteSpace(timeAttr))
      metadata["check_time"] = timeAttr!;

    var weight = ruleResult.Attribute("weight")?.Value;
    var severity = MapWeightToSeverity(weight);

    var messages = ruleResult.Elements(xccdfNs + "message").Select(m => m.Value?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
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
      VerifiedAt = itemVerifiedAt,
      EvidencePaths = Array.Empty<string>(),
      Metadata = metadata
    };
  }

  private static VerifyStatus MapScapStatus(string? scapStatus)
  {
    if (string.IsNullOrWhiteSpace(scapStatus))
      return VerifyStatus.Unknown;

    var normalized = scapStatus!
      .Trim()
      .Replace("_", string.Empty)
      .Replace("-", string.Empty)
      .Replace(" ", string.Empty)
      .ToLowerInvariant();

    return normalized switch
    {
      "pass" => VerifyStatus.Pass,
      "notafinding" => VerifyStatus.Pass,
      "fail" => VerifyStatus.Fail,
      "open" => VerifyStatus.Fail,
      "notapplicable" => VerifyStatus.NotApplicable,
      "na" => VerifyStatus.NotApplicable,
      "notchecked" => VerifyStatus.NotReviewed,
      "notselected" => VerifyStatus.NotReviewed,
      "notreviewed" => VerifyStatus.NotReviewed,
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

  private static string? MapWeightToSeverity(string? weight)
  {
    if (string.IsNullOrWhiteSpace(weight))
      return null;

    if (!double.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericWeight))
      return null;

    if (numericWeight >= 9.0) return "high";
    if (numericWeight >= 4.0) return "medium";
    if (numericWeight > 0.0) return "low";
    return null;
  }

  private static DateTimeOffset ParseTimestamp(string? value, DateTimeOffset fallback)
  {
    if (string.IsNullOrWhiteSpace(value))
      return fallback;

    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
      return parsed;

    if (DateTimeOffset.TryParse(value, out parsed))
      return parsed;

    return fallback;
  }

  private static XNamespace ResolveXccdfNamespace(XDocument doc)
  {
    var rootNs = doc.Root?.Name.Namespace;
    if (rootNs == Xccdf11Ns)
      return Xccdf11Ns;

    if (rootNs == Xccdf12Ns)
      return Xccdf12Ns;

    if (doc.Descendants(Xccdf12Ns + "TestResult").Any())
      return Xccdf12Ns;

    if (doc.Descendants(Xccdf11Ns + "TestResult").Any())
      return Xccdf11Ns;

    return Xccdf12Ns;
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
      throw new InvalidDataException($"[VERIFY-SCAP-XML-001] Failed to parse SCAP XML '{filePath}': {ex.Message}", ex);
    }
  }

  private static void AddMatches(HashSet<string> candidates, string rootPath, string pattern)
  {
    try
    {
      foreach (var file in Directory.GetFiles(rootPath, pattern, SearchOption.AllDirectories))
        candidates.Add(file);
    }
    catch
    {
    }
  }
}
