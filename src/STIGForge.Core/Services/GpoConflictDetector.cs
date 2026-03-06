using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class GpoConflictDetector
{
  private static readonly Regex RegistryPolicyTitleRegex = new(
    @"^Registry\s+Policy:\s*(?<path>.+?)\s*=\s*(?<value>.+)$",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex SecuritySettingRegex = new(
    @"setting\s+'(?<path>[^']+)'\s+is\s+configured\s+to\s+'(?<value>[^']+)'",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex RegistryCheckRegex = new(
    @"value\s+'(?<name>[^']+)'\s+under\s+'(?<path>[^']+)'",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private static readonly Regex FixValueRegex = new(
    @"=\s*'(?<value>[^']+)'",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

  private readonly IProcessRunner? _processRunner;

  public GpoConflictDetector(IProcessRunner? processRunner = null)
  {
    _processRunner = processRunner;
  }

  public async Task<IReadOnlyList<GpoConflict>> DetectConflictsAsync(string bundleRoot, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot)) throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));
    if (!Directory.Exists(bundleRoot)) throw new DirectoryNotFoundException("Bundle root not found: " + bundleRoot);

    var localSettings = LoadLocalStigSettings(bundleRoot);
    if (localSettings.Count == 0)
      return [];

    var appliedSettings = await LoadAppliedGpoSettingsAsync(localSettings.Keys, ct).ConfigureAwait(false);
    if (appliedSettings.Count == 0)
      return [];

    var conflicts = new List<GpoConflict>();
    foreach (var localSetting in localSettings)
    {
      AppliedGpoSetting? applied;
      if (!appliedSettings.TryGetValue(localSetting.Key, out applied))
        continue;

      var localValue = NormalizeValue(localSetting.Value);
      var gpoValue = NormalizeValue(applied.Value);
      if (string.Equals(localValue, gpoValue, StringComparison.OrdinalIgnoreCase))
        continue;

      conflicts.Add(new GpoConflict
      {
        SettingPath = localSetting.Key,
        LocalValue = localSetting.Value,
        GpoValue = applied.Value,
        GpoName = applied.GpoName,
        ConflictType = "ValueOverride"
      });
    }

    return conflicts
      .OrderBy(c => c.SettingPath, StringComparer.OrdinalIgnoreCase)
      .ThenBy(c => c.GpoName, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static IReadOnlyDictionary<string, string> LoadLocalStigSettings(string bundleRoot)
  {
    var controlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var controls = JsonSerializer.Deserialize<List<ControlRecord>>(
      File.ReadAllText(controlsPath),
      JsonOptions.CaseInsensitive)
      ?? new List<ControlRecord>();

    var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var control in controls)
    {
      LocalSetting? localSetting;
      if (!TryExtractLocalSetting(control, out localSetting) || localSetting == null)
        continue;

      settings[localSetting.Path] = localSetting.Value;
    }

    return settings;
  }

  private async Task<Dictionary<string, AppliedGpoSetting>> LoadAppliedGpoSettingsAsync(IEnumerable<string> targetPaths, CancellationToken ct)
  {
    if (_processRunner == null)
      return new Dictionary<string, AppliedGpoSetting>(StringComparer.OrdinalIgnoreCase);

    var rsopPath = Path.Combine(Path.GetTempPath(), "stigforge-rsop-" + Guid.NewGuid().ToString("N") + ".xml");
    try
    {
      var rsopResult = await _processRunner.RunAsync(new ProcessStartInfo
      {
        FileName = "gpresult.exe",
        Arguments = "/x \"" + rsopPath + "\" /f",
        CreateNoWindow = true,
        UseShellExecute = false
      }, ct).ConfigureAwait(false);

      if (rsopResult.ExitCode == 0 && File.Exists(rsopPath))
      {
        var xml = await File.ReadAllTextAsync(rsopPath, ct).ConfigureAwait(false);
        var parsed = ParseRsopXml(xml, targetPaths);
        if (parsed.Count > 0)
          return parsed;
      }

      await _processRunner.RunAsync(new ProcessStartInfo
      {
        FileName = "gpresult.exe",
        Arguments = "/r /scope computer",
        CreateNoWindow = true,
        UseShellExecute = false
      }, ct).ConfigureAwait(false);

      return new Dictionary<string, AppliedGpoSetting>(StringComparer.OrdinalIgnoreCase);
    }
    finally
    {
      try
      {
        if (File.Exists(rsopPath))
          File.Delete(rsopPath);
      }
      catch (Exception)
      {
      }
    }
  }

  private static Dictionary<string, AppliedGpoSetting> ParseRsopXml(string xml, IEnumerable<string> targetPaths)
  {
    if (string.IsNullOrWhiteSpace(xml))
      return new Dictionary<string, AppliedGpoSetting>(StringComparer.OrdinalIgnoreCase);

    var targetPathList = targetPaths.ToList();
    var targetByNormalized = targetPathList.ToDictionary(NormalizePath, p => p, StringComparer.OrdinalIgnoreCase);
    var results = new Dictionary<string, AppliedGpoSetting>(StringComparer.OrdinalIgnoreCase);

    XDocument doc;
    try
    {
      doc = XDocument.Parse(xml);
    }
    catch (Exception)
    {
      // RSoP XML may be malformed or empty — treat as no results.
      return results;
    }

    foreach (var element in doc.Descendants())
    {
      var path = FirstNonEmpty(
        Attr(element, "settingPath"),
        Attr(element, "path"),
        Attr(element, "name"),
        Attr(element, "key"),
        ChildValue(element, "settingPath"),
        ChildValue(element, "path"),
        ChildValue(element, "name"),
        ChildValue(element, "key"));

      var value = FirstNonEmpty(
        Attr(element, "value"),
        Attr(element, "data"),
        Attr(element, "state"),
        ChildValue(element, "value"),
        ChildValue(element, "data"),
        ChildValue(element, "state"));

      if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(value))
        continue;

      string? targetPath;
      if (!TryMatchTargetPath(path, targetByNormalized, out targetPath) || string.IsNullOrWhiteSpace(targetPath))
        continue;

      var gpoName = FirstNonEmpty(
        Attr(element, "gpo"),
        Attr(element, "gpoName"),
        Attr(element, "source"),
        ChildValue(element, "gpo"),
        ChildValue(element, "gpoName"),
        ChildValue(element, "source"),
        "Unknown GPO");

      results[targetPath] = new AppliedGpoSetting
      {
        Path = targetPath,
        Value = value,
        GpoName = gpoName
      };
    }

    return results;
  }

  private static bool TryExtractLocalSetting(ControlRecord control, out LocalSetting? localSetting)
  {
    localSetting = null;

    if (!string.IsNullOrWhiteSpace(control.Title))
    {
      var titleMatch = RegistryPolicyTitleRegex.Match(control.Title);
      if (titleMatch.Success)
      {
        localSetting = new LocalSetting
        {
          Path = titleMatch.Groups["path"].Value.Trim(),
          Value = titleMatch.Groups["value"].Value.Trim()
        };
        return true;
      }
    }

    if (!string.IsNullOrWhiteSpace(control.CheckText))
    {
      var settingMatch = SecuritySettingRegex.Match(control.CheckText);
      if (settingMatch.Success)
      {
        localSetting = new LocalSetting
        {
          Path = settingMatch.Groups["path"].Value.Trim(),
          Value = settingMatch.Groups["value"].Value.Trim()
        };
        return true;
      }

      var registryCheckMatch = RegistryCheckRegex.Match(control.CheckText);
      if (registryCheckMatch.Success)
      {
        var keyPath = registryCheckMatch.Groups["path"].Value.Trim();
        var valueName = registryCheckMatch.Groups["name"].Value.Trim();
        var value = TryExtractFixValue(control.FixText);
        localSetting = new LocalSetting
        {
          Path = keyPath + "\\" + valueName,
          Value = string.IsNullOrWhiteSpace(value) ? "Enabled" : value
        };
        return true;
      }
    }

    return false;
  }

  private static string? TryExtractFixValue(string? fixText)
  {
    if (string.IsNullOrWhiteSpace(fixText))
      return null;

    var match = FixValueRegex.Match(fixText);
    if (!match.Success)
      return null;

    return match.Groups["value"].Value.Trim();
  }

  private static bool TryMatchTargetPath(
    string candidatePath,
    IReadOnlyDictionary<string, string> targetByNormalized,
    out string? targetPath)
  {
    targetPath = null;
    var normalizedCandidate = NormalizePath(candidatePath);
    if (targetByNormalized.TryGetValue(normalizedCandidate, out targetPath))
      return true;

    var candidateNoHive = RemoveRegistryHivePrefix(normalizedCandidate);
    foreach (var kvp in targetByNormalized)
    {
      if (string.Equals(candidateNoHive, RemoveRegistryHivePrefix(kvp.Key), StringComparison.OrdinalIgnoreCase)
          || candidateNoHive.EndsWith(RemoveRegistryHivePrefix(kvp.Key), StringComparison.OrdinalIgnoreCase)
          || RemoveRegistryHivePrefix(kvp.Key).EndsWith(candidateNoHive, StringComparison.OrdinalIgnoreCase))
      {
        targetPath = kvp.Value;
        return true;
      }
    }

    var compactCandidate = Regex.Replace(candidateNoHive, "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase);
    if (!string.IsNullOrWhiteSpace(compactCandidate))
    {
      foreach (var kvp in targetByNormalized)
      {
        var compactTarget = Regex.Replace(RemoveRegistryHivePrefix(kvp.Key), "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase);
        if (string.Equals(compactCandidate, compactTarget, StringComparison.OrdinalIgnoreCase))
        {
          targetPath = kvp.Value;
          return true;
        }
      }
    }

    return false;
  }

  private static string Attr(XElement element, string name)
  {
    var attr = element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
    return attr?.Value?.Trim() ?? string.Empty;
  }

  private static string ChildValue(XElement element, string name)
  {
    var child = element.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
    return child?.Value?.Trim() ?? string.Empty;
  }

  private static string FirstNonEmpty(params string[] values)
  {
    foreach (var value in values)
    {
      if (!string.IsNullOrWhiteSpace(value))
        return value;
    }

    return string.Empty;
  }

  private static string NormalizePath(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
      return string.Empty;

    var normalized = path.Trim()
      .Replace("/", "\\")
      .Replace(" ", string.Empty);

    while (normalized.Contains("\\\\", StringComparison.Ordinal))
      normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);

    return normalized.ToLowerInvariant();
  }

  private static string RemoveRegistryHivePrefix(string normalizedPath)
  {
    if (normalizedPath.StartsWith("hklm:\\", StringComparison.OrdinalIgnoreCase))
      return normalizedPath.Substring("hklm:\\".Length);

    if (normalizedPath.StartsWith("hklm\\", StringComparison.OrdinalIgnoreCase))
      return normalizedPath.Substring("hklm\\".Length);

    if (normalizedPath.StartsWith("hkcu:\\", StringComparison.OrdinalIgnoreCase))
      return normalizedPath.Substring("hkcu:\\".Length);

    if (normalizedPath.StartsWith("hkcu\\", StringComparison.OrdinalIgnoreCase))
      return normalizedPath.Substring("hkcu\\".Length);

    return normalizedPath;
  }

  private static string NormalizeValue(string value)
  {
    return (value ?? string.Empty).Trim().Trim('"', '\'').ToLowerInvariant();
  }

  private sealed class LocalSetting
  {
    public string Path { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
  }

  private sealed class AppliedGpoSetting
  {
    public string Path { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string GpoName { get; set; } = string.Empty;
  }
}
