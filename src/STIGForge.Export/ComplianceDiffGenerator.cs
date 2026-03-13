using System.Text;
using System.Text.Json;
using STIGForge.Core;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Computes the difference between two verification runs, identifying regressions,
/// remediations, and scope changes. Works with file paths or pre-loaded results.
/// </summary>
public static class ComplianceDiffGenerator
{
  public static ComplianceDiff ComputeDiff(
    IReadOnlyList<NormalizedVerifyResult> baseline,
    IReadOnlyList<NormalizedVerifyResult> target,
    string baselineLabel,
    string targetLabel)
  {
    var baseMap = ToDictionary(baseline);
    var targetMap = ToDictionary(target);

    var regressions = new List<ControlStatusChange>();
    var remediations = new List<ControlStatusChange>();
    var removed = new List<ControlStatusChange>();

    foreach (var (key, bResult) in baseMap)
    {
      if (!targetMap.TryGetValue(key, out var tResult))
      {
        removed.Add(ToChange(bResult, StatusLabel(bResult.Status), string.Empty));
        continue;
      }

      if (IsPass(bResult.Status) && IsFail(tResult.Status))
        regressions.Add(ToChange(tResult, StatusLabel(bResult.Status), StatusLabel(tResult.Status)));
      else if (IsFail(bResult.Status) && IsPass(tResult.Status))
        remediations.Add(ToChange(tResult, StatusLabel(bResult.Status), StatusLabel(tResult.Status)));
    }

    var added = new List<ControlStatusChange>();
    foreach (var (key, tResult) in targetMap)
    {
      if (!baseMap.ContainsKey(key))
        added.Add(ToChange(tResult, string.Empty, StatusLabel(tResult.Status)));
    }

    var baselinePercent = CalcPercent(baseline);
    var targetPercent = CalcPercent(target);

    return new ComplianceDiff
    {
      BaselineLabel = baselineLabel,
      TargetLabel = targetLabel,
      BaselineTimestamp = baseline.Where(r => r.VerifiedAt.HasValue).Select(r => r.VerifiedAt!.Value).DefaultIfEmpty().Max(),
      TargetTimestamp = target.Where(r => r.VerifiedAt.HasValue).Select(r => r.VerifiedAt!.Value).DefaultIfEmpty().Max(),
      BaselineCompliancePercent = baselinePercent,
      TargetCompliancePercent = targetPercent,
      DeltaPercent = Math.Round(targetPercent - baselinePercent, 2),
      Regressions = regressions,
      Remediations = remediations,
      Added = added,
      Removed = removed,
      SeveritySummary = BuildSeveritySummary(regressions, remediations)
    };
  }

  public static ComplianceDiff ComputeDiffFromPaths(string baselinePath, string targetPath)
  {
    var baseResults = LoadResults(baselinePath);
    var targetResults = LoadResults(targetPath);

    var baseLabel = Path.GetDirectoryName(baselinePath) ?? baselinePath;
    var targetLabel = Path.GetDirectoryName(targetPath) ?? targetPath;

    return ComputeDiff(baseResults, targetResults, baseLabel, targetLabel);
  }

  public static void WriteDiffConsole(ComplianceDiff diff, TextWriter writer)
  {
    writer.WriteLine($"Compliance Diff: {diff.BaselineLabel} -> {diff.TargetLabel}");
    writer.WriteLine($"  Baseline: {diff.BaselineCompliancePercent:F1}%  Target: {diff.TargetCompliancePercent:F1}%  Delta: {diff.DeltaPercent:+0.0;-0.0;0.0}%");
    writer.WriteLine();

    if (diff.Regressions.Count > 0)
    {
      writer.WriteLine($"REGRESSIONS ({diff.Regressions.Count}):");
      writer.WriteLine("  {0,-14} {1,-10} {2,-8} {3,-8} {4}", "VulnId", "Severity", "Old", "New", "Title");
      foreach (var r in diff.Regressions)
        writer.WriteLine("  {0,-14} {1,-10} {2,-8} {3,-8} {4}", r.VulnId, r.Severity ?? "-", r.OldStatus, r.NewStatus, Truncate(r.Title, 50));
      writer.WriteLine();
    }

    if (diff.Remediations.Count > 0)
    {
      writer.WriteLine($"REMEDIATIONS ({diff.Remediations.Count}):");
      writer.WriteLine("  {0,-14} {1,-10} {2,-8} {3,-8} {4}", "VulnId", "Severity", "Old", "New", "Title");
      foreach (var r in diff.Remediations)
        writer.WriteLine("  {0,-14} {1,-10} {2,-8} {3,-8} {4}", r.VulnId, r.Severity ?? "-", r.OldStatus, r.NewStatus, Truncate(r.Title, 50));
      writer.WriteLine();
    }

    if (diff.Added.Count > 0)
      writer.WriteLine($"ADDED: {diff.Added.Count} controls new in target");
    if (diff.Removed.Count > 0)
      writer.WriteLine($"REMOVED: {diff.Removed.Count} controls missing in target");

    writer.WriteLine();
    var sev = diff.SeveritySummary;
    writer.WriteLine("Severity Summary:");
    writer.WriteLine($"  CAT I:   {sev.CatIRegressions} regressions, {sev.CatIRemediations} remediations");
    writer.WriteLine($"  CAT II:  {sev.CatIIRegressions} regressions, {sev.CatIIRemediations} remediations");
    writer.WriteLine($"  CAT III: {sev.CatIIIRegressions} regressions, {sev.CatIIIRemediations} remediations");
    writer.WriteLine($"  Net change: {sev.NetChange:+0;-0;0}");
  }

