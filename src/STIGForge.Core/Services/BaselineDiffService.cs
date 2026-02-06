using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Compares two STIG packs (baseline vs new release) and identifies changes.
/// Supports quarterly STIG update workflows.
/// </summary>
public sealed class BaselineDiffService
{
  private readonly IControlRepository _controls;

  public BaselineDiffService(IControlRepository controls)
  {
    _controls = controls;
  }

  /// <summary>
  /// Compare two packs and generate a diff report.
  /// </summary>
  public async Task<BaselineDiff> ComparePacksAsync(
    string baselinePackId,
    string newPackId,
    CancellationToken cancellationToken = default)
  {
    // Load controls from both packs
    var baselineControls = await _controls.ListControlsAsync(baselinePackId, cancellationToken);
    var newControls = await _controls.ListControlsAsync(newPackId, cancellationToken);

    var diff = new BaselineDiff
    {
      BaselinePackId = baselinePackId,
      NewPackId = newPackId,
      ComparedAt = DateTimeOffset.Now
    };

    // Build lookup maps
    var baselineMap = BuildControlMap(baselineControls);
    var newMap = BuildControlMap(newControls);

    // Identify added controls (in new, not in baseline)
    foreach (var kvp in newMap)
    {
      if (!baselineMap.ContainsKey(kvp.Key))
      {
        diff.AddedControls.Add(new ControlDiff
        {
          ControlKey = kvp.Key,
          NewControl = kvp.Value,
          ChangeType = ControlChangeType.Added
        });
      }
    }

    // Identify removed controls (in baseline, not in new)
    foreach (var kvp in baselineMap)
    {
      if (!newMap.ContainsKey(kvp.Key))
      {
        diff.RemovedControls.Add(new ControlDiff
        {
          ControlKey = kvp.Key,
          BaselineControl = kvp.Value,
          ChangeType = ControlChangeType.Removed
        });
      }
    }

    // Identify modified controls (in both, but different)
    foreach (var kvp in baselineMap)
    {
      if (newMap.TryGetValue(kvp.Key, out var newControl))
      {
        var changes = CompareControls(kvp.Value, newControl);
        if (changes.Count > 0)
        {
          diff.ModifiedControls.Add(new ControlDiff
          {
            ControlKey = kvp.Key,
            BaselineControl = kvp.Value,
            NewControl = newControl,
            ChangeType = ControlChangeType.Modified,
            Changes = changes
          });
        }
      }
    }

    // Calculate summary statistics
    diff.TotalAdded = diff.AddedControls.Count;
    diff.TotalRemoved = diff.RemovedControls.Count;
    diff.TotalModified = diff.ModifiedControls.Count;
    diff.TotalUnchanged = baselineControls.Count() - diff.TotalRemoved - diff.TotalModified;

    return diff;
  }

  /// <summary>
  /// Compare two individual controls and identify what changed.
  /// </summary>
  private static List<ControlFieldChange> CompareControls(ControlRecord baseline, ControlRecord newControl)
  {
    var changes = new List<ControlFieldChange>();

    // Compare title
    if (baseline.Title != newControl.Title)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "Title",
        OldValue = baseline.Title,
        NewValue = newControl.Title,
        Impact = FieldChangeImpact.Low
      });
    }

    // Compare severity
    if (baseline.Severity != newControl.Severity)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "Severity",
        OldValue = baseline.Severity,
        NewValue = newControl.Severity,
        Impact = FieldChangeImpact.High // Severity change is significant
      });
    }

    // Compare check text
    if (baseline.CheckText != newControl.CheckText)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "CheckText",
        OldValue = baseline.CheckText,
        NewValue = newControl.CheckText,
        Impact = FieldChangeImpact.High // Check logic changed
      });
    }

    // Compare fix text
    if (baseline.FixText != newControl.FixText)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "FixText",
        OldValue = baseline.FixText,
        NewValue = newControl.FixText,
        Impact = FieldChangeImpact.Medium // Fix approach changed
      });
    }

    // Compare discussion
    if (baseline.Discussion != newControl.Discussion)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "Discussion",
        OldValue = baseline.Discussion,
        NewValue = newControl.Discussion,
        Impact = FieldChangeImpact.Low // Context change
      });
    }

    // Compare IsManual flag
    if (baseline.IsManual != newControl.IsManual)
    {
      changes.Add(new ControlFieldChange
      {
        FieldName = "IsManual",
        OldValue = baseline.IsManual.ToString(),
        NewValue = newControl.IsManual.ToString(),
        Impact = FieldChangeImpact.High // Automation status changed
      });
    }

    return changes;
  }

  /// <summary>
  /// Build a map of controls by their unique identifier.
  /// Priority: RuleId > VulnId > ControlId
  /// </summary>
  private static Dictionary<string, ControlRecord> BuildControlMap(IReadOnlyList<ControlRecord> controls)
  {
    var map = new Dictionary<string, ControlRecord>(StringComparer.OrdinalIgnoreCase);

    foreach (var control in controls)
    {
      var key = GetControlKey(control);
      if (!map.ContainsKey(key))
        map[key] = control;
    }

    return map;
  }

  private static string GetControlKey(ControlRecord control)
  {
    // Prefer RuleId (stable across versions), fall back to VulnId, then ControlId
    if (!string.IsNullOrWhiteSpace(control.ExternalIds.RuleId))
      return "RULE:" + control.ExternalIds.RuleId;
    if (!string.IsNullOrWhiteSpace(control.ExternalIds.VulnId))
      return "VULN:" + control.ExternalIds.VulnId;
    return "ID:" + control.ControlId;
  }
}

/// <summary>
/// Result of comparing two STIG packs.
/// </summary>
public sealed class BaselineDiff
{
  public string BaselinePackId { get; set; } = string.Empty;
  public string NewPackId { get; set; } = string.Empty;
  public DateTimeOffset ComparedAt { get; set; }

  public List<ControlDiff> AddedControls { get; set; } = new();
  public List<ControlDiff> RemovedControls { get; set; } = new();
  public List<ControlDiff> ModifiedControls { get; set; } = new();

  public int TotalAdded { get; set; }
  public int TotalRemoved { get; set; }
  public int TotalModified { get; set; }
  public int TotalUnchanged { get; set; }

  public int TotalControls => TotalAdded + TotalRemoved + TotalModified + TotalUnchanged;
}

/// <summary>
/// Diff for a single control.
/// </summary>
public sealed class ControlDiff
{
  public string ControlKey { get; set; } = string.Empty;
  public ControlRecord? BaselineControl { get; set; }
  public ControlRecord? NewControl { get; set; }
  public ControlChangeType ChangeType { get; set; }
  public List<ControlFieldChange> Changes { get; set; } = new();
}

/// <summary>
/// Change to a specific field within a control.
/// </summary>
public sealed class ControlFieldChange
{
  public string FieldName { get; set; } = string.Empty;
  public string? OldValue { get; set; }
  public string? NewValue { get; set; }
  public FieldChangeImpact Impact { get; set; }
}

/// <summary>
/// Type of control change.
/// </summary>
public enum ControlChangeType
{
  Added,
  Removed,
  Modified
}

/// <summary>
/// Impact level of a field change.
/// </summary>
public enum FieldChangeImpact
{
  Low,      // Cosmetic changes (title, discussion)
  Medium,   // Significant but not breaking (fix text)
  High      // Breaking changes (check text, severity, manual status)
}
