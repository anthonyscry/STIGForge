using System.Xml;
using System.Xml.Linq;
using STIGForge.Content.Models;

namespace STIGForge.Content.Import;

public static class OvalParser
{
  private static readonly XNamespace OvalNs = "http://oval.mitre.org/XMLSchema/oval-definitions-5";

  public static IReadOnlyList<OvalDefinition> Parse(string xmlPath)
  {
    if (!File.Exists(xmlPath))
      throw new FileNotFoundException("OVAL XML file not found", xmlPath);

    var doc = LoadSecureXml(xmlPath);
    var definitions = doc.Descendants(OvalNs + "definition").ToList();
    var results = new List<OvalDefinition>(definitions.Count);

    foreach (var definition in definitions)
    {
      var id = definition.Attribute("id")?.Value?.Trim();
      if (string.IsNullOrWhiteSpace(id))
        continue;

      var metadata = definition.Element(OvalNs + "metadata");
      var title = metadata?.Element(OvalNs + "title")?.Value?.Trim();
      var description = metadata?.Element(OvalNs + "description")?.Value?.Trim();

      var definitionId = id;

      results.Add(new OvalDefinition
      {
        DefinitionId = definitionId!,
        Title = title,
        Class = definition.Attribute("class")?.Value?.Trim(),
        Description = description
      });
    }

    return results;
  }

  private static XDocument LoadSecureXml(string xmlPath)
  {
    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreWhitespace = true,
      MaxCharactersFromEntities = 1024,
      MaxCharactersInDocument = 20_000_000,
      Async = false
    };

    try
    {
      using var reader = XmlReader.Create(xmlPath, settings);
      return XDocument.Load(reader, LoadOptions.None);
    }
    catch (XmlException ex)
    {
      throw new ParsingException($"[OVAL-XML-001] Failed to parse OVAL XML '{xmlPath}': {ex.Message}", xmlPath, null, ex);
    }
  }
}
