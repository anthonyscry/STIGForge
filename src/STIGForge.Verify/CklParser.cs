using System.Xml.Linq;

namespace STIGForge.Verify;

public static class CklParser
{
  public static IReadOnlyList<ControlResult> ParseFile(string path, string toolName)
  {
    var doc = XDocument.Load(path);
    var vulnNodes = doc.Descendants("VULN").ToList();
    var results = new List<ControlResult>(vulnNodes.Count);

    foreach (var vuln in vulnNodes)
    {
      var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var sd in vuln.Elements("STIG_DATA"))
      {
        var key = sd.Element("VULN_ATTRIBUTE")?.Value?.Trim() ?? string.Empty;
        var val = sd.Element("ATTRIBUTE_DATA")?.Value?.Trim() ?? string.Empty;
        if (key.Length > 0)
          data[key] = val;
      }

      var status = vuln.Element("STATUS")?.Value?.Trim();
      var finding = vuln.Element("FINDING_DETAILS")?.Value?.Trim();
      var comments = vuln.Element("COMMENTS")?.Value?.Trim();

      var result = new ControlResult
      {
        VulnId = Get(data, "Vuln_Num"),
        RuleId = Get(data, "Rule_ID"),
        Title = Get(data, "Rule_Title") ?? Get(data, "Vuln_Title"),
        Severity = Get(data, "Severity"),
        Status = status,
        FindingDetails = finding,
        Comments = comments,
        Tool = toolName,
        SourceFile = path,
        VerifiedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
      };

      results.Add(result);
    }

    return results;
  }

  private static string? Get(IReadOnlyDictionary<string, string> dict, string key)
  {
    return dict.TryGetValue(key, out var val) ? val : null;
  }
}
