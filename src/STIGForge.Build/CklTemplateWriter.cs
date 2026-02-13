using System.Linq;
using System.Xml.Linq;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using STIGForge.Core.Models;

namespace STIGForge.Build;

public static class CklTemplateWriter
{
  public static void WriteTemplateCkls(string cklDir, IReadOnlyList<ControlRecord> controls, ContentPack pack, Profile profile)
  {
    if (string.IsNullOrWhiteSpace(cklDir))
      throw new ArgumentException("CKL output directory is required.", nameof(cklDir));
    if (controls == null)
      throw new ArgumentNullException(nameof(controls));
    if (pack == null)
      throw new ArgumentNullException(nameof(pack));
    if (profile == null)
      throw new ArgumentNullException(nameof(profile));

    Directory.CreateDirectory(cklDir);

    var groups = controls
      .GroupBy(control => control.Revision.PackName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
      .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    foreach (var group in groups)
    {
      var stigName = group.Key ?? string.Empty;
      var fileName = SanitizeFileName(stigName) + "_template.ckl";
      var filePath = Path.Combine(cklDir, fileName);
      var doc = BuildTemplateDocument(stigName, group.ToList(), pack);
      doc.Save(filePath);
    }
  }

  private static XDocument BuildTemplateDocument(string stigName, IReadOnlyList<ControlRecord> controls, ContentPack pack)
  {
    var releaseInfo = pack.ReleaseDate.HasValue
      ? pack.ReleaseDate.Value.ToString("yyyy-MM-dd")
      : string.Empty;

    var checklist = new XElement("CHECKLIST",
      new XElement("ASSET",
        new XElement("ROLE", "None"),
        new XElement("ASSET_TYPE", "Computing"),
        new XElement("HOST_NAME", Environment.MachineName),
        new XElement("HOST_IP", string.Empty),
        new XElement("HOST_MAC", string.Empty),
        new XElement("HOST_FQDN", string.Empty),
        new XElement("TARGET_COMMENT", string.Empty),
        new XElement("TECH_AREA", string.Empty),
        new XElement("TARGET_KEY", string.Empty),
        new XElement("WEB_OR_DATABASE", "false"),
        new XElement("WEB_DB_SITE", string.Empty),
        new XElement("WEB_DB_INSTANCE", string.Empty)),
      new XElement("STIGS",
        new XElement("iSTIG",
          new XElement("STIG_INFO",
            new XElement("SI_DATA",
              new XElement("SID_NAME", "stigid"),
              new XElement("SID_DATA", stigName ?? string.Empty)),
            new XElement("SI_DATA",
              new XElement("SID_NAME", "title"),
              new XElement("SID_DATA", stigName ?? string.Empty)),
            new XElement("SI_DATA",
              new XElement("SID_NAME", "releaseinfo"),
              new XElement("SID_DATA", releaseInfo ?? string.Empty))),
          BuildVulnElements(controls))));

    return new XDocument(new XDeclaration("1.0", "UTF-8", null), checklist);
  }

  private static object[] BuildVulnElements(IReadOnlyList<ControlRecord> controls)
  {
    var elements = new List<object>(controls.Count);
    foreach (var control in controls)
    {
      var vuln = new XElement("VULN",
        StigData("Vuln_Num", control.ExternalIds.VulnId ?? string.Empty),
        StigData("Severity", control.Severity ?? string.Empty),
        StigData("Rule_ID", control.ExternalIds.RuleId ?? string.Empty),
        StigData("Rule_Title", control.Title ?? string.Empty),
        StigData("Vuln_Discuss", control.Discussion ?? string.Empty),
        StigData("Check_Content", control.CheckText ?? string.Empty),
        StigData("Fix_Text", control.FixText ?? string.Empty),
        new XElement("STATUS", ControlStatusStrings.NotReviewed),
        new XElement("FINDING_DETAILS", string.Empty),
        new XElement("COMMENTS", string.Empty),
        new XElement("SEVERITY_OVERRIDE", string.Empty),
        new XElement("SEVERITY_JUSTIFICATION", string.Empty));
      elements.Add(vuln);
    }

    return elements.ToArray();
  }

  private static XElement StigData(string attribute, string data)
  {
    return new XElement("STIG_DATA",
      new XElement("VULN_ATTRIBUTE", attribute),
      new XElement("ATTRIBUTE_DATA", data));
  }

  private static string SanitizeFileName(string value)
  {
    var source = string.IsNullOrWhiteSpace(value) ? PackTypes.Stig : value.Trim();
    var chars = source.ToCharArray();
    var invalidChars = Path.GetInvalidFileNameChars();
    for (var i = 0; i < chars.Length; i++)
    {
      if (Array.IndexOf(invalidChars, chars[i]) >= 0)
        chars[i] = '_';
    }

    return new string(chars);
  }
}
