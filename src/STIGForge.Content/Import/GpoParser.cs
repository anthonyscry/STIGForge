using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class GpoParser
{
  public static IReadOnlyList<ControlRecord> Parse(string admxPath, string packName)
  {
    if (!File.Exists(admxPath))
      throw new FileNotFoundException("ADMX file not found", admxPath);

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Ignore,
      IgnoreWhitespace = true,
      Async = false
    };

    using var reader = XmlReader.Create(admxPath, settings);

    var records = new List<ControlRecord>();
    string? currentNamespace = null;

    while (reader.Read())
    {
      if (reader.NodeType != XmlNodeType.Element)
        continue;

      switch (reader.LocalName)
      {
        case "target":
          currentNamespace = reader.GetAttribute("namespace")?.Trim();
          break;
        case "policy":
          var record = ParsePolicy(reader, currentNamespace, packName);
          if (record != null)
            records.Add(record);
          break;
      }
    }

    return records;
  }

  private static ControlRecord? ParsePolicy(XmlReader reader, string? policyNamespace, string packName)
  {
    var policyName = reader.GetAttribute("name")?.Trim();
    if (string.IsNullOrWhiteSpace(policyName))
      return null;

    var displayName = reader.GetAttribute("displayName")?.Trim();
    var key = reader.GetAttribute("key")?.Trim();
    var valueName = reader.GetAttribute("valueName")?.Trim();

    var title = CleanDisplayName(displayName) ?? policyName;
    var keyText = string.IsNullOrWhiteSpace(key) ? "unknown" : key;
    var valueText = string.IsNullOrWhiteSpace(valueName) ? "Enabled" : valueName;

    return new ControlRecord
    {
      ControlId = Guid.NewGuid().ToString("n"),
      ExternalIds = new ExternalIds
      {
        RuleId = policyName,
        BenchmarkId = policyNamespace
      },
      Title = title,
      Severity = "medium",
      Discussion = $"Registry Key: {keyText}",
      CheckText = $"Verify registry value '{valueText}' under '{keyText}'",
      FixText = $"Configure Group Policy '{policyName}'",
      IsManual = false,
      Applicability = new Applicability
      {
        OsTarget = OsTarget.Win11,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Unknown,
        Confidence = Confidence.Medium
      },
      Revision = new RevisionInfo
      {
        PackName = packName
      }
    };
  }

  private static string? CleanDisplayName(string? displayName)
  {
    if (string.IsNullOrWhiteSpace(displayName))
      return null;

    var value = displayName.Trim();
    if (value.StartsWith("$(string.", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
      return value;

    return value;
  }
}
