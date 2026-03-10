using System.Text;
using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Apply.OrgSettings;

/// <summary>
/// Parses PowerSTIG OrgSettings XML files to discover organizational values
/// that require user input, and generates completed OrgSettings XML from
/// user-provided <see cref="OrgSettingsProfile"/> answers.
/// </summary>
public static class OrgSettingsParser
{
  /// <summary>
  /// Well-known rule categories inferred from rule ID patterns and PowerSTIG rule types.
  /// </summary>
  private static readonly (string Pattern, string Category, string Description)[] KnownRulePatterns =
  [
    ("RootCertificate", "Certificate", "DoD Root CA certificate thumbprint"),
    ("SecurityOption", "Security Option", "Security option value (e.g., account lockout, legal banner)"),
    ("ServiceRule", "Service", "Windows service configuration"),
    ("RegistryRule", "Registry", "Registry value requiring organizational input"),
    ("AuditPolicy", "Audit Policy", "Audit policy configuration"),
    ("AccountPolicy", "Account Policy", "Account policy setting"),
  ];

  /// <summary>
  /// Scans a PowerSTIG module directory for OrgSettings XML files matching the
  /// given OS target and returns all entries that have empty values (require user input).
  /// </summary>
  public static List<OrgSettingEntry> DiscoverEmptySettings(
    string powerStigModulePath,
    OsTarget osTarget,
    RoleTemplate role = RoleTemplate.MemberServer)
  {
    var results = new List<OrgSettingEntry>();
    var stigDataDir = FindStigDataDirectory(powerStigModulePath);
    if (stigDataDir == null)
      return results;

    var osPattern = osTarget switch
    {
      OsTarget.Server2019 => "WindowsServer-2019",
      OsTarget.Server2022 => "WindowsServer-2022",
      OsTarget.Win10 => "WindowsClient-10",
      OsTarget.Win11 => "WindowsClient-11",
      _ => null
    };

    if (osPattern == null)
      return results;

    foreach (var xmlFile in Directory.EnumerateFiles(stigDataDir, "*.xml", SearchOption.AllDirectories))
    {
      var fileName = Path.GetFileName(xmlFile);
      if (!fileName.Contains(osPattern, StringComparison.OrdinalIgnoreCase))
        continue;
      if (!fileName.Contains("OrganizationalSettings", StringComparison.OrdinalIgnoreCase)
          && !fileName.EndsWith(".org.default.xml", StringComparison.OrdinalIgnoreCase))
        continue;

      results.AddRange(ParseOrgSettingsXml(xmlFile));
    }

    // Also scan companion STIGs (Firewall, Defender, DotNet, IE)
    var companionPatterns = new[] { "WindowsFirewall", "WindowsDefender", "DotNetFramework", "InternetExplorer" };
    foreach (var companion in companionPatterns)
    {
      foreach (var xmlFile in Directory.EnumerateFiles(stigDataDir, "*.xml", SearchOption.AllDirectories))
      {
        var fileName = Path.GetFileName(xmlFile);
        if (!fileName.Contains(companion, StringComparison.OrdinalIgnoreCase))
          continue;
        if (!fileName.Contains("OrganizationalSettings", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".org.default.xml", StringComparison.OrdinalIgnoreCase))
          continue;

        results.AddRange(ParseOrgSettingsXml(xmlFile));
      }
    }

    return results;
  }

  /// <summary>
  /// Parses a single OrgSettings XML file and returns entries with empty values.
  /// </summary>
  public static List<OrgSettingEntry> ParseOrgSettingsXml(string xmlPath)
  {
    var entries = new List<OrgSettingEntry>();

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null
    };

    using var reader = XmlReader.Create(xmlPath, settings);
    var doc = new XmlDocument();
    doc.Load(reader);

    var nodes = doc.SelectNodes("//OrganizationalSetting");
    if (nodes == null)
      return entries;

