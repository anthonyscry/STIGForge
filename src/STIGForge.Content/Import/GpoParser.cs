using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class GpoParser
{
  public static IReadOnlyList<ControlRecord> Parse(string admxPath, string packName)
  {
    if (!File.Exists(admxPath))
      throw new FileNotFoundException("ADMX file not found", admxPath);

    var osTarget = DetectOsTargetFromContext(packName, admxPath);

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
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
          var record = ParsePolicy(reader, currentNamespace, packName, osTarget);
          if (record != null)
            records.Add(record);
          break;
      }
    }

    return records;
  }

  /// <summary>
  /// Detect OS target from the pack name and file path context.
  /// DISA GPO bundles use folder names like "Windows 11", "Windows Server 2019", etc.
  /// The importer creates packs named "Local Policy – Windows 11", etc.
  /// </summary>
  internal static OsTarget DetectOsTargetFromContext(string packName, string filePath)
  {
    // Check pack name first (most reliable — set by importer from folder name)
    var context = (packName + " " + filePath).Replace('_', ' ');

    if (context.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0
        || context.IndexOf("Win11", StringComparison.OrdinalIgnoreCase) >= 0)
      return OsTarget.Win11;

    if (context.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0
        || context.IndexOf("Win10", StringComparison.OrdinalIgnoreCase) >= 0)
      return OsTarget.Win10;

    if (context.IndexOf("Server 2022", StringComparison.OrdinalIgnoreCase) >= 0)
      return OsTarget.Server2022;

    if (context.IndexOf("Server 2019", StringComparison.OrdinalIgnoreCase) >= 0)
      return OsTarget.Server2019;

    // Check parent directory names in the file path
    try
    {
      var dir = Path.GetDirectoryName(filePath);
      while (!string.IsNullOrWhiteSpace(dir))
      {
        var dirName = new DirectoryInfo(dir).Name;
        if (dirName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0
            || dirName.IndexOf("Win11", StringComparison.OrdinalIgnoreCase) >= 0)
          return OsTarget.Win11;
        if (dirName.IndexOf("Server 2022", StringComparison.OrdinalIgnoreCase) >= 0)
          return OsTarget.Server2022;
        if (dirName.IndexOf("Server 2019", StringComparison.OrdinalIgnoreCase) >= 0)
          return OsTarget.Server2019;
        if (dirName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0
            || dirName.IndexOf("Win10", StringComparison.OrdinalIgnoreCase) >= 0)
          return OsTarget.Win10;
        dir = Path.GetDirectoryName(dir);
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("GPO path detection failed: " + ex.Message);
    }

    return OsTarget.Unknown;
  }

  private static ControlRecord? ParsePolicy(XmlReader reader, string? policyNamespace, string packName, OsTarget osTarget)
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
      Title = title!,
      Severity = "medium",
      Discussion = $"Registry Key: {keyText}",
      CheckText = $"Verify registry value '{valueText}' under '{keyText}'",
      FixText = $"Configure Group Policy '{policyName}'",
      IsManual = false,
      Applicability = new Applicability
      {
        OsTarget = osTarget,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Unknown,
        Confidence = osTarget == OsTarget.Unknown ? Confidence.Low : Confidence.Medium
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

    var value = (displayName ?? string.Empty).Trim();
    if (value.StartsWith("$(string.", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
      return value;

    return value;
  }
}
