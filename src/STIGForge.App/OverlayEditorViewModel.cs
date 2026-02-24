using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class OverlayEditorViewModel : ObservableObject
{
  private readonly IOverlayRepository _overlays;
  private readonly IControlRepository? _controls;

  public OverlayEditorViewModel(IOverlayRepository overlays, IControlRepository? controls = null)
  {
    _overlays = overlays;
    _controls = controls;
  }

  [ObservableProperty] private string overlayId = "";
  [ObservableProperty] private string overlayName = "";
  [ObservableProperty] private string powerStigRuleId = "";
  [ObservableProperty] private string powerStigSettingName = "";
  [ObservableProperty] private string powerStigValue = "";
  [ObservableProperty] private string overlayStatus = "";

  public IList<PowerStigOverride> PowerStigOverrides { get; } = new List<PowerStigOverride>();
  public IList<ControlOverride> ControlOverrides { get; } = new List<ControlOverride>();

  /// <summary>
  /// Available rules sourced from controls in selected content packs.
  /// Populated when pack IDs are provided via LoadAvailableRulesAsync.
  /// </summary>
  public ObservableCollection<SelectableRuleItem> AvailableRules { get; } = new();

  [ObservableProperty] private SelectableRuleItem? selectedRule;
  [ObservableProperty] private ControlStatus selectedRuleStatus = ControlStatus.NotApplicable;
  [ObservableProperty] private string selectedRuleReason = "";
  [ObservableProperty] private string selectedRuleNotes = "";

  /// <summary>
  /// Loads available rules from controls in the specified content packs.
  /// </summary>
  public async Task LoadAvailableRulesAsync(IReadOnlyList<string> packIds, CancellationToken ct)
  {
    if (_controls == null)
    {
      AvailableRules.Clear();
      return;
    }

    var allControls = new List<ControlRecord>();
    foreach (var packId in packIds)
    {
      var packControls = await _controls.ListControlsAsync(packId, ct);
      allControls.AddRange(packControls);
    }

    // Create selectable rule items with deterministic sorting
    var rules = allControls
      .Where(c => c.ExternalIds != null && (!string.IsNullOrWhiteSpace(c.ExternalIds.RuleId) || !string.IsNullOrWhiteSpace(c.ExternalIds.VulnId)))
      .Select(c => new SelectableRuleItem
      {
        RuleId = c.ExternalIds?.RuleId ?? "",
        VulnId = c.ExternalIds?.VulnId ?? "",
        Title = c.Title,
        Severity = c.Severity,
        PackId = c.SourcePackId
      })
      .OrderBy(r => string.IsNullOrWhiteSpace(r.RuleId) ? 1 : 0)
      .ThenBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.VulnId, StringComparer.OrdinalIgnoreCase)
      .ToList();

    AvailableRules.Clear();
    foreach (var rule in rules)
      AvailableRules.Add(rule);
  }

  [RelayCommand]
  private void AddPowerStigOverride()
  {
    if (string.IsNullOrWhiteSpace(PowerStigRuleId)) return;

    InvokeOnUiThread(() =>
    {
      PowerStigOverrides.Add(new PowerStigOverride
      {
        RuleId = PowerStigRuleId.Trim(),
        SettingName = string.IsNullOrWhiteSpace(PowerStigSettingName) ? null : PowerStigSettingName.Trim(),
        Value = string.IsNullOrWhiteSpace(PowerStigValue) ? null : PowerStigValue.Trim()
      });
    });

    PowerStigRuleId = string.Empty;
    PowerStigSettingName = string.Empty;
    PowerStigValue = string.Empty;
    OnPropertyChanged(nameof(PowerStigOverrides));
  }

  [RelayCommand]
  private void AddControlOverride()
  {
    if (SelectedRule == null) return;

    // Check for duplicate
    var existing = ControlOverrides.FirstOrDefault(o =>
      (!string.IsNullOrWhiteSpace(SelectedRule.RuleId) && string.Equals(o.RuleId, SelectedRule.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(SelectedRule.VulnId) && string.Equals(o.VulnId, SelectedRule.VulnId, StringComparison.OrdinalIgnoreCase)));

    if (existing != null)
    {
      OverlayStatus = "Rule already in override list. Remove the existing entry first.";
      return;
    }

    InvokeOnUiThread(() =>
    {
      ControlOverrides.Add(new ControlOverride
      {
        RuleId = string.IsNullOrWhiteSpace(SelectedRule.RuleId) ? null : SelectedRule.RuleId.Trim(),
        VulnId = string.IsNullOrWhiteSpace(SelectedRule.VulnId) ? null : SelectedRule.VulnId.Trim(),
        StatusOverride = SelectedRuleStatus,
        NaReason = string.IsNullOrWhiteSpace(SelectedRuleReason) ? null : SelectedRuleReason.Trim(),
        Notes = string.IsNullOrWhiteSpace(SelectedRuleNotes) ? null : SelectedRuleNotes.Trim()
      });
    });

    OverlayStatus = "Added control override for " + (SelectedRule.RuleId ?? SelectedRule.VulnId ?? "unknown");
    SelectedRule = null;
    SelectedRuleStatus = ControlStatus.NotApplicable;
    SelectedRuleReason = string.Empty;
    SelectedRuleNotes = string.Empty;
    OnPropertyChanged(nameof(ControlOverrides));
  }

  [RelayCommand]
  private void RemoveControlOverride(ControlOverride? item)
  {
    if (item == null) return;

    InvokeOnUiThread(() =>
    {
      ControlOverrides.Remove(item);
    });

    OnPropertyChanged(nameof(ControlOverrides));
    OverlayStatus = "Removed control override for " + (item.RuleId ?? item.VulnId ?? "unknown");
  }

  [RelayCommand]
  private void ImportPowerStigCsv()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
      Title = "Select PowerSTIG mapping CSV"
    };

    if (ofd.ShowDialog() != true) return;

    var rows = ReadPowerStigCsv(ofd.FileName);
    InvokeOnUiThread(() =>
    {
      foreach (var r in rows)
        PowerStigOverrides.Add(r);
    });

    OnPropertyChanged(nameof(PowerStigOverrides));
  }

  [RelayCommand]
  private async Task SaveOverlayAsync()
  {
    try
    {
      var id = string.IsNullOrWhiteSpace(OverlayId) ? Guid.NewGuid().ToString("n") : OverlayId.Trim();
      var overlay = new Overlay
      {
        OverlayId = id,
        Name = string.IsNullOrWhiteSpace(OverlayName) ? "Overlay" : OverlayName.Trim(),
        UpdatedAt = DateTimeOffset.Now,
        PowerStigOverrides = PowerStigOverrides.ToList(),
        Overrides = ControlOverrides.ToList()
      };

      await _overlays.SaveAsync(overlay, CancellationToken.None);
      OverlayId = overlay.OverlayId;
      OverlayStatus = "Overlay saved: " + overlay.OverlayId;
    }
    catch (Exception ex)
    {
      OverlayStatus = "Save failed: " + ex.Message;
    }
  }

  private static IReadOnlyList<PowerStigOverride> ReadPowerStigCsv(string csvPath)
  {
    var list = new List<PowerStigOverride>();
    var lines = File.ReadAllLines(csvPath);

    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line)) continue;
      if (line.StartsWith("RuleId", StringComparison.OrdinalIgnoreCase)) continue;

      var parts = ParseCsvLine(line);
      if (parts.Length < 3) continue;

      var ruleId = parts[0].Trim();
      if (string.IsNullOrWhiteSpace(ruleId)) continue;

      list.Add(new PowerStigOverride
      {
        RuleId = ruleId,
        SettingName = parts[1].Trim(),
        Value = parts[2].Trim()
      });
    }

    return list;
  }

  private static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;
    for (int i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          sb.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        list.Add(sb.ToString());
        sb.Clear();
      }
      else
      {
        sb.Append(ch);
      }
    }
    list.Add(sb.ToString());
    return list.ToArray();
  }

  private static void InvokeOnUiThread(Action action)
  {
    var dispatcher = System.Windows.Application.Current?.Dispatcher;
    if (dispatcher == null)
    {
      action();
      return;
    }

    if (dispatcher.CheckAccess())
    {
      action();
      return;
    }

    dispatcher.Invoke(action);
  }
}

/// <summary>
/// Represents a selectable rule from content pack controls for overlay editing.
/// </summary>
public sealed class SelectableRuleItem
{
  public string RuleId { get; set; } = string.Empty;
  public string VulnId { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Severity { get; set; } = string.Empty;
  public string PackId { get; set; } = string.Empty;

  /// <summary>
  /// Display text showing RuleId (or VulnId if RuleId is empty) with severity.
  /// </summary>
  public string DisplayText => !string.IsNullOrWhiteSpace(RuleId)
    ? $"[{Severity}] {RuleId} - {Title}"
    : !string.IsNullOrWhiteSpace(VulnId)
      ? $"[{Severity}] {VulnId} - {Title}"
      : Title;
}