    foreach (XmlNode node in nodes)
    {
      var ruleId = node.Attributes?["id"]?.Value;
      var value = node.Attributes?["value"]?.Value;
      var ruleType = node.Attributes?["type"]?.Value ?? string.Empty;

      if (string.IsNullOrWhiteSpace(ruleId))
        continue;

      // Only include entries with empty values (these need user input)
      if (!string.IsNullOrWhiteSpace(value))
        continue;

      var (category, description) = CategorizeRule(ruleId!, ruleType);
      var defaults = OrgSettingsDefaults.GetDefaults();
      var hasDefault = defaults.TryGetValue(ruleId!, out var knownDefault);

      entries.Add(new OrgSettingEntry
      {
        RuleId = ruleId!,
        Value = hasDefault ? knownDefault!.Value : string.Empty,
        Severity = GuessSeverity(ruleType),
        Category = hasDefault ? knownDefault!.Category : category,
        Description = hasDefault ? knownDefault!.Description : description,
        DefaultValue = hasDefault ? knownDefault!.Value : string.Empty,
        IsRequired = ruleType.Contains("high", StringComparison.OrdinalIgnoreCase)
      });
    }

    return entries;
  }

  /// <summary>
  /// Generates a completed OrgSettings XML file from user-provided answers.
  /// Merges answers into the original template, filling in empty values.
  /// </summary>
  public static string GenerateOrgSettingsXml(
    string templateXmlPath,
    OrgSettingsProfile profile)
  {
    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null
    };

    using var reader = XmlReader.Create(templateXmlPath, settings);
    var doc = new XmlDocument();
    doc.Load(reader);

    var answerLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in profile.Entries)
    {
      if (!string.IsNullOrWhiteSpace(entry.Value))
        answerLookup[entry.RuleId] = entry.Value;
    }

    var nodes = doc.SelectNodes("//OrganizationalSetting");
    if (nodes != null)
    {
      foreach (XmlNode node in nodes)
      {
        var ruleId = node.Attributes?["id"]?.Value;
        if (ruleId != null && answerLookup.TryGetValue(ruleId, out var answer))
        {
          if (node.Attributes?["value"] != null)
            node.Attributes!["value"]!.Value = answer;
        }
      }
    }

    using var sw = new StringWriter();
    using var xw = XmlWriter.Create(sw, new XmlWriterSettings
    {
      Indent = true,
      Encoding = Encoding.UTF8,
      OmitXmlDeclaration = false
    });
    doc.WriteTo(xw);
    xw.Flush();
    return sw.ToString();
  }

  /// <summary>
  /// Writes a completed OrgSettings XML to disk for use during DSC compilation.
  /// </summary>
  public static string WriteOrgSettingsXml(
    string templateXmlPath,
    OrgSettingsProfile profile,
    string outputDirectory)
  {
    var xml = GenerateOrgSettingsXml(templateXmlPath, profile);
    Directory.CreateDirectory(outputDirectory);
    var outputPath = Path.Combine(outputDirectory, "OrgSettings.xml");

    var tempPath = outputPath + ".tmp";
    File.WriteAllText(tempPath, xml, new UTF8Encoding(false));
    if (File.Exists(outputPath))
      File.Delete(outputPath);
    File.Move(tempPath, outputPath);

    return outputPath;
  }

  /// <summary>
  /// Locates the StigData/Processed directory within a PowerSTIG module installation.
  /// </summary>
  private static string? FindStigDataDirectory(string modulePath)
  {
    // PowerSTIG layout: PowerSTIG/{version}/StigData/Processed/
    if (Directory.Exists(modulePath))
    {
      foreach (var versionDir in Directory.GetDirectories(modulePath))
      {
        var processed = Path.Combine(versionDir, "StigData", "Processed");
        if (Directory.Exists(processed))
          return processed;
      }

      // Direct path: modulePath/StigData/Processed/
      var direct = Path.Combine(modulePath, "StigData", "Processed");
      if (Directory.Exists(direct))
        return direct;
    }

    return null;
  }

  private static (string Category, string Description) CategorizeRule(string ruleId, string ruleType)
  {
    foreach (var (pattern, category, description) in KnownRulePatterns)
    {
      if (ruleType.Contains(pattern, StringComparison.OrdinalIgnoreCase))
        return (category, $"{description} ({ruleId})");
    }

    return ("Other", $"Organizational setting requiring site-specific value ({ruleId})");
  }

  private static string GuessSeverity(string ruleType)
  {
    if (ruleType.Contains("high", StringComparison.OrdinalIgnoreCase))
      return "high";
    if (ruleType.Contains("low", StringComparison.OrdinalIgnoreCase))
      return "low";
    return "medium";
  }
}
