using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
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

    var zips = EnumerateZipFiles(inboxFolder, warnings, ct);

    foreach (var zip in zips)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var detected = await BuildCandidatesAsync(zip, ct).ConfigureAwait(false);
        candidates.AddRange(detected);
      }
      catch (OperationCanceledException)
      {
        throw;
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

  public async Task<ImportInboxScanResult> ScanWithCanonicalChecklistAsync(
    string inboxFolder,
    CancellationToken ct,
    CanonicalChecklistProjector? canonicalChecklistProjector = null)
  {
    var scan = await ScanAsync(inboxFolder, ct).ConfigureAwait(false);
    var warnings = scan.Warnings.ToList();
    var projector = canonicalChecklistProjector ?? new CanonicalChecklistProjector();
    var canonicalChecklist = projector.Project(scan.Candidates, warnings);

    return new ImportInboxScanResult
    {
      Candidates = scan.Candidates,
      Warnings = warnings,
      CanonicalChecklist = canonicalChecklist
    };
  }

  private static List<string> EnumerateZipFiles(string rootFolder, ICollection<string> warnings, CancellationToken ct)
  {
    var directories = new Stack<string>();
    var zipFiles = new List<string>();
    directories.Push(rootFolder);

    while (directories.Count > 0)
    {
      ct.ThrowIfCancellationRequested();
      var current = directories.Pop();

      try
      {
        var files = Directory.GetFiles(current, "*.zip", SearchOption.TopDirectoryOnly)
          .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
          .ToList();
        zipFiles.AddRange(files);
      }
      catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
      {
        warnings.Add("Skipping inaccessible folder files: " + current + " (" + ex.Message + ")");
      }

      try
      {
        var children = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly)
          .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
          .ToList();
        for (var i = children.Count - 1; i >= 0; i--)
          directories.Push(children[i]);
      }
      catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
      {
        warnings.Add("Skipping inaccessible subdirectories: " + current + " (" + ex.Message + ")");
      }
    }

    return zipFiles
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private async Task<IReadOnlyList<ImportInboxCandidate>> BuildCandidatesAsync(string zipPath, CancellationToken ct)
  {
    var fileName = Path.GetFileName(zipPath);
    var sha256 = await _hash.Sha256FileAsync(zipPath, ct).ConfigureAwait(false);
    var candidates = new List<ImportInboxCandidate>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    using var archive = ZipFile.OpenRead(zipPath);
    var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
    var namesLower = names.Select(n => n.ToLowerInvariant()).ToList();
    var importedFrom = DetectProvenance(zipPath, namesLower);
    var isNiwcEnhanced = DetectNiwcEnhanced(zipPath, namesLower);

    if (namesLower.Any(n => n.EndsWith("evaluate-stig.ps1", StringComparison.Ordinal)))
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.EvaluateStig;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:evaluate-stig";
      candidate.Reasons.Add("Detected Evaluate-STIG.ps1 in archive.");
      AddUniqueCandidate(candidates, seen, candidate);
    }

    if (namesLower.Any(n => n.EndsWith("scc.exe", StringComparison.Ordinal)))
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.Scc;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:scc";
      candidate.Reasons.Add("Detected scc.exe in archive.");
      AddUniqueCandidate(candidates, seen, candidate);
    }

    if (namesLower.Any(n => n.EndsWith("powerstig.psd1", StringComparison.Ordinal) || n.EndsWith("powerstig.psm1", StringComparison.Ordinal)))
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Tool;
      candidate.ToolKind = ToolArtifactKind.PowerStig;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "tool:powerstig";
      candidate.Reasons.Add("Detected PowerSTIG module files in archive.");
      AddUniqueCandidate(candidates, seen, candidate);
    }

    var xmlEntries = archive.Entries
      .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
      .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var benchmarkEntries = new List<ZipArchiveEntry>();
    var scapDataStreamEntries = new List<ZipArchiveEntry>();
    foreach (var xmlEntry in xmlEntries)
    {
      var xmlFileName = Path.GetFileName(xmlEntry.FullName);
      if (xmlFileName.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        benchmarkEntries.Add(xmlEntry);
        continue;
      }

      if (!TryReadXmlRootLocalName(xmlEntry, out var rootLocalName))
        continue;

      if (string.Equals(rootLocalName, "Benchmark", StringComparison.OrdinalIgnoreCase))
      {
        benchmarkEntries.Add(xmlEntry);
        continue;
      }

      if (string.Equals(rootLocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase))
        scapDataStreamEntries.Add(xmlEntry);
    }

    var hasXccdf = benchmarkEntries.Count > 0 || scapDataStreamEntries.Count > 0;
    var hasAdmx = namesLower.Any(n => n.EndsWith(".admx", StringComparison.Ordinal));
    var hasLocalPolicies = namesLower.Any(n =>
      n.IndexOf("support files/local policies", StringComparison.Ordinal) >= 0);
    var hasDomainGpoObjects = namesLower.Any(n =>
      (n.StartsWith("gpos/", StringComparison.Ordinal)
      || n.IndexOf("/gpos/", StringComparison.Ordinal) >= 0)
      && n.IndexOf("/domainsysvol/gpo/", StringComparison.Ordinal) >= 0);
    var hasGpo = hasLocalPolicies || hasDomainGpoObjects;

    if (hasGpo)
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Gpo;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = "gpo:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
      if (hasLocalPolicies)
        candidate.Reasons.Add("Detected .Support Files/Local Policies content.");
      if (hasDomainGpoObjects)
        candidate.Reasons.Add("Detected domain GPO structure under /gpos/ (DomainSysvol/GPO).");
      AddUniqueCandidate(candidates, seen, candidate);
    }

    if (hasAdmx)
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Admx;
      candidate.Confidence = DetectionConfidence.High;
      candidate.ContentKey = BuildAdmxContentKey(archive, zipPath);
      candidate.Reasons.Add("Detected ADMX templates in archive.");
      AddUniqueCandidate(candidates, seen, candidate);
    }

    if (hasXccdf)
    {
      foreach (var benchmarkEntry in benchmarkEntries)
      {
        var hasRelatedOval = HasRelatedOval(benchmarkEntry, namesLower);
        var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
        candidate.ArtifactKind = hasRelatedOval ? ImportArtifactKind.Scap : ImportArtifactKind.Stig;
        candidate.Confidence = DetectionConfidence.High;
        PopulateXccdfIdentity(candidate, benchmarkEntry, hasRelatedOval ? "scap" : "stig");
        candidate.Reasons.Add(hasRelatedOval
          ? "Detected XCCDF + OVAL signature in " + benchmarkEntry.FullName + "."
          : "Detected XCCDF benchmark signature in " + benchmarkEntry.FullName + ".");
        candidates.Add(candidate);
      }

      foreach (var dataStreamEntry in scapDataStreamEntries)
      {
        var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
        candidate.ArtifactKind = ImportArtifactKind.Scap;
        candidate.Confidence = DetectionConfidence.High;
        PopulateXccdfIdentity(candidate, dataStreamEntry, "scap");
        candidate.Reasons.Add("Detected SCAP data stream signature in " + dataStreamEntry.FullName + ".");
        candidates.Add(candidate);
      }
    }

    if (candidates.Count == 0)
    {
      var nestedZipEntries = namesLower
        .Where(n => n.EndsWith(".zip", StringComparison.Ordinal))
        .ToList();

      if (nestedZipEntries.Count > 0)
      {
        var hasNestedScap = nestedZipEntries.Any(n => n.IndexOf("scap", StringComparison.Ordinal) >= 0);
        var hasNestedStig = nestedZipEntries.Any(n =>
          n.IndexOf("stig", StringComparison.Ordinal) >= 0
          || n.IndexOf("srg", StringComparison.Ordinal) >= 0);

        if (hasNestedScap || hasNestedStig)
        {
          var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
          candidate.ArtifactKind = hasNestedScap ? ImportArtifactKind.Scap : ImportArtifactKind.Stig;
          candidate.Confidence = DetectionConfidence.Medium;
          candidate.ContentKey = (hasNestedScap ? "scap:" : "stig:") + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
          candidate.Reasons.Add("Detected nested STIG/SCAP ZIP archive entries.");
          AddUniqueCandidate(candidates, seen, candidate);
        }
      }
    }

    if (candidates.Count == 0)
    {
      var candidate = CreateCandidate(zipPath, fileName, sha256, importedFrom, isNiwcEnhanced);
      candidate.ArtifactKind = ImportArtifactKind.Unknown;
      candidate.Confidence = DetectionConfidence.Low;
      candidate.ContentKey = "unknown:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
      candidate.Reasons.Add("No recognized STIG/SCAP/GPO/ADMX/tool signature found.");
      candidates.Add(candidate);
    }

    return candidates;
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
      if (string.IsNullOrWhiteSpace(targetNs))
      {
        targetNs = root?
          .Elements()
          .FirstOrDefault(e => e.Name.LocalName == "policyNamespaces")?
          .Elements()
          .FirstOrDefault(e => e.Name.LocalName == "target")?
          .Attribute("namespace")?
          .Value?
          .Trim() ?? string.Empty;
      }

      var revision = root?.Attribute("revision")?.Value?.Trim() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(targetNs) || !string.IsNullOrWhiteSpace(revision))
      {
        var identity = string.IsNullOrWhiteSpace(targetNs)
          ? Path.GetFileNameWithoutExtension(admxEntry.FullName).ToLowerInvariant()
          : targetNs.ToLowerInvariant();
        var normalizedRevision = string.IsNullOrWhiteSpace(revision)
          ? "unknown"
          : revision.ToLowerInvariant();

        return "admx:" + identity + ":" + normalizedRevision;
      }
    }
    catch
    {
    }

    return "admx:" + Path.GetFileNameWithoutExtension(zipPath).ToLowerInvariant();
  }

  private static void PopulateXccdfIdentity(ImportInboxCandidate candidate, ZipArchiveEntry xccdfEntry, string defaultPrefix)
  {
    try
    {
      using var stream = xccdfEntry.Open();
      var doc = XDocument.Load(stream, LoadOptions.None);
      var root = doc.Root;
      if (root == null)
      {
        candidate.ContentKey = defaultPrefix + ":" + Path.GetFileNameWithoutExtension(xccdfEntry.FullName).ToLowerInvariant();
        return;
      }

      var metadataRoot = root;
      var isDataStreamRoot = string.Equals(root.Name.LocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase);
      if (isDataStreamRoot)
      {
        var embeddedBenchmark = root.Descendants().FirstOrDefault(e =>
          string.Equals(e.Name.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase));
        if (embeddedBenchmark != null)
          metadataRoot = embeddedBenchmark;
      }

      var benchmarkId = metadataRoot.Attribute("id")?.Value?.Trim() ?? string.Empty;
      if (isDataStreamRoot && ReferenceEquals(metadataRoot, root))
        benchmarkId = string.Empty;

      var benchmarkVersion = metadataRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value?.Trim() ?? string.Empty;
      var benchmarkTitle = metadataRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value?.Trim() ?? string.Empty;
      var statusDateRaw = metadataRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "status")?.Attribute("date")?.Value?.Trim();

      if (!string.IsNullOrWhiteSpace(statusDateRaw) && DateTimeOffset.TryParse(statusDateRaw, out var parsedDate))
        candidate.BenchmarkDate = parsedDate;

      var releasePlainText = metadataRoot.Elements()
        .FirstOrDefault(e => e.Name.LocalName == "plain-text")?.Value ?? string.Empty;
      var versionTag = ParseVersionTag(releasePlainText)
        ?? ParseVersionTag(benchmarkVersion)
        ?? ParseVersionTag(xccdfEntry.FullName)
        ?? ParseVersionTag(candidate.FileName)
        ?? string.Empty;

      candidate.VersionTag = versionTag;

      var keyIdentity = !string.IsNullOrWhiteSpace(benchmarkId)
        ? benchmarkId
        : !string.IsNullOrWhiteSpace(benchmarkTitle)
          ? benchmarkTitle
          : Path.GetFileNameWithoutExtension(xccdfEntry.FullName);

      candidate.ContentKey = defaultPrefix + ":" + NormalizeKey(keyIdentity);
    }
    catch
    {
      candidate.ContentKey = defaultPrefix + ":" + Path.GetFileNameWithoutExtension(xccdfEntry.FullName).ToLowerInvariant();
    }
  }

  private static ImportInboxCandidate CreateCandidate(string zipPath, string fileName, string sha256, ImportProvenance importedFrom, bool isNiwcEnhanced)
  {
    return new ImportInboxCandidate
    {
      ZipPath = zipPath,
      FileName = fileName,
      Sha256 = sha256,
      ImportedFrom = importedFrom,
      IsNiwcEnhanced = isNiwcEnhanced
    };
  }

  private static ImportProvenance DetectProvenance(string zipPath, IReadOnlyList<string> entryPathsLower)
  {
    var source = (zipPath ?? string.Empty).ToLowerInvariant();
    if (source.IndexOf("consolidated", StringComparison.Ordinal) >= 0
      || source.IndexOf("bundle", StringComparison.Ordinal) >= 0
      || entryPathsLower.Any(p => p.IndexOf("consolidated", StringComparison.Ordinal) >= 0))
      return ImportProvenance.ConsolidatedBundle;

    if (source.EndsWith(".zip", StringComparison.Ordinal))
      return ImportProvenance.StandaloneZip;

    return ImportProvenance.Other;
  }

  private static bool DetectNiwcEnhanced(string zipPath, IReadOnlyList<string> entryPathsLower)
  {
    var source = (zipPath ?? string.Empty).ToLowerInvariant();
    var sourceLooksNiwc = source.IndexOf("niwc", StringComparison.Ordinal) >= 0
      || source.IndexOf("atlantic", StringComparison.Ordinal) >= 0;
    var sourceLooksEnhanced = source.IndexOf("enhanced", StringComparison.Ordinal) >= 0
      || source.IndexOf("consolidated", StringComparison.Ordinal) >= 0;

    if (sourceLooksNiwc && sourceLooksEnhanced)
      return true;

    return entryPathsLower.Any(p => p.IndexOf("niwc", StringComparison.Ordinal) >= 0
      && (p.IndexOf("enhanced", StringComparison.Ordinal) >= 0 || p.IndexOf("consolidated", StringComparison.Ordinal) >= 0));
  }

  private static void AddUniqueCandidate(ICollection<ImportInboxCandidate> candidates, ISet<string> seen, ImportInboxCandidate candidate)
  {
    var key = candidate.ArtifactKind + "|" + candidate.ToolKind + "|" + candidate.ContentKey;
    if (!seen.Add(key))
      return;

    candidates.Add(candidate);
  }

  private static bool TryReadXmlRootLocalName(ZipArchiveEntry xmlEntry, out string rootLocalName)
  {
    rootLocalName = string.Empty;

    try
    {
      using var stream = xmlEntry.Open();
      var settings = new XmlReaderSettings
      {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        XmlResolver = null
      };

      using var reader = XmlReader.Create(stream, settings);
      while (reader.Read())
      {
        if (reader.NodeType != XmlNodeType.Element)
          continue;

        rootLocalName = reader.LocalName;
        return !string.IsNullOrWhiteSpace(rootLocalName);
      }
    }
    catch
    {
    }

    return false;
  }

  private static bool HasRelatedOval(ZipArchiveEntry xccdfEntry, IReadOnlyList<string> namesLower)
  {
    var xccdfPath = xccdfEntry.FullName.Replace('\\', '/').ToLowerInvariant();
    var xccdfFileName = Path.GetFileNameWithoutExtension(xccdfPath);
    var xccdfDirectory = Path.GetDirectoryName(xccdfPath)?.Replace('\\', '/');

    if (!string.IsNullOrWhiteSpace(xccdfDirectory))
    {
      var directoryPrefix = xccdfDirectory + "/";
      if (namesLower.Any(n => n.StartsWith(directoryPrefix, StringComparison.Ordinal)
        && n.EndsWith(".xml", StringComparison.Ordinal)
        && n.Contains("oval")))
      {
        return true;
      }
    }

    var xccdfStemToken = Regex.Replace(xccdfFileName, "xccdf", string.Empty, RegexOptions.IgnoreCase);
    if (!string.IsNullOrWhiteSpace(xccdfStemToken)
      && namesLower.Any(n => n.EndsWith(".xml", StringComparison.Ordinal)
        && n.Contains("oval")
        && n.Contains(xccdfStemToken)))
    {
      return true;
    }

    return false;
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
