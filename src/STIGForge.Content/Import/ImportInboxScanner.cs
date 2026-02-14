using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using STIGForge.Core.Abstractions;

namespace STIGForge.Content.Import;

public sealed class ImportInboxScanner
{
  private static readonly Regex DisaVersionRegex = new(@"V\s*(\d+)\s*R\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
  private readonly IHashingService _hash;

  public ImportInboxScanner(IHashingService hash)
  {
    _hash = hash;
  }

  public async Task<ImportInboxScanResult> ScanAsync(string inboxFolder, CancellationToken ct)
  {
    var warnings = new List<string>();
    var candidates = new List<ImportInboxCandidate>();

    if (string.IsNullOrWhiteSpace(inboxFolder) || !Directory.Exists(inboxFolder))
    {
      warnings.Add("Import folder not found: " + inboxFolder);
      return new ImportInboxScanResult { Candidates = candidates, Warnings = warnings };
    }

    var zips = Directory.GetFiles(inboxFolder, "*.zip", SearchOption.AllDirectories)
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    foreach (var zip in zips)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var candidate = await BuildCandidateAsync(zip, ct).ConfigureAwait(false);
        candidates.Add(candidate);
      }
      catch (Exception ex)
      {
        warnings.Add(Path.GetFileName(zip) + ": " + ex.Message);
      }
    }

