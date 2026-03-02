using System.Xml.Linq;
using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class CklImporter
{
  public CklChecklist Import(string cklFilePath)
  {
    if (!File.Exists(cklFilePath))
      throw new FileNotFoundException("CKL file not found", cklFilePath);

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreWhitespace = true,
      MaxCharactersFromEntities = 1024,
      MaxCharactersInDocument = 20_000_000
    };
    using var reader = XmlReader.Create(cklFilePath, settings);
    var doc = XDocument.Load(reader, LoadOptions.None);
    var root = doc.Root;
    if (root == null)
      throw new InvalidOperationException("Invalid CKL file: no root element");

    var asset = root.Element("ASSET");
    var stig = root.Element("STIGS")?.Element("iSTIG");

    var checklist = new CklChecklist
    {
      FilePath = cklFilePath,
      ImportedAt = DateTimeOffset.UtcNow,

      AssetName = GetElementValue(asset, "ASSET_NAME") ?? GetElementValue(asset, "HOST_NAME") ?? "Unknown",
      HostName = GetElementValue(asset, "HOST_NAME") ?? GetElementValue(asset, "ASSET_NAME") ?? "Unknown",
      HostIp = GetElementValue(asset, "HOST_IP") ?? string.Empty,
      HostMac = GetElementValue(asset, "HOST_MAC") ?? string.Empty,
      HostFqdn = GetElementValue(asset, "HOST_FQDN") ?? string.Empty,

      StigTitle = GetElementValue(stig?.Element("STIG_INFO"), "title") ?? "Unknown STIG",
      StigVersion = GetElementValue(stig?.Element("STIG_INFO"), "version") ?? "1",
      StigRelease = GetElementValue(stig?.Element("STIG_INFO"), "releaseinfo") ?? string.Empty,

      Findings = ParseVulnerabilities(stig?.Element("VULN")).ToList()
    };

    return checklist;
  }

  public IReadOnlyList<ControlResult> ToControlResults(CklChecklist checklist)
  {
    return checklist.Findings.Select(f => new ControlResult
    {
      RuleId = f.VulnId,
      VulnId = f.VulnId,
      Status = MapCklStatus(f.Status),
      Comments = f.Comments ?? string.Empty,
      FindingDetails = f.FindingDetails ?? string.Empty,
      Severity = f.Severity,
      Title = f.RuleTitle,
      SourceFile = checklist.FilePath
    }).ToList();
  }

  private static IEnumerable<CklFinding> ParseVulnerabilities(XElement? vulnElement)
  {
    if (vulnElement == null)
      yield break;

    var stigElement = vulnElement.Parent;
    if (stigElement == null)
      yield break;

    foreach (var vuln in stigElement.Elements("VULN"))
    {
      yield return new CklFinding
      {
        VulnId = GetStigData(vuln, "Vuln_Num") ?? string.Empty,
        RuleId = GetStigData(vuln, "Rule_Ver") ?? GetStigData(vuln, "Rule_ID") ?? string.Empty,
        RuleTitle = GetStigData(vuln, "Rule_Title") ?? string.Empty,
        Severity = MapSeverity(GetStigData(vuln, "Severity")),
        Status = GetElementValue(vuln, "STATUS") ?? "Not_Reviewed",
        Comments = GetElementValue(vuln, "COMMENTS"),
        FindingDetails = GetElementValue(vuln, "FINDING_DETAILS"),
        CheckContent = GetStigData(vuln, "Check_Content"),
        FixText = GetStigData(vuln, "Fix_Text")
      };
    }
  }

  private static string? GetElementValue(XElement? parent, string elementName)
  {
    return parent?.Element(elementName)?.Value?.Trim();
  }

  private static string? GetStigData(XElement vuln, string attributeName)
  {
    var stigData = vuln.Elements("STIG_DATA")
      .FirstOrDefault(s => s.Element("VULN_ATTRIBUTE")?.Value == attributeName);
    return stigData?.Element("ATTRIBUTE_DATA")?.Value?.Trim();
  }

  private static string MapSeverity(string? severity)
  {
    return severity?.Trim() switch
    {
      "high" or "I" => "high",
      "medium" or "II" => "medium",
      "low" or "III" => "low",
      _ => "unknown"
    };
  }

  private static string MapCklStatus(string status)
  {
    return status.ToLowerInvariant() switch
    {
      "notafinding" or "pass" => "Pass",
      "open" or "fail" => "Fail",
      "not_applicable" or "notapplicable" => "NotApplicable",
      "not_reviewed" or "notreviewed" => "NotReviewed",
      _ => "NotReviewed"
    };
  }
}

