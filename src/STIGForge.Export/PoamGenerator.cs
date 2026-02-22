using System.Text;
using System.Text.Json;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Generates eMASS-compatible POA&M (Plan of Action and Milestones) from failed verification results.
/// Formats data for eMASS import: control deficiencies, remediation plans, milestones.
/// </summary>
public static class PoamGenerator
{
  /// <summary>
  /// Generate POA&M records from verification results.
  /// Only includes failed/open findings requiring remediation.
  /// </summary>
  public static PoamPackage GeneratePoam(
    IReadOnlyList<NormalizedVerifyResult> results,
    string systemName,
    string bundleId)
  {
    var openFindings = results
      .Where(r => r.Status == VerifyStatus.Fail || r.Status == VerifyStatus.Error)
      .ToList();

    var poamItems = new List<PoamItem>(openFindings.Count);

    foreach (var finding in openFindings)
    {
      var poam = new PoamItem
      {
        ControlId = finding.ControlId,
        VulnId = finding.VulnId,
        RuleId = finding.RuleId,
        Title = finding.Title ?? "Untitled Control",
        Severity = MapSeverityToImpact(finding.Severity),
        Description = BuildDescription(finding),
        Weakness = BuildWeakness(finding),
        Resources = "System administrators, security team",
        ScheduledCompletionDate = CalculateCompletionDate(finding.Severity),
        MilestoneChanges = "Initial finding from automated scan",
        MilestoneChangesDate = finding.VerifiedAt ?? DateTimeOffset.Now,
        SourceIdentifyingControl = finding.Tool,
        SourceIdentifyingVulnerability = BuildSourceDescription(finding),
        Status = "Ongoing",
        Comments = finding.Comments ?? string.Empty,
        RawFindingDetails = finding.FindingDetails ?? string.Empty,
        SystemName = systemName,
        BundleId = bundleId
      };

      poamItems.Add(poam);
    }

    var summary = new PoamSummary
    {
      TotalFindings = openFindings.Count,
      CriticalFindings = openFindings.Count(f => f.Severity?.ToLowerInvariant() == "high"),
      HighFindings = openFindings.Count(f => f.Severity?.ToLowerInvariant() == "medium"),
      MediumFindings = openFindings.Count(f => f.Severity?.ToLowerInvariant() == "low"),
      LowFindings = 0,
      GeneratedAt = DateTimeOffset.Now,
      SystemName = systemName
    };

    return new PoamPackage
    {
      Items = poamItems,
      Summary = summary
    };
  }

