using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public enum ImportStage
{
  Extracting,
  Parsing,
  Validating,
  Persisting,
  Complete,
  Failed
}

public sealed class ImportCheckpoint
{
  public string PackId { get; set; } = string.Empty;

  public string ZipPath { get; set; } = string.Empty;

  public string PackName { get; set; } = string.Empty;

  public ImportStage Stage { get; set; }

  public int ParsedControlCount { get; set; }

  public string? ErrorMessage { get; set; }

  public DateTimeOffset StartedAt { get; set; }

  public DateTimeOffset? CompletedAt { get; set; }

  public void Save(string packRoot)
  {
    Directory.CreateDirectory(packRoot);
    var path = GetPath(packRoot);
    var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
  }

  public static ImportCheckpoint? Load(string packRoot)
  {
    var path = GetPath(packRoot);
    if (!File.Exists(path))
      return null;

    try
    {
      var json = File.ReadAllText(path);
      return JsonSerializer.Deserialize<ImportCheckpoint>(json);
    }
    catch
    {
      return null;
    }
  }

  private static string GetPath(string packRoot)
  {
    return Path.Combine(packRoot, "import_checkpoint.json");
  }
}

public enum ConflictSeverity
{
  Info,
  Warning,
  Error
}

public sealed class ControlConflict
{
  public string ControlId { get; set; } = string.Empty;

  public ConflictSeverity Severity { get; set; }

  public string Reason { get; set; } = string.Empty;

  public string ExistingPackId { get; set; } = string.Empty;

  public string NewPackId { get; set; } = string.Empty;

  public IReadOnlyList<string> Differences { get; set; } = Array.Empty<string>();
}

public sealed class ConflictDetector
{
  private readonly IContentPackRepository _packs;
  private readonly IControlRepository _controls;

  public ConflictDetector(IContentPackRepository packs, IControlRepository controls)
  {
    _packs = packs;
    _controls = controls;
  }

  public async Task<List<ControlConflict>> DetectConflictsAsync(
    string newPackId,
    IReadOnlyList<ControlRecord> incomingControls,
    CancellationToken ct)
  {
    var conflicts = new List<ControlConflict>();

    var duplicates = incomingControls
      .Where(c => !string.IsNullOrWhiteSpace(c.ControlId))
      .GroupBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() > 1)
      .ToList();

    foreach (var duplicate in duplicates)
    {
      conflicts.Add(new ControlConflict
      {
        ControlId = duplicate.Key,
        Severity = ConflictSeverity.Error,
        Reason = "Duplicate control ID in incoming content pack.",
        ExistingPackId = string.Empty,
        NewPackId = newPackId,
        Differences = new[] { $"Duplicate count: {duplicate.Count()}" }
      });
    }

    var existingPacks = await _packs.ListAsync(ct).ConfigureAwait(false);
    foreach (var existingPack in existingPacks)
    {
      if (string.Equals(existingPack.PackId, newPackId, StringComparison.OrdinalIgnoreCase))
        continue;

      var existingControls = await _controls.ListControlsAsync(existingPack.PackId, ct).ConfigureAwait(false);
      if (existingControls.Count == 0)
        continue;

      var existingById = existingControls
        .Where(c => !string.IsNullOrWhiteSpace(c.ControlId))
        .ToDictionary(c => c.ControlId, c => c, StringComparer.OrdinalIgnoreCase);

      foreach (var incoming in incomingControls)
      {
        if (string.IsNullOrWhiteSpace(incoming.ControlId))
          continue;

        if (!existingById.TryGetValue(incoming.ControlId, out var existing))
          continue;

        var differences = FindDifferences(existing, incoming);
        if (differences.Count == 0)
          continue;

        conflicts.Add(new ControlConflict
        {
          ControlId = incoming.ControlId,
          Severity = DetermineSeverity(existing, incoming, differences),
          Reason = "Control exists in another pack with differing canonical fields.",
          ExistingPackId = existingPack.PackId,
          NewPackId = newPackId,
          Differences = differences
        });
      }
    }

    return conflicts;
  }

  private static List<string> FindDifferences(ControlRecord existing, ControlRecord incoming)
  {
    var differences = new List<string>();

    AddDifferenceIfChanged(differences, "title", existing.Title, incoming.Title, StringComparison.Ordinal);
    AddDifferenceIfChanged(differences, "severity", existing.Severity, incoming.Severity, StringComparison.OrdinalIgnoreCase);
    AddDifferenceIfChanged(differences, "discussion", existing.Discussion, incoming.Discussion, StringComparison.Ordinal);
    AddDifferenceIfChanged(differences, "check_text", existing.CheckText, incoming.CheckText, StringComparison.Ordinal);
    AddDifferenceIfChanged(differences, "fix_text", existing.FixText, incoming.FixText, StringComparison.Ordinal);
    AddDifferenceIfChanged(differences, "vuln_id", existing.ExternalIds.VulnId, incoming.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase);
    AddDifferenceIfChanged(differences, "rule_id", existing.ExternalIds.RuleId, incoming.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase);

    return differences;
  }

  private static void AddDifferenceIfChanged(List<string> differences, string name, string? existing, string? incoming, StringComparison comparison)
  {
    var existingValue = existing?.Trim() ?? string.Empty;
    var incomingValue = incoming?.Trim() ?? string.Empty;
    if (!string.Equals(existingValue, incomingValue, comparison))
      differences.Add(name);
  }

  private static ConflictSeverity DetermineSeverity(ControlRecord existing, ControlRecord incoming, IReadOnlyList<string> differences)
  {
    if (differences.Contains("rule_id", StringComparer.OrdinalIgnoreCase)
        || differences.Contains("vuln_id", StringComparer.OrdinalIgnoreCase))
      return ConflictSeverity.Error;

    if (differences.Contains("severity", StringComparer.OrdinalIgnoreCase)
        || differences.Contains("check_text", StringComparer.OrdinalIgnoreCase)
        || differences.Contains("fix_text", StringComparer.OrdinalIgnoreCase))
      return ConflictSeverity.Warning;

    return ConflictSeverity.Info;
  }
}
