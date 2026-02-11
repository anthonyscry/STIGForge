using System.Text;
using System.Text.Json;

namespace STIGForge.Verify;

public static class VerifyReportWriter
{
  public static VerifyReport BuildFromCkls(string outputRoot, string toolName)
  {
    var cklFiles = Directory.GetFiles(outputRoot, "*.ckl", SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var results = new List<ControlResult>();
    foreach (var ckl in cklFiles)
      results.AddRange(CklParser.ParseFile(ckl, toolName));

    return new VerifyReport
    {
      Tool = toolName,
      ToolVersion = "unknown",
      StartedAt = DateTimeOffset.Now,
      FinishedAt = DateTimeOffset.Now,
      OutputRoot = outputRoot,
      Results = results
    };
  }

  public static void WriteJson(string path, VerifyReport report)
  {
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  public static void WriteCsv(string path, IReadOnlyList<ControlResult> results)
  {
    var sb = new StringBuilder(results.Count * 80 + 256);
    sb.AppendLine("VulnId,RuleId,Title,Severity,Status,Tool,SourceFile,VerifiedAt");

    foreach (var r in results)
    {
      sb.AppendLine(string.Join(",",
        Csv(r.VulnId),
        Csv(r.RuleId),
        Csv(r.Title),
        Csv(r.Severity),
        Csv(r.Status),
        Csv(r.Tool),
        Csv(r.SourceFile),
        Csv(r.VerifiedAt?.ToString("o"))));
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  public static IReadOnlyList<CoverageSummary> BuildCoverageSummary(IReadOnlyList<ControlResult> results)
  {
    var grouped = results
      .GroupBy(r => r.Tool ?? string.Empty, StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    var summaries = new List<CoverageSummary>();
    foreach (var g in grouped)
    {
      int closed = g.Count(r => IsClosed(r.Status));
      int total = g.Count();
      int open = total - closed;
      summaries.Add(new CoverageSummary
      {
        Tool = string.IsNullOrWhiteSpace(g.Key) ? "Unknown" : g.Key,
        ClosedCount = closed,
        OpenCount = open,
        TotalCount = total,
        ClosedPercent = total == 0 ? 0 : Math.Round((closed * 100.0) / total, 2)
      });
    }

    return summaries;
  }

  public static IReadOnlyList<CoverageOverlap> BuildOverlapSummary(IReadOnlyList<ControlResult> results)
  {
    var maps = BuildControlSourceMap(results);
    var grouped = maps
      .GroupBy(m => m.SourcesKey, StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    var overlaps = new List<CoverageOverlap>();
    foreach (var g in grouped)
    {
      int closed = g.Count(x => x.IsClosed);
      int total = g.Count();
      overlaps.Add(new CoverageOverlap
      {
        SourcesKey = g.Key,
        SourceCount = g.Key.Split('|').Length,
        ControlsCount = total,
        ClosedCount = closed,
        OpenCount = total - closed
      });
    }

    return overlaps;
  }

  public static IReadOnlyList<ControlSourceMap> BuildControlSourceMap(IReadOnlyList<ControlResult> results)
  {
    var grouped = results
      .GroupBy(r => GetControlKey(r), StringComparer.OrdinalIgnoreCase);

    var maps = new List<ControlSourceMap>();
    foreach (var g in grouped)
    {
      var sources = g
        .Select(r => string.IsNullOrWhiteSpace(r.Tool) ? "Unknown" : r.Tool.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList();

      bool anyClosed = g.Any(r => IsClosed(r.Status));
      bool anyOpen = g.Any(r => !IsClosed(r.Status));
      bool isClosed = anyClosed && !anyOpen;

      var sample = g.FirstOrDefault();
      if (sample == null)
        continue;

      maps.Add(new ControlSourceMap
      {
        ControlKey = g.Key,
        VulnId = sample.VulnId,
        RuleId = sample.RuleId,
        Title = sample.Title,
        SourcesKey = string.Join("|", sources),
        IsClosed = isClosed
      });
    }

    return maps;
  }

  public static void WriteOverlapSummary(string csvPath, string jsonPath, IReadOnlyList<CoverageOverlap> overlaps)
  {
    var json = JsonSerializer.Serialize(overlaps, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json, Encoding.UTF8);

    var sb = new StringBuilder(overlaps.Count * 64 + 128);
    sb.AppendLine("SourcesKey,SourceCount,ControlsCount,ClosedCount,OpenCount");
    foreach (var o in overlaps)
    {
      sb.AppendLine(string.Join(",",
        Csv(o.SourcesKey),
        o.SourceCount,
        o.ControlsCount,
        o.ClosedCount,
        o.OpenCount));
    }

    File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
  }

  public static void WriteControlSourceMap(string csvPath, IReadOnlyList<ControlSourceMap> maps)
  {
    var sb = new StringBuilder(maps.Count * 80 + 256);
    sb.AppendLine("ControlKey,VulnId,RuleId,Title,SourcesKey,IsClosed");
    foreach (var m in maps)
    {
      sb.AppendLine(string.Join(",",
        Csv(m.ControlKey),
        Csv(m.VulnId),
        Csv(m.RuleId),
        Csv(m.Title),
        Csv(m.SourcesKey),
        m.IsClosed ? "true" : "false"));
    }
    File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
  }

  public static void WriteCoverageSummary(string csvPath, string jsonPath, IReadOnlyList<CoverageSummary> summaries)
  {
    var json = JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(jsonPath, json, Encoding.UTF8);

    var sb = new StringBuilder(summaries.Count * 64 + 128);
    sb.AppendLine("Tool,ClosedCount,OpenCount,TotalCount,ClosedPercent");
    foreach (var s in summaries)
    {
      sb.AppendLine(string.Join(",",
        Csv(s.Tool),
        s.ClosedCount,
        s.OpenCount,
        s.TotalCount,
        s.ClosedPercent.ToString("0.##")));
    }

    File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }

  private static bool IsClosed(string? status)
  {
    if (string.IsNullOrWhiteSpace(status)) return false;
    var s = status!.Trim().ToLowerInvariant();

    if (s.Contains("notafinding") || s.Contains("pass")) return true;
    if (s.Contains("not_applicable") || s.Contains("not applicable")) return true;

    if (s.Contains("open") || s.Contains("fail")) return false;
    if (s.Contains("not_reviewed") || s.Contains("not reviewed")) return false;

    return false;
  }

  private static string GetControlKey(ControlResult r)
  {
    if (!string.IsNullOrWhiteSpace(r.RuleId)) return "RULE:" + r.RuleId!.Trim();
    if (!string.IsNullOrWhiteSpace(r.VulnId)) return "VULN:" + r.VulnId!.Trim();
    return "TITLE:" + (r.Title ?? string.Empty).Trim();
  }
}
