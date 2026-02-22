using System.Text.Json;

namespace STIGForge.Evidence;

/// <summary>
/// Scans evidence directories, builds a queryable in-memory index, and writes
/// Evidence/evidence_index.json as a flat manifest for downstream audit packaging.
/// </summary>
public sealed class EvidenceIndexService
{
  private readonly string _bundleRoot;

  public EvidenceIndexService(string bundleRoot)
  {
    _bundleRoot = bundleRoot;
  }

  /// <summary>
  /// Scan Evidence/by_control/ directories and build an evidence index.
  /// </summary>
  public async Task<EvidenceIndex> BuildIndexAsync(CancellationToken ct = default)
  {
    var index = new EvidenceIndex
    {
      BundleRoot = _bundleRoot,
      IndexedAt = DateTimeOffset.UtcNow
    };

    var byControlDir = Path.Combine(_bundleRoot, "Evidence", "by_control");
    if (!Directory.Exists(byControlDir))
    {
      index.TotalEntries = 0;
      return index;
    }

    var controlDirs = Directory.GetDirectories(byControlDir);
    foreach (var controlDir in controlDirs)
    {
      ct.ThrowIfCancellationRequested();

      var controlKey = Path.GetFileName(controlDir);
      var metadataFiles = Directory.GetFiles(controlDir, "*.json")
        .Where(f => !Path.GetFileName(f).StartsWith("_")) // skip summary files
        .ToArray();

      foreach (var metaFile in metadataFiles)
      {
        try
        {
          var json = await File.ReadAllTextAsync(metaFile, ct).ConfigureAwait(false);
          var metadata = JsonSerializer.Deserialize<EvidenceMetadata>(json, new JsonSerializerOptions
          {
            PropertyNameCaseInsensitive = true
          });

          if (metadata == null) continue;

          var baseName = Path.GetFileNameWithoutExtension(metaFile);

          // Find the sibling evidence file (same basename, different extension)
          var siblingFiles = Directory.GetFiles(controlDir, baseName + ".*")
            .Where(f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

          var evidenceFile = siblingFiles.FirstOrDefault();
          var evidenceDir = Path.Combine(_bundleRoot, "Evidence");
          var relativePath = evidenceFile != null
            ? Path.GetRelativePath(evidenceDir, evidenceFile)
            : string.Empty;

          index.Entries.Add(new EvidenceIndexEntry
          {
            EvidenceId = baseName,
            ControlKey = controlKey,
            RuleId = metadata.RuleId,
            ControlId = metadata.ControlId,
            Title = metadata.Title,
            Type = metadata.Type,
            Source = metadata.Source,
            TimestampUtc = metadata.TimestampUtc,
            Sha256 = metadata.Sha256,
            RelativePath = relativePath,
            RunId = metadata.RunId,
            StepName = metadata.StepName,
            SupersedesEvidenceId = metadata.SupersedesEvidenceId,
            Tags = metadata.Tags
          });
        }
        catch
        {
          // Skip malformed metadata files
        }
      }
    }

    // Sort by ControlKey then TimestampUtc for deterministic output
    index.Entries = index.Entries
      .OrderBy(e => e.ControlKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(e => e.TimestampUtc, StringComparer.Ordinal)
      .ToList();

    index.TotalEntries = index.Entries.Count;
    return index;
  }

  /// <summary>
  /// Get all evidence entries for a specific control.
  /// </summary>
  public static List<EvidenceIndexEntry> GetEvidenceForControl(EvidenceIndex index, string controlKey)
  {
    return index.Entries
      .Where(e => string.Equals(e.ControlKey, controlKey, StringComparison.OrdinalIgnoreCase))
      .ToList();
  }

  /// <summary>
  /// Get all evidence entries of a specific type.
  /// </summary>
  public static List<EvidenceIndexEntry> GetEvidenceByType(EvidenceIndex index, string type)
  {
    return index.Entries
      .Where(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
      .ToList();
  }

  /// <summary>
  /// Get all evidence entries from a specific run.
  /// </summary>
  public static List<EvidenceIndexEntry> GetEvidenceByRun(EvidenceIndex index, string runId)
  {
    return index.Entries
      .Where(e => !string.IsNullOrWhiteSpace(e.RunId) &&
                  string.Equals(e.RunId, runId, StringComparison.OrdinalIgnoreCase))
      .ToList();
  }

  /// <summary>
  /// Get all evidence entries with a specific tag key-value pair.
  /// </summary>
  public static List<EvidenceIndexEntry> GetEvidenceByTag(EvidenceIndex index, string key, string value)
  {
    return index.Entries
      .Where(e => e.Tags != null &&
                  e.Tags.TryGetValue(key, out var tagValue) &&
                  string.Equals(tagValue, value, StringComparison.OrdinalIgnoreCase))
      .ToList();
  }

  /// <summary>
  /// Build lineage chain by following SupersedesEvidenceId backwards.
  /// Returns the chain from the given entry back to the oldest ancestor.
  /// </summary>
  public static List<EvidenceIndexEntry> GetLineageChain(EvidenceIndex index, string evidenceId)
  {
    var chain = new List<EvidenceIndexEntry>();
    var lookup = index.Entries.ToDictionary(e => e.EvidenceId, e => e, StringComparer.OrdinalIgnoreCase);
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var currentId = evidenceId;
    while (!string.IsNullOrWhiteSpace(currentId) && !visited.Contains(currentId))
    {
      visited.Add(currentId);
      if (lookup.TryGetValue(currentId, out var entry))
      {
        chain.Add(entry);
        currentId = entry.SupersedesEvidenceId;
      }
      else
      {
        break;
      }
    }

    return chain;
  }

  /// <summary>
  /// Write the evidence index to Evidence/evidence_index.json.
  /// </summary>
  public async Task WriteIndexAsync(EvidenceIndex index, CancellationToken ct = default)
  {
    var evidenceDir = Path.Combine(_bundleRoot, "Evidence");
    Directory.CreateDirectory(evidenceDir);

    var indexPath = Path.Combine(evidenceDir, "evidence_index.json");
    var json = JsonSerializer.Serialize(index, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    await File.WriteAllTextAsync(indexPath, json, ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Read an existing evidence index from Evidence/evidence_index.json.
  /// Returns null if the file does not exist.
  /// </summary>
  public async Task<EvidenceIndex?> ReadIndexAsync(CancellationToken ct = default)
  {
    var indexPath = Path.Combine(_bundleRoot, "Evidence", "evidence_index.json");
    if (!File.Exists(indexPath))
      return null;

    var json = await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false);
    return JsonSerializer.Deserialize<EvidenceIndex>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });
  }
}
