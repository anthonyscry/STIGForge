using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class OverlayEditorViewModel : ObservableObject
{
  private readonly IOverlayRepository _overlays;

  public OverlayEditorViewModel(IOverlayRepository overlays)
  {
    _overlays = overlays;
  }

  [ObservableProperty] private string overlayId = "";
  [ObservableProperty] private string overlayName = "";
  [ObservableProperty] private string powerStigRuleId = "";
  [ObservableProperty] private string powerStigSettingName = "";
  [ObservableProperty] private string powerStigValue = "";
  [ObservableProperty] private string overlayStatus = "";

  public IList<PowerStigOverride> PowerStigOverrides { get; } = new List<PowerStigOverride>();

  [RelayCommand]
  private void AddPowerStigOverride()
  {
    if (string.IsNullOrWhiteSpace(PowerStigRuleId)) return;

    PowerStigOverrides.Add(new PowerStigOverride
    {
      RuleId = PowerStigRuleId.Trim(),
      SettingName = string.IsNullOrWhiteSpace(PowerStigSettingName) ? null : PowerStigSettingName.Trim(),
      Value = string.IsNullOrWhiteSpace(PowerStigValue) ? null : PowerStigValue.Trim()
    });

    PowerStigRuleId = string.Empty;
    PowerStigSettingName = string.Empty;
    PowerStigValue = string.Empty;
    OnPropertyChanged(nameof(PowerStigOverrides));
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
    foreach (var r in rows)
      PowerStigOverrides.Add(r);

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
        PowerStigOverrides = PowerStigOverrides.ToList()
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
}