  /// <summary>
  /// Write POA&M package to JSON and CSV files.
  /// </summary>
  public static void WritePoamFiles(PoamPackage package, string outputDir)
  {
    Directory.CreateDirectory(outputDir);

    // JSON export (structured data for tooling)
    var jsonPath = Path.Combine(outputDir, "poam.json");
    var json = JsonSerializer.Serialize(package, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(jsonPath, json, Encoding.UTF8);

    // CSV export (eMASS import format)
    var csvPath = Path.Combine(outputDir, "poam.csv");
    WritePoamCsv(package.Items, csvPath);

    // Summary report
    var summaryPath = Path.Combine(outputDir, "poam_summary.txt");
    WriteSummaryReport(package.Summary, summaryPath);
  }

  private static void WritePoamCsv(IReadOnlyList<PoamItem> items, string outputPath)
  {
    var sb = new StringBuilder(items.Count * 200 + 512);

    // eMASS POA&M CSV header
    sb.AppendLine(string.Join(",",
      "Control Vulnerability Description",
      "Security Control Number (NC/NA controls only)",
      "Office/Org",
      "Security Checks",
      "Resources Required",
      "Scheduled Completion Date",
      "Milestone with Completion Dates",
      "Milestone Changes",
      "Source Identifying Control Vulnerability",
      "Status",
      "Comments",
      "Raw Severity",
      "Impact",
      "Likelihood"));

    foreach (var item in items)
    {
      sb.AppendLine(string.Join(",",
        Csv(item.Description),
        Csv(item.ControlId),
        Csv("System Admin"), // Office/Org
        Csv(item.Weakness),
        Csv(item.Resources),
        Csv(item.ScheduledCompletionDate.ToString("yyyy-MM-dd")),
        Csv($"Remediation target: {item.ScheduledCompletionDate:yyyy-MM-dd}"),
        Csv(item.MilestoneChanges),
        Csv(item.SourceIdentifyingVulnerability),
        Csv(item.Status),
        Csv(item.Comments),
        Csv(item.Severity),
        Csv(item.Severity), // Impact = Severity for STIG findings
        Csv("Medium"))); // Default likelihood
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteSummaryReport(PoamSummary summary, string outputPath)
  {
    var sb = new StringBuilder(512);
    sb.AppendLine("POA&M Summary Report");
    sb.AppendLine("====================");
    sb.AppendLine();
    sb.AppendLine($"System: {summary.SystemName}");
    sb.AppendLine($"Generated: {summary.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();
    sb.AppendLine("Findings by Severity:");
    sb.AppendLine($"  Critical (CAT I): {summary.CriticalFindings}");
    sb.AppendLine($"  High (CAT II):    {summary.HighFindings}");
    sb.AppendLine($"  Medium (CAT III): {summary.MediumFindings}");
    sb.AppendLine($"  Low:              {summary.LowFindings}");
    sb.AppendLine();
    sb.AppendLine($"Total Open Findings: {summary.TotalFindings}");

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static string MapSeverityToImpact(string? severity)
  {
    if (string.IsNullOrWhiteSpace(severity))
      return "Medium";

    var normalized = severity!.ToLowerInvariant();
    return normalized switch
    {
      "high" => "High",
      "cat i" => "High",
      "cati" => "High",
      "medium" => "Medium",
      "cat ii" => "Medium",
      "catii" => "Medium",
      "low" => "Low",
      "cat iii" => "Low",
      "catiii" => "Low",
      _ => "Medium"
    };
  }

  private static string BuildDescription(NormalizedVerifyResult finding)
  {
    var desc = finding.Title ?? "Security control deficiency";
    if (!string.IsNullOrWhiteSpace(finding.FindingDetails))
    {
      var details = finding.FindingDetails!.Length > 200
        ? finding.FindingDetails.Substring(0, 200) + "..."
        : finding.FindingDetails;
      desc += $" | Details: {details}";
    }
    return desc;
  }

  private static string BuildWeakness(NormalizedVerifyResult finding)
  {
    var weakness = $"Control {finding.ControlId} failed verification";
    if (finding.Metadata.TryGetValue("check_content", out var checkContent))
      weakness += $" | Check: {checkContent}";
    return weakness;
  }

  private static string BuildSourceDescription(NormalizedVerifyResult finding)
  {
    var source = $"{finding.Tool} verification";
    if (finding.VerifiedAt.HasValue)
      source += $" on {finding.VerifiedAt.Value:yyyy-MM-dd}";
    if (!string.IsNullOrWhiteSpace(finding.SourceFile))
      source += $" (source: {Path.GetFileName(finding.SourceFile)})";
    return source;
  }

  private static DateTimeOffset CalculateCompletionDate(string? severity)
  {
    var now = DateTimeOffset.Now;
    var normalized = (severity ?? "medium").ToLowerInvariant();

    // eMASS timelines: CAT I (30 days), CAT II (90 days), CAT III (180 days)
    return normalized switch
    {
      "high" => now.AddDays(30),
      "cat i" => now.AddDays(30),
      "cati" => now.AddDays(30),
      "medium" => now.AddDays(90),
      "cat ii" => now.AddDays(90),
      "catii" => now.AddDays(90),
      "low" => now.AddDays(180),
      "cat iii" => now.AddDays(180),
      "catiii" => now.AddDays(180),
      _ => now.AddDays(90)
    };
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}

/// <summary>
/// POA&M package containing items and summary.
/// </summary>
public sealed class PoamPackage
{
  public IReadOnlyList<PoamItem> Items { get; set; } = Array.Empty<PoamItem>();
  public PoamSummary Summary { get; set; } = new();
}

/// <summary>
/// Individual POA&M item for a failed control.
/// </summary>
public sealed class PoamItem
{
  public string ControlId { get; set; } = string.Empty;
  public string? VulnId { get; set; }
  public string? RuleId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string Severity { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Weakness { get; set; } = string.Empty;
  public string Resources { get; set; } = string.Empty;
  public DateTimeOffset ScheduledCompletionDate { get; set; }
  public string MilestoneChanges { get; set; } = string.Empty;
  public DateTimeOffset MilestoneChangesDate { get; set; }
  public string SourceIdentifyingControl { get; set; } = string.Empty;
  public string SourceIdentifyingVulnerability { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public string Comments { get; set; } = string.Empty;
  public string RawFindingDetails { get; set; } = string.Empty;
  public string SystemName { get; set; } = string.Empty;
  public string BundleId { get; set; } = string.Empty;
  public string? HostsAffected { get; set; }
}

/// <summary>
/// POA&M summary statistics.
/// </summary>
public sealed class PoamSummary
{
  public int TotalFindings { get; set; }
  public int CriticalFindings { get; set; }
  public int HighFindings { get; set; }
  public int MediumFindings { get; set; }
  public int LowFindings { get; set; }
  public DateTimeOffset GeneratedAt { get; set; }
  public string SystemName { get; set; } = string.Empty;
}