  public static void WriteDiffJson(ComplianceDiff diff, string outputPath)
  {
    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var json = JsonSerializer.Serialize(diff, JsonOptions.IndentedCamelCase);
    File.WriteAllText(outputPath, json, Encoding.UTF8);
  }

  public static void WriteDiffCsv(ComplianceDiff diff, string outputPath)
  {
    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    var sb = new StringBuilder(1024);
    sb.AppendLine("ChangeType,VulnId,RuleId,Severity,OldStatus,NewStatus,Title,Tool");

    WriteChanges(sb, "Regression", diff.Regressions);
    WriteChanges(sb, "Remediation", diff.Remediations);
    WriteChanges(sb, "Added", diff.Added);
    WriteChanges(sb, "Removed", diff.Removed);

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static List<NormalizedVerifyResult> LoadResults(string path)
  {
    // Accept either a direct path to consolidated-results.json or a bundle root
    string resolvedPath;
    if (File.Exists(path) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
      resolvedPath = path;
    }
    else
    {
      resolvedPath = Path.Combine(path, "Verify", "consolidated-results.json");
      if (!File.Exists(resolvedPath))
      {
        // Try searching subdirectories
        var candidates = Directory.EnumerateFiles(
          Path.Combine(path, "Verify"), "consolidated-results.json", SearchOption.AllDirectories).ToList();
        resolvedPath = candidates.Count > 0
          ? candidates[0]
          : throw new FileNotFoundException("No consolidated-results.json found under: " + path);
      }
    }

    var report = VerifyReportReader.LoadFromJson(resolvedPath);
    return report.Results.Select(r => new NormalizedVerifyResult
    {
      ControlId = r.VulnId ?? r.RuleId ?? string.Empty,
      VulnId = r.VulnId,
      RuleId = r.RuleId,
      Title = r.Title,
      Severity = r.Severity,
      Status = ExportStatusMapper.MapToVerifyStatus(r.Status),
      FindingDetails = r.FindingDetails,
      Comments = r.Comments,
      Tool = r.Tool,
      SourceFile = r.SourceFile,
      VerifiedAt = r.VerifiedAt,
      BenchmarkId = r.BenchmarkId,
      Metadata = new Dictionary<string, string>()
    }).ToList();
  }

  private static Dictionary<string, NormalizedVerifyResult> ToDictionary(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var dict = new Dictionary<string, NormalizedVerifyResult>(results.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var r in results)
    {
      var key = !string.IsNullOrEmpty(r.VulnId) ? r.VulnId! : r.RuleId ?? r.ControlId;
      dict.TryAdd(key, r); // first occurrence wins for dupes
    }
    return dict;
  }

  private static bool IsPass(VerifyStatus status) =>
    status == VerifyStatus.Pass || status == VerifyStatus.NotApplicable;

  private static bool IsFail(VerifyStatus status) =>
    status == VerifyStatus.Fail || status == VerifyStatus.Error;

  private static string StatusLabel(VerifyStatus status) => status.ToString();

  private static ControlStatusChange ToChange(NormalizedVerifyResult r, string oldStatus, string newStatus) =>
    new()
    {
      VulnId = r.VulnId ?? r.ControlId,
      RuleId = r.RuleId,
      Title = r.Title,
      Severity = r.Severity,
      OldStatus = oldStatus,
      NewStatus = newStatus,
      Tool = r.Tool
    };

  private static double CalcPercent(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var pass = results.Count(r => r.Status == VerifyStatus.Pass);
    var fail = results.Count(r => r.Status == VerifyStatus.Fail);
    var error = results.Count(r => r.Status == VerifyStatus.Error);
    var evaluated = pass + fail + error;
    return evaluated > 0 ? Math.Round((pass / (double)evaluated) * 100.0, 2) : 0.0;
  }

  private static DiffSeveritySummary BuildSeveritySummary(
    IReadOnlyList<ControlStatusChange> regressions,
    IReadOnlyList<ControlStatusChange> remediations)
  {
    var summary = new DiffSeveritySummary();

    foreach (var r in regressions)
    {
      switch (NormalizeSeverity(r.Severity))
      {
        case "high": summary.CatIRegressions++; break;
        case "medium": summary.CatIIRegressions++; break;
        case "low": summary.CatIIIRegressions++; break;
      }
    }

    foreach (var r in remediations)
    {
      switch (NormalizeSeverity(r.Severity))
      {
        case "high": summary.CatIRemediations++; break;
        case "medium": summary.CatIIRemediations++; break;
        case "low": summary.CatIIIRemediations++; break;
      }
    }

    summary.NetChange = remediations.Count - regressions.Count;
    return summary;
  }

  private static string NormalizeSeverity(string? severity)
  {
    if (string.IsNullOrWhiteSpace(severity)) return "medium";
    var s = severity!.Trim().ToLowerInvariant();
    return s switch
    {
      "cat i" or "cati" => "high",
      "cat ii" or "catii" => "medium",
      "cat iii" or "catiii" => "low",
      _ => s
    };
  }

  private static string Truncate(string? value, int maxLen)
  {
    if (string.IsNullOrEmpty(value)) return string.Empty;
    return value.Length <= maxLen ? value : value.Substring(0, maxLen - 3) + "...";
  }

  private static string Csv(string? value) => Core.CsvEscape.Escape(value);

  private static void WriteChanges(StringBuilder sb, string changeType, IReadOnlyList<ControlStatusChange> changes)
  {
    foreach (var c in changes)
      sb.AppendLine(string.Join(",", Csv(changeType), Csv(c.VulnId), Csv(c.RuleId), Csv(c.Severity), Csv(c.OldStatus), Csv(c.NewStatus), Csv(c.Title), Csv(c.Tool)));
  }
}
