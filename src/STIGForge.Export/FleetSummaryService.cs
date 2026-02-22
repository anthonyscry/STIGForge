using System.Text;
using System.Text.Json;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Aggregates per-host verification results into a unified fleet summary
/// with control status matrix, compliance percentages, and fleet-wide POA&amp;M.
/// </summary>
public class FleetSummaryService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  /// <summary>
  /// Generate fleet summary from collected per-host results.
  /// </summary>
  public FleetSummary GenerateSummary(string fleetResultsRoot)
  {
    if (!Directory.Exists(fleetResultsRoot))
      throw new DirectoryNotFoundException("Fleet results root not found: " + fleetResultsRoot);

    var perHostStats = new List<FleetHostStats>();
    var controlMatrix = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    var allHosts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var hostDir in Directory.GetDirectories(fleetResultsRoot).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
    {
      var hostName = Path.GetFileName(hostDir);
      allHosts.Add(hostName);

      var results = LoadHostResults(hostDir);
      if (results.Count == 0)
      {
        perHostStats.Add(new FleetHostStats
        {
          HostName = hostName,
          TotalControls = 0,
          CompliancePercentage = 0
        });
        continue;
      }

      var passCount = 0;
      var failCount = 0;
      var naCount = 0;
      var nrCount = 0;

      foreach (var result in results)
      {
        var controlKey = result.VulnId ?? result.RuleId ?? result.Title ?? "Unknown";
        var status = NormalizeStatus(result.Status);

        switch (status)
        {
          case "Pass": passCount++; break;
          case "Fail": failCount++; break;
          case "NA": naCount++; break;
          default: nrCount++; break;
        }

        if (!controlMatrix.ContainsKey(controlKey))
          controlMatrix[controlKey] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        controlMatrix[controlKey][hostName] = status;
      }

      var applicable = passCount + failCount;
      var compliancePct = applicable > 0 ? Math.Round((double)passCount / applicable * 100, 2) : 0;

      perHostStats.Add(new FleetHostStats
      {
        HostName = hostName,
        TotalControls = results.Count,
        PassCount = passCount,
        FailCount = failCount,
        NaCount = naCount,
        NrCount = nrCount,
        CompliancePercentage = compliancePct
      });
    }

    // Calculate fleet-wide compliance as weighted average
    var totalApplicable = perHostStats.Sum(h => h.PassCount + h.FailCount);
    var totalPass = perHostStats.Sum(h => h.PassCount);
    var fleetCompliance = totalApplicable > 0 ? Math.Round((double)totalPass / totalApplicable * 100, 2) : 0;

    // Build failing controls list
    var failingControls = new List<FleetFailingControl>();
    foreach (var kvp in controlMatrix)
    {
      var affectedHosts = kvp.Value
        .Where(h => h.Value == "Fail")
        .Select(h => h.Key)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
        .ToList();

      if (affectedHosts.Count > 0)
      {
        failingControls.Add(new FleetFailingControl
        {
          ControlId = kvp.Key,
          AffectedHosts = affectedHosts,
          AffectedCount = affectedHosts.Count
        });
      }
    }

    return new FleetSummary
    {
      PerHostStats = perHostStats,
      ControlStatusMatrix = controlMatrix.ToDictionary(
        kvp => kvp.Key,
        kvp => (IReadOnlyDictionary<string, string>)kvp.Value.AsReadOnly()),
      FailingControls = failingControls,
      FleetWideCompliance = fleetCompliance,
      HostNames = allHosts.ToList(),
      GeneratedAt = DateTimeOffset.Now
    };
  }

  /// <summary>
  /// Generate fleet-aggregated POA&amp;M with host attribution.
  /// </summary>
  public PoamPackage GenerateFleetPoam(string fleetResultsRoot, string systemName)
  {
    if (!Directory.Exists(fleetResultsRoot))
      throw new DirectoryNotFoundException("Fleet results root not found: " + fleetResultsRoot);

    var poamItems = new Dictionary<string, PoamItem>(StringComparer.OrdinalIgnoreCase);
    var hostsByControl = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var hostDir in Directory.GetDirectories(fleetResultsRoot).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
    {
      var hostName = Path.GetFileName(hostDir);
      var results = LoadHostResults(hostDir);

      foreach (var result in results)
      {
        var status = NormalizeStatus(result.Status);
        if (status != "Fail") continue;

        var controlKey = result.VulnId ?? result.RuleId ?? result.Title ?? "Unknown";

        if (!hostsByControl.ContainsKey(controlKey))
          hostsByControl[controlKey] = new List<string>();
        if (!hostsByControl[controlKey].Contains(hostName, StringComparer.OrdinalIgnoreCase))
          hostsByControl[controlKey].Add(hostName);

        if (!poamItems.ContainsKey(controlKey))
        {
          poamItems[controlKey] = new PoamItem
          {
            ControlId = controlKey,
            VulnId = result.VulnId,
            RuleId = result.RuleId,
            Title = result.Title ?? string.Empty,
            Severity = result.Severity ?? string.Empty,
            Description = result.FindingDetails ?? string.Empty,
            Status = "Ongoing",
            SystemName = systemName,
            ScheduledCompletionDate = DateTimeOffset.Now.AddDays(90)
          };
        }
      }
    }

    // Assign HostsAffected
    foreach (var kvp in poamItems)
    {
      if (hostsByControl.TryGetValue(kvp.Key, out var hosts))
      {
        kvp.Value.HostsAffected = string.Join(",", hosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase));
      }
    }

    var items = poamItems.Values
      .OrderBy(p => p.ControlId, StringComparer.OrdinalIgnoreCase)
      .ToList();

    return new PoamPackage
    {
      Items = items,
      Summary = new PoamSummary
      {
        TotalFindings = items.Count,
        CriticalFindings = items.Count(i => string.Equals(i.Severity, "high", StringComparison.OrdinalIgnoreCase)),
        HighFindings = items.Count(i => string.Equals(i.Severity, "medium", StringComparison.OrdinalIgnoreCase)),
        MediumFindings = items.Count(i => string.Equals(i.Severity, "low", StringComparison.OrdinalIgnoreCase)),
        GeneratedAt = DateTimeOffset.Now,
        SystemName = systemName
      }
    };
  }

  /// <summary>
  /// Write fleet summary files in JSON, CSV, and TXT formats.
  /// </summary>
  public void WriteSummaryFiles(FleetSummary summary, string outputDir)
  {
    Directory.CreateDirectory(outputDir);

    // JSON
    var jsonPath = Path.Combine(outputDir, "fleet_summary.json");
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(summary, JsonOptions), Encoding.UTF8);

    // CSV - rows = controls, columns = hosts
    var csvPath = Path.Combine(outputDir, "fleet_summary.csv");
    WriteSummaryCsv(summary, csvPath);

    // TXT - human-readable report
    var txtPath = Path.Combine(outputDir, "fleet_summary.txt");
    WriteSummaryTxt(summary, txtPath);
  }

  /// <summary>
  /// Generate per-host CKL files from collected fleet artifacts.
  /// Moved from FleetService since CklExporter lives in the Export layer.
  /// </summary>
  public static void GeneratePerHostCkl(string fleetResultsRoot)
  {
    if (!Directory.Exists(fleetResultsRoot)) return;

    foreach (var hostDir in Directory.GetDirectories(fleetResultsRoot))
    {
      var verifyDir = Path.Combine(hostDir, "Verify");
      if (!Directory.Exists(verifyDir)) continue;

      var consolidatedFiles = Directory.GetFiles(verifyDir, "consolidated-results.json", SearchOption.AllDirectories);
      if (consolidatedFiles.Length == 0) continue;

      var hostName = Path.GetFileName(hostDir);
      var exportDir = Path.Combine(hostDir, "Export");

      try
      {
        CklExporter.ExportCkl(new CklExportRequest
        {
          BundleRoot = hostDir,
          OutputDirectory = exportDir,
          HostName = hostName,
          FileFormat = CklFileFormat.Ckl
        });
      }
      catch
      {
        // Best-effort: continue with other hosts
      }
    }
  }

  private static void WriteSummaryCsv(FleetSummary summary, string csvPath)
  {
    var sb = new StringBuilder();
    var hosts = summary.HostNames;

    // Header
    sb.Append("ControlId");
    foreach (var host in hosts)
      sb.Append(',').Append(host);
    sb.AppendLine();

    // Rows
    foreach (var kvp in summary.ControlStatusMatrix.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
      sb.Append(kvp.Key);
      foreach (var host in hosts)
      {
        sb.Append(',');
        if (kvp.Value.TryGetValue(host, out var status))
          sb.Append(status);
        else
          sb.Append("NR");
      }
      sb.AppendLine();
    }

    File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteSummaryTxt(FleetSummary summary, string txtPath)
  {
    var sb = new StringBuilder();
    sb.AppendLine("=== FLEET COMPLIANCE SUMMARY ===");
    sb.AppendLine($"Generated: {summary.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"Fleet-wide compliance: {summary.FleetWideCompliance:F1}%");
    sb.AppendLine();

    // Per-host table
    sb.AppendLine("--- Per-Host Compliance ---");
    sb.AppendLine($"{"Host",-30} {"Total",-8} {"Pass",-8} {"Fail",-8} {"NA",-8} {"NR",-8} {"Compliance"}");
    sb.AppendLine(new string('-', 90));
    foreach (var host in summary.PerHostStats)
    {
      sb.AppendLine($"{host.HostName,-30} {host.TotalControls,-8} {host.PassCount,-8} {host.FailCount,-8} {host.NaCount,-8} {host.NrCount,-8} {host.CompliancePercentage:F1}%");
    }
    sb.AppendLine();

    // Failing controls
    if (summary.FailingControls.Count > 0)
    {
      sb.AppendLine("--- Fleet-Wide Failing Controls ---");
      sb.AppendLine($"{"Control",-20} {"Affected Hosts",-10} {"Hosts"}");
      sb.AppendLine(new string('-', 60));
      foreach (var fc in summary.FailingControls)
      {
        sb.AppendLine($"{fc.ControlId,-20} {fc.AffectedCount,-10} {string.Join(", ", fc.AffectedHosts)}");
      }
    }

    File.WriteAllText(txtPath, sb.ToString(), Encoding.UTF8);
  }

  private static List<ControlResult> LoadHostResults(string hostDir)
  {
    var verifyDir = Path.Combine(hostDir, "Verify");
    if (!Directory.Exists(verifyDir))
      return new List<ControlResult>();

    var reports = Directory.GetFiles(verifyDir, "consolidated-results.json", SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var dedup = new Dictionary<string, ControlResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var reportPath in reports)
    {
      try
      {
        var report = VerifyReportReader.LoadFromJson(reportPath);
        foreach (var result in report.Results)
        {
          var key = result.VulnId ?? result.RuleId ?? result.Title ?? string.Empty;
          if (!string.IsNullOrWhiteSpace(key))
            dedup[key] = result;
        }
      }
      catch
      {
        // Skip corrupt report files
      }
    }

    return dedup.Values.ToList();
  }

  private static string NormalizeStatus(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return "NR";

    var s = status.Trim();
    if (s.Equals("Pass", StringComparison.OrdinalIgnoreCase)
        || s.Equals("NotAFinding", StringComparison.OrdinalIgnoreCase)
        || s.Equals("pass", StringComparison.OrdinalIgnoreCase))
      return "Pass";

    if (s.Equals("Fail", StringComparison.OrdinalIgnoreCase)
        || s.Equals("Open", StringComparison.OrdinalIgnoreCase)
        || s.Equals("fail", StringComparison.OrdinalIgnoreCase))
      return "Fail";

    if (s.Equals("Not_Applicable", StringComparison.OrdinalIgnoreCase)
        || s.Equals("NA", StringComparison.OrdinalIgnoreCase)
        || s.Equals("NotApplicable", StringComparison.OrdinalIgnoreCase))
      return "NA";

    return "NR";
  }
}

/// <summary>
/// Aggregated fleet compliance summary.
/// </summary>
public sealed class FleetSummary
{
  public List<FleetHostStats> PerHostStats { get; set; } = new();
  public IDictionary<string, IReadOnlyDictionary<string, string>> ControlStatusMatrix { get; set; }
    = new Dictionary<string, IReadOnlyDictionary<string, string>>();
  public List<FleetFailingControl> FailingControls { get; set; } = new();
  public List<string> HostNames { get; set; } = new();
  public double FleetWideCompliance { get; set; }
  public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// Per-host compliance statistics.
/// </summary>
public sealed class FleetHostStats
{
  public string HostName { get; set; } = string.Empty;
  public int TotalControls { get; set; }
  public int PassCount { get; set; }
  public int FailCount { get; set; }
  public int NaCount { get; set; }
  public int NrCount { get; set; }
  public double CompliancePercentage { get; set; }
}

/// <summary>
/// A control that fails on one or more fleet hosts.
/// </summary>
public sealed class FleetFailingControl
{
  public string ControlId { get; set; } = string.Empty;
  public List<string> AffectedHosts { get; set; } = new();
  public int AffectedCount { get; set; }
}
