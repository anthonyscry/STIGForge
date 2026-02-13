using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class BundleMissionSummaryService : IBundleMissionSummaryService
{
  private readonly ManualAnswerService _manualAnswers;

  public BundleMissionSummaryService(ManualAnswerService? manualAnswers = null)
  {
    _manualAnswers = manualAnswers ?? new ManualAnswerService();
  }

  public BundleMissionSummary LoadSummary(string bundleRoot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot))
      throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));

    if (!Directory.Exists(bundleRoot))
      throw new DirectoryNotFoundException("Bundle not found: " + bundleRoot);

    var summary = new BundleMissionSummary
    {
      BundleRoot = bundleRoot
    };

    var diagnostics = new List<string>();

    LoadManifestMetadata(bundleRoot, summary, diagnostics);

    var controls = LoadControls(bundleRoot, diagnostics);
    summary.TotalControls = controls.Count;
    summary.ManualControls = controls.Count(c => c.IsManual);
    summary.AutoControls = summary.TotalControls - summary.ManualControls;

    var manualControls = controls.Where(c => c.IsManual).ToList();
    var manualStats = _manualAnswers.GetProgressStats(bundleRoot, manualControls);
    summary.Manual = new BundleManualSummary
    {
      PassCount = manualStats.PassCount,
      FailCount = manualStats.FailCount,
      NotApplicableCount = manualStats.NotApplicableCount,
      OpenCount = manualStats.UnansweredControls,
      AnsweredCount = manualStats.AnsweredControls,
      TotalCount = manualStats.TotalControls,
      PercentComplete = manualStats.PercentComplete
    };

    var verify = LoadVerifySummary(bundleRoot, diagnostics);
    summary.Verify = verify;

    summary.Diagnostics = diagnostics;
    return summary;
  }

  public string NormalizeStatus(string? status)
  {
    var normalized = NormalizeToken(status);
    if (normalized.Length == 0)
      return "Open";

    if (normalized == "pass" || normalized == "notafinding" || normalized == "compliant" || normalized == "closed")
      return "Pass";

    if (normalized == "notapplicable" || normalized == "na")
      return "NotApplicable";

    if (normalized == "fail" || normalized == "noncompliant")
      return "Fail";

    if (normalized == "open" || normalized == "notreviewed" || normalized == "notchecked" || normalized == "unknown" || normalized == "error" || normalized == "informational")
      return "Open";

    return "Open";
  }

  private BundleVerifySummary LoadVerifySummary(string bundleRoot, List<string> diagnostics)
  {
    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot))
      return new BundleVerifySummary();

    var reportPaths = Directory.GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    int closed = 0;
    int total = 0;
    int blockingFailures = 0;
    int warnings = 0;
    int optionalSkips = 0;

    foreach (var reportPath in reportPaths)
    {
      try
      {
        using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
        if (!TryGetPropertyCaseInsensitive(doc.RootElement, "results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
          diagnostics.Add("Verify report missing results array: " + reportPath);
          continue;
        }

        foreach (var item in results.EnumerateArray())
        {
          if (item.ValueKind != JsonValueKind.Object)
            continue;

          total++;

          var status = ReadStringProperty(item, "status");
          var normalized = NormalizeStatus(status);
          if (string.Equals(normalized, "Pass", StringComparison.Ordinal))
          {
            closed++;
          }
          else if (string.Equals(normalized, "NotApplicable", StringComparison.Ordinal))
          {
            closed++;
            optionalSkips++;
          }
          else
          {
            blockingFailures++;
          }

          if (IsWarningStatus(status))
            warnings++;
        }
      }
      catch (Exception ex)
      {
        diagnostics.Add("Failed to parse verify report " + reportPath + ": " + ex.Message);
        warnings++;
      }
    }

    return new BundleVerifySummary
    {
      ClosedCount = closed,
      OpenCount = total - closed,
      TotalCount = total,
      ReportCount = reportPaths.Count,
      BlockingFailureCount = blockingFailures,
      RecoverableWarningCount = warnings,
      OptionalSkipCount = optionalSkips
    };
  }

  private static IReadOnlyList<ControlRecord> LoadControls(string bundleRoot, List<string> diagnostics)
  {
    var controlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath))
      return Array.Empty<ControlRecord>();

    try
    {
      var controls = JsonSerializer.Deserialize<List<ControlRecord>>(File.ReadAllText(controlsPath), new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      return controls ?? new List<ControlRecord>();
    }
    catch (Exception ex)
    {
      diagnostics.Add("Failed to parse pack_controls.json: " + ex.Message);
      return Array.Empty<ControlRecord>();
    }
  }

  private static void LoadManifestMetadata(string bundleRoot, BundleMissionSummary summary, List<string> diagnostics)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return;

    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (TryGetPropertyCaseInsensitive(doc.RootElement, "run", out var run) && run.ValueKind == JsonValueKind.Object)
      {
        summary.PackName = ReadStringProperty(run, "packName") ?? summary.PackName;
        summary.ProfileName = ReadStringProperty(run, "profileName") ?? summary.ProfileName;
      }
      else
      {
        summary.PackName = ReadStringProperty(doc.RootElement, "packName") ?? summary.PackName;
        summary.ProfileName = ReadStringProperty(doc.RootElement, "profileName") ?? summary.ProfileName;
      }
    }
    catch (Exception ex)
    {
      diagnostics.Add("Failed to parse manifest.json: " + ex.Message);
    }
  }

  private bool IsClosedStatus(string? status)
  {
    var normalized = NormalizeStatus(status);
    return string.Equals(normalized, "Pass", StringComparison.Ordinal)
      || string.Equals(normalized, "NotApplicable", StringComparison.Ordinal);
  }

  private static bool IsWarningStatus(string? status)
  {
    var normalized = NormalizeToken(status);
    return normalized == "informational" || normalized == "info" || normalized == "warning";
  }

  private static string? ReadStringProperty(JsonElement element, string propertyName)
  {
    if (!TryGetPropertyCaseInsensitive(element, propertyName, out var value))
      return null;

    return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
  }

  private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      value = default;
      return false;
    }

    if (element.TryGetProperty(propertyName, out value))
      return true;

    foreach (var property in element.EnumerateObject())
    {
      if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
      {
        value = property.Value;
        return true;
      }
    }

    value = default;
    return false;
  }

  private static string NormalizeToken(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return string.Empty;

    var source = (status ?? string.Empty).Trim().ToLowerInvariant();
    var sb = new StringBuilder(source.Length);

    foreach (var ch in source)
    {
      if (char.IsLetterOrDigit(ch))
        sb.Append(ch);
    }

    return sb.ToString();
  }
}
