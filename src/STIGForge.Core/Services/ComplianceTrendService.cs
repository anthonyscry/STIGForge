using System.Text;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class ComplianceTrendService
{
  private const int MaxSnapshots = 90;

  public void RecordSnapshot(string bundleRoot, TrendSnapshot snapshot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot))
      throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));

    if (snapshot == null)
      throw new ArgumentNullException(nameof(snapshot));

    var file = LoadTrend(bundleRoot);
    var snapshotHour = TruncateToHour(snapshot.Timestamp);

    file.Snapshots.RemoveAll(existing => TruncateToHour(existing.Timestamp) == snapshotHour);
    file.Snapshots.Add(snapshot);
    file.Snapshots = KeepLatestSnapshots(file.Snapshots);

    SaveTrend(bundleRoot, file);
  }

  public ComplianceTrendFile LoadTrend(string bundleRoot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot))
      return new ComplianceTrendFile();

    var path = GetTrendPath(bundleRoot);
    if (!File.Exists(path))
      return new ComplianceTrendFile();

    try
    {
      var json = File.ReadAllText(path);
      var file = JsonSerializer.Deserialize<ComplianceTrendFile>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (file?.Snapshots == null)
        return new ComplianceTrendFile();

      file.Snapshots = KeepLatestSnapshots(file.Snapshots);
      return file;
    }
    catch
    {
      return new ComplianceTrendFile();
    }
  }

  private static void SaveTrend(string bundleRoot, ComplianceTrendFile file)
  {
    var path = GetTrendPath(bundleRoot);
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
      Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    File.WriteAllText(path, json, Encoding.UTF8);
  }

  private static string GetTrendPath(string bundleRoot)
  {
    return Path.Combine(bundleRoot, ".stigforge", "compliance_trend.json");
  }

  private static DateTimeOffset TruncateToHour(DateTimeOffset timestamp)
  {
    var utc = timestamp.ToUniversalTime();
    return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
  }

  private static List<TrendSnapshot> KeepLatestSnapshots(IEnumerable<TrendSnapshot> snapshots)
  {
    var ordered = snapshots.OrderBy(s => s.Timestamp).ToList();
    if (ordered.Count <= MaxSnapshots)
      return ordered;

    return ordered.Skip(ordered.Count - MaxSnapshots).ToList();
  }
}
