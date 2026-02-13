using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Utilities;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using ControlStatusStrings = STIGForge.Core.Constants.ControlStatus;
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

    var manualScope = BuildManualScopeControls(bundleRoot, controls, diagnostics);
    summary.ManualControls = manualScope.Count;
    summary.AutoControls = summary.TotalControls - summary.ManualControls;

    var manualStats = _manualAnswers.GetProgressStats(bundleRoot, manualScope);
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

    var verify = LoadVerifySummary(bundleRoot, controls, diagnostics);
    summary.Verify = verify;

    summary.Diagnostics = diagnostics;
    return summary;
  }

  public string NormalizeStatus(string? status)
  {
    var normalized = NormalizeToken(status);
    if (normalized.Length == 0)
      return ControlStatusStrings.Open;

    if (normalized == "pass" || normalized == "notafinding" || normalized == "compliant" || normalized == "closed")
      return ControlStatusStrings.Pass;

    if (normalized == "notapplicable" || normalized == "na")
      return ControlStatusStrings.NotApplicable;

    if (normalized == "fail" || normalized == "noncompliant")
      return ControlStatusStrings.Fail;

    if (normalized == "open" || normalized == "notreviewed" || normalized == "notchecked" || normalized == "unknown" || normalized == "error" || normalized == "informational")
      return ControlStatusStrings.Open;

    return ControlStatusStrings.Open;
  }

  private BundleVerifySummary LoadVerifySummary(string bundleRoot, IReadOnlyList<ControlRecord> controls, List<string> diagnostics)
  {
    var verifyRoot = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
    if (!Directory.Exists(verifyRoot))
      return new BundleVerifySummary();

    var reportPaths = Directory.GetFiles(verifyRoot, BundlePaths.ConsolidatedResultsFileName, SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var controlIndex = BuildControlIndex(controls);
    var states = new Dictionary<string, (bool anyClosed, bool anyOpen, bool anyWarning)>(StringComparer.OrdinalIgnoreCase);

    int blockingFailures = 0;
    int parseWarnings = 0;
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

          var status = ReadStringProperty(item, "status");
          var key = ResolveControlKey(item, status, diagnostics);
          if (string.IsNullOrWhiteSpace(key))
            continue;

          if (controlIndex.Count > 0 && !controlIndex.Contains(key))
            continue;

          var normalized = NormalizeStatus(status);
          var isClosed = string.Equals(normalized, ControlStatusStrings.Pass, StringComparison.Ordinal)
            || string.Equals(normalized, ControlStatusStrings.NotApplicable, StringComparison.Ordinal);
          var isOpen = !isClosed;
          var isWarning = IsWarningStatus(status);

          if (!states.TryGetValue(key, out var state))
            state = (false, false, false);

          state.anyClosed = state.anyClosed || isClosed;
          state.anyOpen = state.anyOpen || isOpen;
          state.anyWarning = state.anyWarning || isWarning;
          states[key] = state;

          if (string.Equals(normalized, ControlStatusStrings.NotApplicable, StringComparison.Ordinal))
            optionalSkips++;
        }
      }
      catch (Exception ex)
      {
        diagnostics.Add("Failed to parse verify report " + reportPath + ": " + ex.Message);
        parseWarnings++;
      }
    }

    var total = states.Count;
    var closed = states.Count(kvp => kvp.Value.anyClosed && !kvp.Value.anyOpen);
    blockingFailures = total - closed;
    var statusWarnings = states.Count(kvp => kvp.Value.anyWarning);

    return new BundleVerifySummary
    {
      ClosedCount = closed,
      OpenCount = total - closed,
      TotalCount = total,
      ReportCount = reportPaths.Count,
      BlockingFailureCount = blockingFailures,
      RecoverableWarningCount = statusWarnings + parseWarnings,
      OptionalSkipCount = optionalSkips
    };
  }

  private static IReadOnlyList<ControlRecord> BuildManualScopeControls(string bundleRoot, IReadOnlyList<ControlRecord> controls, List<string> diagnostics)
  {
    return controls.ToList();
  }

  private static HashSet<string> BuildControlIndex(IReadOnlyList<ControlRecord> controls)
  {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var control in controls)
    {
      var key = BuildControlKey(control.ExternalIds.RuleId, control.ExternalIds.VulnId, control.ControlId);
      if (!string.IsNullOrWhiteSpace(key))
        set.Add(key);
    }
    return set;
  }

  private static string ResolveControlKey(JsonElement item, string? status, List<string> diagnostics)
  {
    var ruleId = ReadStringProperty(item, "ruleId");
    var vulnId = ReadStringProperty(item, "vulnId");
    var title = ReadStringProperty(item, "title");

    var key = BuildControlKey(ruleId, vulnId, title);
    if (string.IsNullOrWhiteSpace(key))
      diagnostics.Add("Verify result skipped due to missing RuleId/VulnId/Title. Status=" + (status ?? "(null)"));

    return key;
  }

  private static string BuildControlKey(string? ruleId, string? vulnId, string? fallback)
  {
    if (!string.IsNullOrWhiteSpace(ruleId))
      return "RULE:" + (ruleId ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(vulnId))
      return "VULN:" + (vulnId ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(fallback))
      return "TITLE:" + (fallback ?? string.Empty).Trim();
    return string.Empty;
  }

  private static string[] ParseCsvLine(string line)
  {
    return CsvUtility.ParseLine(line);
  }

  private static IReadOnlyList<ControlRecord> LoadControls(string bundleRoot, List<string> diagnostics)
  {
    _ = diagnostics;
    return PackControlsReader.Load(bundleRoot);
  }

  private static void LoadManifestMetadata(string bundleRoot, BundleMissionSummary summary, List<string> diagnostics)
  {
    var manifestPath = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
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
    return string.Equals(normalized, ControlStatusStrings.Pass, StringComparison.Ordinal)
      || string.Equals(normalized, ControlStatusStrings.NotApplicable, StringComparison.Ordinal);
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