public sealed class CklExporter
{
  public void Export(CklChecklist checklist, string outputPath)
  {
    var doc = new XDocument(
      new XElement("CHECKLIST",
        new XElement("ASSET",
          new XElement("ASSET_NAME", checklist.AssetName),
          new XElement("HOST_NAME", checklist.HostName),
          new XElement("HOST_IP", checklist.HostIp),
          new XElement("HOST_MAC", checklist.HostMac),
          new XElement("HOST_FQDN", checklist.HostFqdn)
        ),
        new XElement("STIGS",
          new XElement("iSTIG",
            new XElement("STIG_INFO",
              new XElement("title", checklist.StigTitle),
              new XElement("version", checklist.StigVersion),
              new XElement("releaseinfo", checklist.StigRelease)
            ),
            checklist.Findings.Select(f => new XElement("VULN",
              new XElement("STIG_DATA", new XElement("VULN_ATTRIBUTE", "Vuln_Num"), new XElement("ATTRIBUTE_DATA", f.VulnId)),
              new XElement("STIG_DATA", new XElement("VULN_ATTRIBUTE", "Rule_Ver"), new XElement("ATTRIBUTE_DATA", f.RuleId)),
              new XElement("STIG_DATA", new XElement("VULN_ATTRIBUTE", "Rule_Title"), new XElement("ATTRIBUTE_DATA", f.RuleTitle)),
              new XElement("STIG_DATA", new XElement("VULN_ATTRIBUTE", "Severity"), new XElement("ATTRIBUTE_DATA", f.Severity)),
              new XElement("STATUS", f.Status),
              new XElement("COMMENTS", f.Comments ?? string.Empty),
              new XElement("FINDING_DETAILS", f.FindingDetails ?? string.Empty)
            ))
          )
        )
      )
    );

    doc.Save(outputPath);
  }

  public CklChecklist FromControlResults(IReadOnlyList<ControlResult> results, string stigTitle, string assetName)
  {
    return new CklChecklist
    {
      FilePath = string.Empty,
      ImportedAt = DateTimeOffset.UtcNow,
      AssetName = assetName,
      HostName = assetName,
      StigTitle = stigTitle,
      StigVersion = "1",
      Findings = results.Select(r => new CklFinding
      {
        VulnId = r.VulnId ?? r.RuleId,
        RuleId = r.RuleId,
        RuleTitle = r.Title ?? r.RuleId,
        Severity = r.Severity ?? "unknown",
        Status = MapResultStatus(r.Status),
        Comments = r.Comments ?? string.Empty,
        FindingDetails = r.FindingDetails ?? r.Comments ?? string.Empty
      }).ToList()
    };
  }

  private static string MapResultStatus(string? status)
  {
    return status?.ToLowerInvariant() switch
    {
      "pass" or "compliant" => "NotAFinding",
      "fail" or "noncompliant" => "Open",
      "notapplicable" => "Not_Applicable",
      "notreviewed" => "Not_Reviewed",
      _ => "Not_Reviewed"
    };
  }
}

public sealed class CklChecklist
{
  public string FilePath { get; set; } = string.Empty;
  public DateTimeOffset ImportedAt { get; set; }

  public string AssetName { get; set; } = string.Empty;
  public string HostName { get; set; } = string.Empty;
  public string HostIp { get; set; } = string.Empty;
  public string HostMac { get; set; } = string.Empty;
  public string HostFqdn { get; set; } = string.Empty;

  public string StigTitle { get; set; } = string.Empty;
  public string StigVersion { get; set; } = string.Empty;
  public string StigRelease { get; set; } = string.Empty;

  public List<CklFinding> Findings { get; set; } = new();

  public int PassCount => Findings.Count(f => f.Status == "NotAFinding" || f.Status == "Not_A_Finding");
  public int FailCount => Findings.Count(f => f.Status == "Open" || f.Status == "Fail");
  public int NotApplicableCount => Findings.Count(f => f.Status == "Not_Applicable" || f.Status == "NotApplicable");
  public int NotReviewedCount => Findings.Count(f => f.Status == "Not_Reviewed" || f.Status == "NotReviewed");
}

public sealed class CklFinding
{
  public string VulnId { get; set; } = string.Empty;
  public string RuleId { get; set; } = string.Empty;
  public string RuleTitle { get; set; } = string.Empty;
  public string Severity { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public string? Comments { get; set; }
  public string? FindingDetails { get; set; }
  public string? CheckContent { get; set; }
  public string? FixText { get; set; }
}
