using System.Text;
using System.Xml.Linq;
using STIGForge.Core.Constants;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Generates STIG Viewer-compatible CKL (Checklist) XML files from verification results.
/// CKL is the standard format consumed by DISA STIG Viewer and eMASS.
/// </summary>
public static class CklExporter
{
  /// <summary>
  /// Export verification results to CKL XML format.
  /// </summary>
  public static CklExportResult ExportCkl(CklExportRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");
    if (!Directory.Exists(request.BundleRoot))
      throw new DirectoryNotFoundException("Bundle root not found: " + request.BundleRoot);

    var results = LoadResults(request.BundleRoot);
    if (results.Count == 0)
      return new CklExportResult { OutputPath = string.Empty, ControlCount = 0, Message = "No verification results found." };

    var outputDir = string.IsNullOrWhiteSpace(request.OutputDirectory) ? Path.Combine(request.BundleRoot, BundlePaths.ExportDirectory) : request.OutputDirectory;
    Directory.CreateDirectory(outputDir);

    var cklPath = Path.Combine(outputDir, request.FileName ?? "stigforge_checklist.ckl");
    var doc = BuildCklDocument(results, request.HostName, request.HostIp, request.HostMac, request.StigId);
    doc.Save(cklPath);

    return new CklExportResult
    {
      OutputPath = cklPath,
      ControlCount = results.Count,
      Message = "CKL exported successfully."
    };
  }

  private static XDocument BuildCklDocument(IReadOnlyList<ControlResult> results, string? hostName, string? hostIp, string? hostMac, string? stigId)
  {
    var checklist = new XElement("CHECKLIST",
      new XElement("ASSET",
        new XElement("ROLE", "None"),
        new XElement("ASSET_TYPE", "Computing"),
        new XElement("HOST_NAME", hostName ?? Environment.MachineName),
        new XElement("HOST_IP", hostIp ?? string.Empty),
        new XElement("HOST_MAC", hostMac ?? string.Empty),
        new XElement("HOST_FQDN", string.Empty),
        new XElement("TARGET_COMMENT", string.Empty),
        new XElement("TECH_AREA", string.Empty),
        new XElement("TARGET_KEY", string.Empty),
        new XElement("WEB_OR_DATABASE", "false"),
        new XElement("WEB_DB_SITE", string.Empty),
        new XElement("WEB_DB_INSTANCE", string.Empty)),
      new XElement("STIGS",
        new XElement("iSTIG",
          new XElement("STIG_INFO",
            new XElement("SI_DATA",
              new XElement("SID_NAME", "stigid"),
              new XElement("SID_DATA", stigId ?? "STIGForge_Export")),
            new XElement("SI_DATA",
              new XElement("SID_NAME", "title"),
              new XElement("SID_DATA", "STIGForge Exported Checklist")),
            new XElement("SI_DATA",
              new XElement("SID_NAME", "releaseinfo"),
              new XElement("SID_DATA", "STIGForge " + DateTimeOffset.Now.ToString("yyyy-MM-dd")))),
          BuildVulnElements(results))));

    return new XDocument(new XDeclaration("1.0", "UTF-8", null), checklist);
  }

  private static object[] BuildVulnElements(IReadOnlyList<ControlResult> results)
  {
    var elements = new List<object>(results.Count);
    foreach (var r in results)
    {
      var vuln = new XElement("VULN",
        StigData("Vuln_Num", r.VulnId ?? string.Empty),
        StigData("Severity", r.Severity ?? "medium"),
        StigData("Rule_ID", r.RuleId ?? string.Empty),
        StigData("Rule_Title", r.Title ?? string.Empty),
        StigData("Rule_Ver", string.Empty),
        StigData("Vuln_Discuss", string.Empty),
        StigData("IA_Controls", string.Empty),
        StigData("Check_Content", string.Empty),
        StigData("Fix_Text", string.Empty),
        StigData("STIGRef", r.Tool),
        new XElement("STATUS", ExportStatusMapper.MapToCklStatus(r.Status)),
        new XElement("FINDING_DETAILS", r.FindingDetails ?? string.Empty),
        new XElement("COMMENTS", r.Comments ?? string.Empty),
        new XElement("SEVERITY_OVERRIDE", string.Empty),
        new XElement("SEVERITY_JUSTIFICATION", string.Empty));
      elements.Add(vuln);
    }
    return elements.ToArray();
  }

  private static XElement StigData(string attribute, string data)
  {
    return new XElement("STIG_DATA",
      new XElement("VULN_ATTRIBUTE", attribute),
      new XElement("ATTRIBUTE_DATA", data));
  }

  private static List<ControlResult> LoadResults(string bundleRoot)
  {
    var verifyRoot = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
    if (!Directory.Exists(verifyRoot)) return new List<ControlResult>();

    var reports = Directory.GetFiles(verifyRoot, BundlePaths.ConsolidatedResultsFileName, SearchOption.AllDirectories);
    var all = new List<ControlResult>();
    foreach (var reportPath in reports)
    {
      var report = VerifyReportReader.LoadFromJson(reportPath);
      all.AddRange(report.Results);
    }

    var deduped = new Dictionary<string, ControlResult>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < all.Count; i++)
    {
      var r = all[i];
      var key = !string.IsNullOrWhiteSpace(r.RuleId) ? "RULE:" + r.RuleId
              : !string.IsNullOrWhiteSpace(r.VulnId) ? "VULN:" + r.VulnId
              : "IDX:" + i.ToString();
      deduped[key] = r;
    }
    return deduped.Values.ToList();
  }
}

public sealed class CklExportRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? OutputDirectory { get; set; }
  public string? FileName { get; set; }
  public string? HostName { get; set; }
  public string? HostIp { get; set; }
  public string? HostMac { get; set; }
  public string? StigId { get; set; }
}

public sealed class CklExportResult
{
  public string OutputPath { get; set; } = string.Empty;
  public int ControlCount { get; set; }
  public string Message { get; set; } = string.Empty;
}