    return new ImportInboxScanResult
    {
      Candidates = candidates,
      Warnings = warnings
    };
  }

  private async Task<ImportInboxCandidate> BuildCandidateAsync(string zipPath, CancellationToken ct)
  {
    var candidate = new ImportInboxCandidate
    {
      ZipPath = zipPath,
      FileName = Path.GetFileName(zipPath),
      Sha256 = await _hash.Sha256FileAsync(zipPath, ct).ConfigureAwait(false)
    };

    using var archive = ZipFile.OpenRead(zipPath);
    var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
    var namesLower = names.Select(n => n.ToLowerInvariant()).ToList();

    if (namesLower.Any(n => n.EndsWith("evaluate-stig.ps1", StringComparison.Ordinal)))
    {
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.EvaluateStig;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:evaluate-stig";
      candidate.Reasons.Add("Detected Evaluate-STIG.ps1 in archive.");
      return candidate;
    }

    if (namesLower.Any(n => n.EndsWith("scc.exe", StringComparison.Ordinal)))
    {
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.Scc;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:scc";
      candidate.Reasons.Add("Detected scc.exe in archive.");
      return candidate;
    }

    if (namesLower.Any(n => n.EndsWith("powerstig.psd1", StringComparison.Ordinal) || n.EndsWith("powerstig.psm1", StringComparison.Ordinal)))
    {
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.PowerStig;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:powerstig";
      candidate.Reasons.Add("Detected PowerSTIG module files in archive.");
      return candidate;
    }

    var hasXccdf = namesLower.Any(n => n.EndsWith(".xml", StringComparison.Ordinal) && n.Contains("xccdf", StringComparison.Ordinal));
    var hasOval = namesLower.Any(n => n.EndsWith(".xml", StringComparison.Ordinal) && n.Contains("oval", StringComparison.Ordinal));
    var hasAdmx = namesLower.Any(n => n.EndsWith(".admx", StringComparison.Ordinal));
    var hasLocalPolicies = namesLower.Any(n => n.Contains(".support files/local policies", StringComparison.Ordinal));

    if (hasLocalPolicies)
    {
      candidate.ArtifactKind = ImportArtifactKind.Gpo;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "gpo:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
      candidate.Reasons.Add("Detected .Support Files/Local Policies content.");
      return candidate;
    }

    if (hasAdmx && !hasXccdf && !hasOval)
    {
      candidate.ArtifactKind = ImportArtifactKind.Admx;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = BuildAdmxContentKey(archive, zipPath);
      candidate.Reasons.Add("Detected ADMX templates without XCCDF/OVAL content.");
      return candidate;
    }

    if (hasXccdf && hasOval)
    {
      candidate.ArtifactKind = ImportArtifactKind.Scap;
      candidate.Confidence = DetectionConfidence.High;
      PopulateXccdfIdentity(candidate, archive, defaultPrefix: "scap");
      candidate.Reasons.Add("Detected XCCDF + OVAL signature.");
      return candidate;
    }

    if (hasXccdf)
    {
      candidate.ArtifactKind = ImportArtifactKind.Stig;
      candidate.Confidence = DetectionConfidence.High;
      PopulateXccdfIdentity(candidate, archive, defaultPrefix: "stig");
      candidate.Reasons.Add("Detected XCCDF signature.");
      return candidate;
    }

    candidate.ArtifactKind = ImportArtifactKind.Unknown;
    candidate.Confidence = DetectionConfidence.Low;
    candidate.ContentKey = "unknown:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
    candidate.Reasons.Add("No recognized STIG/SCAP/GPO/ADMX/tool signature found.");
    return candidate;
  }

  private static string BuildAdmxContentKey(ZipArchive archive, string zipPath)
  {
    var admxEntry = archive.Entries
      .FirstOrDefault(e => e.FullName.EndsWith(".admx", StringComparison.OrdinalIgnoreCase));
    if (admxEntry == null)
      return "admx:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();

    try
    {
      using var stream = admxEntry.Open();
      var doc = XDocument.Load(stream, LoadOptions.None);
      var root = doc.Root;
      var targetNs = root?.Attribute("targetNamespace")?.Value?.Trim() ?? string.Empty;
      var revision = root?.Attribute("revision")?.Value?.Trim() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(targetNs) || !string.IsNullOrWhiteSpace(revision))
        return "admx:" + targetNs.ToLowerInvariant() + ":" + revision.ToLowerInvariant();
    }
    catch
    {
    }

    return "admx:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
  }

  private static void PopulateXccdfIdentity(ImportInboxCandidate candidate, ZipArchive archive, string defaultPrefix)
  {
    var xccdfEntry = archive.Entries
      .FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(e.FullName).Contains("xccdf", StringComparison.OrdinalIgnoreCase));

    if (xccdfEntry == null)
    {
      candidate.ContentKey = defaultPrefix + ":" + Path.GetFileNameWithoutExtension(candidate.FileName).ToLowerInvariant();
      return;
    }

    try
    {
      using var stream = xccdfEntry.Open();
      var doc = XDocument.Load(stream, LoadOptions.None);
      var root = doc.Root;
      if (root == null)
      {
        candidate.ContentKey = defaultPrefix + ":" + Path.GetFileNameWithoutExtension(candidate.FileName).ToLowerInvariant();
        return;
      }

      var benchmarkId = root.Attribute("id")?.Value?.Trim() ?? string.Empty;
      var benchmarkVersion = root.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value?.Trim() ?? string.Empty;
      var benchmarkTitle = root.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value?.Trim() ?? string.Empty;
      var statusDateRaw = root.Elements().FirstOrDefault(e => e.Name.LocalName == "status")?.Attribute("date")?.Value?.Trim();

      if (!string.IsNullOrWhiteSpace(statusDateRaw) && DateTimeOffset.TryParse(statusDateRaw, out var parsedDate))
        candidate.BenchmarkDate = parsedDate;

      var releasePlainText = root.Elements()
        .FirstOrDefault(e => e.Name.LocalName == "plain-text")?.Value ?? string.Empty;
      var versionTag = ParseVersionTag(releasePlainText)
        ?? ParseVersionTag(benchmarkVersion)
        ?? ParseVersionTag(candidate.FileName)
        ?? string.Empty;

      candidate.VersionTag = versionTag;

      var keyIdentity = !string.IsNullOrWhiteSpace(benchmarkId)
        ? benchmarkId
        : !string.IsNullOrWhiteSpace(benchmarkTitle)
          ? benchmarkTitle
          : Path.GetFileNameWithoutExtension(candidate.FileName);

      candidate.ContentKey = defaultPrefix + ":" + NormalizeKey(keyIdentity);
    }
    catch
    {
      candidate.ContentKey = defaultPrefix + ":" + Path.GetFileNameWithoutExtension(candidate.FileName).ToLowerInvariant();
    }
  }

  private static string NormalizeKey(string value)
  {
    var normalized = value.Trim().ToLowerInvariant();
    normalized = Regex.Replace(normalized, @"\s+", "-");
    return normalized;
  }

  private static string? ParseVersionTag(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return null;

    var match = DisaVersionRegex.Match(text);
    if (!match.Success)
      return null;

    return "V" + match.Groups[1].Value + "R" + match.Groups[2].Value;
  }
}
