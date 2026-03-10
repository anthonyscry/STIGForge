using System.Text.Json;
using STIGForge.Core;
using STIGForge.Core.Models;

namespace STIGForge.Apply.OrgSettings;

/// <summary>
/// Serializes and deserializes <see cref="OrgSettingsProfile"/> to/from
/// portable JSON files (.stigorgsettings.json) for cross-system export/import.
/// </summary>
public static class OrgSettingsSerializer
{
  private const string FileExtension = ".stigorgsettings.json";

  /// <summary>
  /// Saves an OrgSettingsProfile to a JSON file.
  /// Uses atomic write (temp file + rename).
  /// </summary>
  public static void Save(string filePath, OrgSettingsProfile profile)
  {
    profile.UpdatedAt = DateTimeOffset.UtcNow;
    var json = JsonSerializer.Serialize(profile, JsonOptions.Indented);

    var dir = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(dir))
      Directory.CreateDirectory(dir);

    var tempPath = filePath + ".tmp";
    File.WriteAllText(tempPath, json);
    if (File.Exists(filePath))
      File.Delete(filePath);
    File.Move(tempPath, filePath);
  }

  /// <summary>
  /// Loads an OrgSettingsProfile from a JSON file.
  /// </summary>
  public static OrgSettingsProfile? Load(string filePath)
  {
    if (!File.Exists(filePath))
      return null;

    var json = File.ReadAllText(filePath);
    return JsonSerializer.Deserialize<OrgSettingsProfile>(json, JsonOptions.CaseInsensitive);
  }

  /// <summary>
  /// Gets the default file path for an OrgSettings profile in the given output directory.
  /// </summary>
  public static string GetDefaultPath(string outputDirectory, string profileName)
  {
    var safeName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(outputDirectory, safeName + FileExtension);
  }

  /// <summary>
  /// Merges saved answers from an existing profile into a newly discovered set of entries.
  /// Preserves user-provided values for entries that still exist in the new template.
  /// </summary>
  public static void MergeAnswers(List<OrgSettingEntry> entries, OrgSettingsProfile savedProfile)
  {
    var savedLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var saved in savedProfile.Entries)
    {
      if (!string.IsNullOrWhiteSpace(saved.Value))
        savedLookup[saved.RuleId] = saved.Value;
    }

    foreach (var entry in entries)
    {
      if (savedLookup.TryGetValue(entry.RuleId, out var savedValue))
        entry.Value = savedValue;
    }
  }
}
