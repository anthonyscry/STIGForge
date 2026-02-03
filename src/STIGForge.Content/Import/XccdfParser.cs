using System.Xml.Linq;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class XccdfParser
{
  public static IReadOnlyList<ControlRecord> Parse(string xmlPath, string packName)
  {
    var doc = XDocument.Load(xmlPath);
    XNamespace ns = doc.Root?.Name.NamespaceName ?? string.Empty;

    var benchmark = doc.Descendants(ns + "Benchmark").FirstOrDefault() ?? doc.Root;
    string benchmarkId = benchmark?.Attribute("id")?.Value ?? Path.GetFileNameWithoutExtension(xmlPath);

    var rules = doc.Descendants(ns + "Rule").ToList();
    var results = new List<ControlRecord>(rules.Count);

    foreach (var r in rules)
    {
      var ruleId = r.Attribute("id")?.Value;
      var title = r.Element(ns + "title")?.Value?.Trim() ?? ruleId ?? "Untitled";
      var severity = r.Attribute("severity")?.Value ?? "unknown";
      var desc = r.Element(ns + "description")?.Value?.Trim();

      var external = new ExternalIds
      {
        VulnId = ExtractVulnId(ruleId, title),
        RuleId = ruleId,
        SrgId = null,
        BenchmarkId = benchmarkId
      };

      var app = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Unknown,
        Confidence = Confidence.Low
      };

      var rev = new RevisionInfo
      {
        PackName = packName,
        BenchmarkVersion = null,
        BenchmarkRelease = null,
        BenchmarkDate = null
      };

      results.Add(new ControlRecord
      {
        ControlId = Guid.NewGuid().ToString("n"),
        ExternalIds = external,
        Title = title,
        Severity = severity,
        Discussion = desc,
        CheckText = null,
        FixText = null,
        IsManual = false,
        WizardPrompt = null,
        Applicability = app,
        Revision = rev
      });
    }

    return results;
  }

  private static string? ExtractVulnId(string? ruleId, string title)
  {
    var text = (ruleId ?? string.Empty) + " " + title;
    var idx = text.IndexOf("V-", StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return null;

    int end = idx + 2;
    while (end < text.Length && char.IsDigit(text[end])) end++;
    var candidate = text.Substring(idx, end - idx);
    return candidate.Length >= 4 ? candidate : null;
  }
}
