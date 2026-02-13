using System.IO;
using System.Text.Json;
using STIGForge.Core.Constants;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Centralized reader for pack_controls.json files in bundle manifests.
/// </summary>
public static class PackControlsReader
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private static readonly object CacheLock = new();
  private static readonly Dictionary<string, (DateTime LastWrite, List<ControlRecord> Controls)> Cache = new();

  /// <summary>
  /// Loads control records from a bundle's pack_controls.json file.
  /// Returns empty list if file doesn't exist or parsing fails.
  /// </summary>
  public static List<ControlRecord> Load(string bundleRoot)
  {
    var path = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, BundlePaths.PackControlsFileName);
    return LoadFromPath(path);
  }

  /// <summary>
  /// Loads control records from an explicit file path.
  /// Returns empty list if file doesn't exist or parsing fails.
  /// </summary>
  public static List<ControlRecord> LoadFromPath(string path)
  {
    if (!File.Exists(path))
      return new List<ControlRecord>();

    try
    {
      var lastWrite = File.GetLastWriteTimeUtc(path);

      lock (CacheLock)
      {
        if (Cache.TryGetValue(path, out var entry) && entry.LastWrite == lastWrite)
          return new List<ControlRecord>(entry.Controls);
      }

      var json = File.ReadAllText(path);
      var controls = JsonSerializer.Deserialize<List<ControlRecord>>(json, Options)
        ?? new List<ControlRecord>();

      lock (CacheLock)
      {
        Cache[path] = (lastWrite, controls);
      }

      return new List<ControlRecord>(controls);
    }
    catch
    {
      return new List<ControlRecord>();
    }
  }

  /// <summary>
  /// Clears the cache. Call when a bundle is rebuilt or pack_controls.json is regenerated.
  /// </summary>
  public static void InvalidateCache()
  {
    lock (CacheLock)
    {
      Cache.Clear();
    }
  }
}
