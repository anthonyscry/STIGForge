using System.Text;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Content.Diff;

public static class ReleaseDiffWriter
{
  public static void WriteJson(string path, ReleaseDiff diff)
  {
    var json = JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  public static void WriteCsv(string path, ReleaseDiff diff)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("RuleId,VulnId,Title,Kind,IsManual,ManualChanged,ChangedFields");
    foreach (var i in diff.Items)
    {
      var fields = string.Join(";", i.ChangedFields);
      sb.AppendLine(string.Join(",",
        Csv(i.RuleId),
        Csv(i.VulnId),
        Csv(i.Title),
        Csv(i.Kind.ToString()),
        Csv(i.IsManual ? "true" : "false"),
        Csv(i.ManualChanged ? "true" : "false"),
        Csv(fields)));
    }
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
