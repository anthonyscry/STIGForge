using System.Xml.Linq;
using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class NessusImporter
{
  public IReadOnlyList<NessusFinding> Import(string nessusFilePath)
  {
    if (!File.Exists(nessusFilePath))
      throw new FileNotFoundException("Nessus file not found", nessusFilePath);

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreWhitespace = true,
      MaxCharactersFromEntities = 1024,
      MaxCharactersInDocument = 40_000_000
    };

    using var reader = XmlReader.Create(nessusFilePath, settings);
    var doc = XDocument.Load(reader, LoadOptions.None);
    var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

    var findings = new List<NessusFinding>();

    var reportHosts = doc.Descendants(ns + "ReportHost");
    foreach (var host in reportHosts)
    {
      var hostName = host.Attribute("name")?.Value ?? "unknown";
      var hostProps = ParseHostProperties(host, ns);

      var reportItems = host.Descendants(ns + "ReportItem");
      foreach (var item in reportItems)
      {
        var finding = ParseReportItem(item, hostName, hostProps, ns);
        if (finding != null)
          findings.Add(finding);
      }
    }

    return findings.OrderByDescending(f => f.Severity).ToList();
  }

  private static Dictionary<string, string> ParseHostProperties(XElement reportHost, XNamespace ns)
  {
    var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var hostProps = reportHost.Element(ns + "HostProperties");
    if (hostProps == null) return props;

    foreach (var tag in hostProps.Elements(ns + "tag"))
    {
      var name = tag.Attribute("name")?.Value;
      var value = tag.Value;
      if (!string.IsNullOrWhiteSpace(name))
        props[name] = value;
    }

    return props;
  }

  private static NessusFinding? ParseReportItem(XElement item, string hostName, Dictionary<string, string> hostProps, XNamespace ns)
  {
    var pluginId = item.Attribute("pluginID")?.Value;
    var pluginName = item.Attribute("pluginName")?.Value;
    var severity = ParseSeverity(item.Attribute("severity")?.Value);

    if (string.IsNullOrWhiteSpace(pluginId))
      return null;

    return new NessusFinding
    {
      PluginId = pluginId,
      PluginName = pluginName ?? "Unknown",
      HostName = hostName,
      HostIp = hostProps.GetValueOrDefault("host-ip", hostName),
      OperatingSystem = hostProps.GetValueOrDefault("operating-system", "Unknown"),
      Severity = severity,
      Port = item.Attribute("port")?.Value ?? "0",
      Protocol = item.Attribute("protocol")?.Value ?? "tcp",
      Service = item.Attribute("svc_name")?.Value ?? "",
      Synopsis = GetChildValue(item, "synopsis", ns),
      Description = GetChildValue(item, "description", ns),
      Solution = GetChildValue(item, "solution", ns),
      SeeAlso = GetChildValue(item, "see_also", ns),
      CvssScore = ParseDouble(GetChildValue(item, "cvss_base_score", ns)),
      CvssVector = GetChildValue(item, "cvss_vector", ns),
      CveList = item.Elements(ns + "cve").Select(e => e.Value).ToList(),
      CweList = item.Elements(ns + "cwe").Select(e => e.Value).ToList(),
      XrefList = item.Elements(ns + "xref").Select(e => e.Value).ToList(),
      StigRuleId = ExtractStigRuleId(item, ns),
      StigVersion = ExtractStigVersion(item, ns),
      Output = GetChildValue(item, "plugin_output", ns)
    };
  }

  private static int ParseSeverity(string? severity)
  {
    return int.TryParse(severity, out var s) ? s : 0;
  }

  private static double ParseDouble(string? value)
  {
    return double.TryParse(value, out var d) ? d : 0.0;
  }

  private static string GetChildValue(XElement parent, string childName, XNamespace ns)
  {
    return parent.Element(ns + childName)?.Value?.Trim() ?? string.Empty;
  }

  private static string? ExtractStigRuleId(XElement item, XNamespace ns)
  {
    var stigElements = item.Elements(ns + "stig_severity");
    foreach (var stig in stigElements)
    {
      var ruleId = stig.Attribute("rule_id")?.Value;
      if (!string.IsNullOrWhiteSpace(ruleId))
        return ruleId;
    }

    var description = GetChildValue(item, "description", ns);
    var match = System.Text.RegularExpressions.Regex.Match(description, @"SV-\d+r\d+_rule");
    if (match.Success)
      return match.Value;

    var seeAlso = GetChildValue(item, "see_also", ns);
    match = System.Text.RegularExpressions.Regex.Match(seeAlso, @"SV-\d+r\d+_rule");
    if (match.Success)
      return match.Value;

    return null;
  }

  private static string? ExtractStigVersion(XElement item, XNamespace ns)
  {
    var stigElements = item.Elements(ns + "stig_severity");
    foreach (var stig in stigElements)
    {
      var version = stig.Attribute("version")?.Value;
      if (!string.IsNullOrWhiteSpace(version))
        return version;
    }
    return null;
  }
}

public sealed class NessusFinding
{
  public string PluginId { get; set; } = string.Empty;
  public string PluginName { get; set; } = string.Empty;
  public string HostName { get; set; } = string.Empty;
  public string HostIp { get; set; } = string.Empty;
  public string OperatingSystem { get; set; } = string.Empty;
  public int Severity { get; set; }
  public string Port { get; set; } = string.Empty;
  public string Protocol { get; set; } = string.Empty;
  public string Service { get; set; } = string.Empty;
  public string Synopsis { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string Solution { get; set; } = string.Empty;
  public string SeeAlso { get; set; } = string.Empty;
  public double CvssScore { get; set; }
  public string CvssVector { get; set; } = string.Empty;
  public IReadOnlyList<string> CveList { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> CweList { get; set; } = Array.Empty<string>();
  public IReadOnlyList<string> XrefList { get; set; } = Array.Empty<string>();
  public string? StigRuleId { get; set; }
  public string? StigVersion { get; set; }
  public string Output { get; set; } = string.Empty;

  public string SeverityName => Severity switch
  {
    0 => "Info",
    1 => "Low",
    2 => "Medium",
    3 => "High",
    4 => "Critical",
    _ => "Unknown"
  };
}
