using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public sealed class CanonicalChecklistProjector
{
  public IReadOnlyList<LocalWorkflowChecklistItem> Project(IReadOnlyList<ImportInboxCandidate> candidates, ICollection<string>? warnings = null)
  {
    if (candidates == null)
      throw new ArgumentNullException(nameof(candidates));

    var canonical = new List<LocalWorkflowChecklistItem>();
    var seenChecklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var seenZipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var candidate in candidates)
    {
      if (candidate == null)
        continue;

      if (candidate.ArtifactKind != ImportArtifactKind.Stig && candidate.ArtifactKind != ImportArtifactKind.Scap)
        continue;

      var zipPath = candidate.ZipPath?.Trim() ?? string.Empty;
      if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
        continue;

      if (!seenZipPaths.Add(zipPath))
        continue;

      try
      {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
          if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            continue;

          ProjectEntry(entry, canonical, seenChecklist);
        }
      }
      catch (Exception ex)
      {
        warnings?.Add(Path.GetFileName(zipPath) + ": canonical projection failed (" + ex.Message + ")");
      }
    }

    return canonical
      .OrderBy(i => i.StigId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(i => i.RuleId, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static void ProjectEntry(ZipArchiveEntry entry, ICollection<LocalWorkflowChecklistItem> canonical, ISet<string> seenChecklist)
  {
    var benchmark = ReadBenchmarkRoot(entry);
    if (benchmark == null)
      return;

    var stigId = benchmark.Attribute("id")?.Value?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(stigId))
      return;

    foreach (var rule in benchmark.Descendants().Where(e => string.Equals(e.Name.LocalName, "Rule", StringComparison.OrdinalIgnoreCase)))
    {
      var ruleId = rule.Attribute("id")?.Value?.Trim() ?? string.Empty;
      if (string.IsNullOrWhiteSpace(ruleId))
        continue;

      var key = stigId + "|" + ruleId;
      if (!seenChecklist.Add(key))
        continue;

      canonical.Add(new LocalWorkflowChecklistItem
      {
        StigId = stigId,
        RuleId = ruleId
      });
    }
  }

  private static XElement? ReadBenchmarkRoot(ZipArchiveEntry entry)
  {
    try
    {
      using var stream = entry.Open();
      var settings = new XmlReaderSettings
      {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        XmlResolver = null
      };

      using var reader = XmlReader.Create(stream, settings);
      var document = XDocument.Load(reader, LoadOptions.None);
      if (document.Root == null)
        return null;

      if (string.Equals(document.Root.Name.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase))
        return document.Root;

      return document.Root
        .Descendants()
        .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase));
    }
    catch
    {
      return null;
    }
  }
}
