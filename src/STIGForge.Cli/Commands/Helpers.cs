using STIGForge.Core.Models;

namespace STIGForge.Cli.Commands;

internal static class Helpers
{
  public static string ResolveAbsolutePath(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
      return Path.GetFullPath(".");

    return Path.GetFullPath(path);
  }

  public static string ResolveMissionPath(string outputRoot)
  {
    return Path.Combine(ResolveAbsolutePath(outputRoot), "mission.json");
  }

  public static string ResolveReportPath(string path)
  {
    if (File.Exists(path)) return path;
    if (Directory.Exists(path))
    {
      var candidate = Path.Combine(path, "consolidated-results.json");
      if (File.Exists(candidate)) return candidate;
    }
    throw new FileNotFoundException("Report not found: " + path);
  }

  public static IReadOnlyList<PowerStigOverride> ReadPowerStigOverrides(string csvPath)
  {
    if (!File.Exists(csvPath)) throw new FileNotFoundException("CSV not found", csvPath);
    var lines = File.ReadAllLines(csvPath);
    var list = new List<PowerStigOverride>();
    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line)) continue;
      if (line.StartsWith("RuleId", StringComparison.OrdinalIgnoreCase)) continue;
      var parts = ParseCsvLine(line);
      if (parts.Length < 3) continue;
      var ruleId = parts[0].Trim();
      if (string.IsNullOrWhiteSpace(ruleId)) continue;
      list.Add(new PowerStigOverride { RuleId = ruleId, SettingName = parts[1].Trim(), Value = parts[2].Trim() });
    }
    return list;
  }

  public static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new System.Text.StringBuilder();
    bool inQuotes = false;
    for (int i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
        else inQuotes = !inQuotes;
      }
      else if (ch == ',' && !inQuotes) { list.Add(sb.ToString()); sb.Clear(); }
      else sb.Append(ch);
    }
    list.Add(sb.ToString());
    return list.ToArray();
  }

  public static void WritePowerStigMapCsv(string path, IReadOnlyList<ControlRecord> controls)
  {
    var sb = new System.Text.StringBuilder(controls.Count * 40 + 128);
    sb.AppendLine("RuleId,Title,SettingName,Value,HintSetting,HintValue");
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in controls)
    {
      var ruleId = c.ExternalIds.RuleId;
      if (string.IsNullOrWhiteSpace(ruleId) || !seen.Add(ruleId)) continue;
      sb.AppendLine(string.Join(",", Csv(ruleId), Csv(c.Title), "", "", Csv(ExtractHintSetting(c)), Csv(ExtractHintValue(c))));
    }
    File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
  }

  public static string Truncate(string? value, int maxLength)
  {
    if (string.IsNullOrEmpty(value)) return string.Empty;
    var singleLine = value.Replace("\r", "").Replace("\n", " ");
    return singleLine.Length <= maxLength ? singleLine : singleLine.Substring(0, maxLength) + "...";
  }

  public static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }

  private static string ExtractHintSetting(ControlRecord c)
  {
    var text = (c.FixText ?? "") + "\n" + (c.CheckText ?? "");
    return ExtractAfterLabel(text, new[] { "Value Name", "Value name", "ValueName" });
  }

  private static string ExtractHintValue(ControlRecord c)
  {
    var text = (c.FixText ?? "") + "\n" + (c.CheckText ?? "");
    return ExtractAfterLabel(text, new[] { "Value Data", "Value data", "Value:" });
  }

  private static string ExtractAfterLabel(string text, string[] labels)
  {
    foreach (var label in labels)
    {
      var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
      if (idx < 0) continue;
      var line = text.Substring(idx + label.Length);
      var nl = line.IndexOfAny(new[] { '\r', '\n' });
      if (nl >= 0) line = line.Substring(0, nl);
      var cleaned = line.Replace(":", "").Trim();
      if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
    }
    return string.Empty;
  }
}
