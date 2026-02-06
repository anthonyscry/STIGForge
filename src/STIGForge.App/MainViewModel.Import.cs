using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using STIGForge.Build;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class MainViewModel
{
  [RelayCommand]
  private async Task ImportContentPackAsync()
  {
    if (IsBusy) return;
    var ofd = new OpenFileDialog
    {
      Filter = "Zip Files (*.zip)|*.zip|All Files (*.*)|*.*",
      Title = "Select DISA Content Pack ZIP"
    };

    if (ofd.ShowDialog() != true) return;

    try
    {
      IsBusy = true;
      StatusText = "Importing...";
      var packName = "Imported_" + DateTimeOffset.Now.ToString("yyyyMMdd_HHmm");
      var pack = await _importer.ImportZipAsync(ofd.FileName, packName, "manual_import", CancellationToken.None);
      ContentPacks.Insert(0, pack);
      OnPropertyChanged(nameof(ContentPacks));
      StatusText = "Imported: " + pack.Name;
    }
    catch (Exception ex)
    {
      StatusText = "Import failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task BuildBundleAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      StatusText = "Building bundle...";
      var pack = SelectedPack;
      if (pack == null)
      {
        StatusText = "No content pack loaded.";
        return;
      }

      var profile = SelectedProfile;
      if (profile == null)
      {
        StatusText = "No profile selected.";
        return;
      }

      var controlList = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
      var overlays = new List<Overlay>();
      if (profile.OverlayIds != null && profile.OverlayIds.Count > 0)
      {
        foreach (var oid in profile.OverlayIds)
        {
          if (string.IsNullOrWhiteSpace(oid)) continue;
          var ov = await _overlays.GetAsync(oid, CancellationToken.None);
          if (ov != null) overlays.Add(ov);
        }
      }

      var result = await _builder.BuildAsync(new BundleBuildRequest
      {
        Pack = pack,
        Profile = profile,
        Controls = controlList,
        Overlays = overlays,
        ToolVersion = "0.1.0-dev"
      }, CancellationToken.None);

      var gatePath = Path.Combine(result.BundleRoot, "Reports", "automation_gate.json");
      BuildGateStatus = File.Exists(gatePath) ? "Automation gate: " + gatePath : "Automation gate: (not found)";
      AutomationGatePath = File.Exists(gatePath) ? gatePath : string.Empty;
      BundleRoot = result.BundleRoot;
      AddRecentBundle(result.BundleRoot);
      StatusText = "Build complete.";
    }
    catch (Exception ex)
    {
      StatusText = "Build failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task SaveProfileAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      var profile = SelectedProfile ?? new Profile { ProfileId = Guid.NewGuid().ToString("n") };
      profile.Name = ProfileName;
      profile.OsTarget = OsTarget.Win11;
      profile.RoleTemplate = RoleTemplate.Workstation;
      profile.HardeningMode = ParseMode(ProfileMode);
      profile.ClassificationMode = ParseClassification(ProfileClassification);
      profile.NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = ProfileAutoNa,
        ConfidenceThreshold = Confidence.High,
        DefaultNaCommentTemplate = ProfileNaComment
      };
      profile.AutomationPolicy = new AutomationPolicy
      {
        Mode = AutomationMode.Standard,
        NewRuleGraceDays = ProfileGraceDays,
        AutoApplyRequiresMapping = true,
        ReleaseDateSource = ReleaseDateSource.ContentPack
      };
      profile.OverlayIds = GetSelectedOverlayIds();

      await _profiles.SaveAsync(profile, CancellationToken.None);
      if (!Profiles.Contains(profile)) Profiles.Add(profile);
      SelectedProfile = profile;
      OnPropertyChanged(nameof(Profiles));
      StatusText = "Profile saved.";
    }
    catch (Exception ex)
    {
      StatusText = "Save failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task RefreshOverlaysAsync()
  {
    try
    {
      IsBusy = true;
      var overlays = await _overlays.ListAsync(CancellationToken.None);
      OverlayItems.Clear();
      foreach (var o in overlays) OverlayItems.Add(new OverlayItem(o));
      OnPropertyChanged(nameof(OverlayItems));

      if (SelectedProfile != null)
        ApplyOverlaySelection(SelectedProfile);
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task DeleteSelectedPack()
  {
    if (SelectedPack == null)
    {
      StatusText = "No pack selected.";
      return;
    }

    var result = System.Windows.MessageBox.Show(
      $"Delete pack \"{SelectedPack.Name}\"?\n\nThis will remove the pack and all its controls from the database.",
      "Confirm Delete",
      System.Windows.MessageBoxButton.YesNo,
      System.Windows.MessageBoxImage.Warning);

    if (result != System.Windows.MessageBoxResult.Yes) return;

    try
    {
      IsBusy = true;
      await _packs.DeleteAsync(SelectedPack.PackId, CancellationToken.None);
      ContentPacks.Remove(SelectedPack);
      SelectedPack = ContentPacks.Count > 0 ? ContentPacks[0] : null;
      OnPropertyChanged(nameof(ContentPacks));
      StatusText = "Pack deleted.";
    }
    catch (Exception ex)
    {
      StatusText = "Delete failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task DeleteSelectedProfile()
  {
    if (SelectedProfile == null)
    {
      StatusText = "No profile selected.";
      return;
    }

    var result = System.Windows.MessageBox.Show(
      $"Delete profile \"{SelectedProfile.Name}\"?",
      "Confirm Delete",
      System.Windows.MessageBoxButton.YesNo,
      System.Windows.MessageBoxImage.Warning);

    if (result != System.Windows.MessageBoxResult.Yes) return;

    try
    {
      IsBusy = true;
      await _profiles.DeleteAsync(SelectedProfile.ProfileId, CancellationToken.None);
      Profiles.Remove(SelectedProfile);
      SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
      OnPropertyChanged(nameof(Profiles));
      StatusText = "Profile deleted.";
    }
    catch (Exception ex)
    {
      StatusText = "Delete failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void OpenOverlayEditor()
  {
    var win = new OverlayEditorWindow();
    win.ShowDialog();
  }

  [RelayCommand]
  private async Task ExportPowerStigMapAsync()
  {
    if (SelectedPack == null) return;
    var output = Path.Combine(_paths.GetPackRoot(SelectedPack.PackId), "powerstig_map.csv");
    var controls = await _controls.ListControlsAsync(SelectedPack.PackId, CancellationToken.None);
    WritePowerStigMapCsv(output, controls);
    StatusText = "Map exported: " + output;
    LastOutputPath = output;
  }

  partial void OnSelectedProfileChanged(Profile? value)
  {
    if (value != null)
    {
      LoadProfileFields(value);
      ApplyOverlaySelection(value);
    }
  }

  partial void OnSelectedPackChanged(ContentPack? value)
  {
    if (value == null)
    {
      ClearPackDetails();
      return;
    }

    _ = LoadPackDetailsAsync(value);
  }

  private void LoadProfileFields(Profile profile)
  {
    ProfileName = profile.Name;
    ProfileMode = profile.HardeningMode.ToString();
    ProfileClassification = profile.ClassificationMode.ToString();
    ProfileGraceDays = profile.AutomationPolicy.NewRuleGraceDays;
    ProfileAutoNa = profile.NaPolicy.AutoNaOutOfScope;
    ProfileNaComment = profile.NaPolicy.DefaultNaCommentTemplate;
  }

  private async Task LoadPackDetailsAsync(ContentPack pack)
  {
    PackDetailName = pack.Name;
    PackDetailId = pack.PackId;
    PackDetailReleaseDate = pack.ReleaseDate?.ToString("yyyy-MM-dd") ?? "(unknown)";
    PackDetailImportedAt = pack.ImportedAt.ToString("yyyy-MM-dd HH:mm");
    PackDetailSource = pack.SourceLabel;
    PackDetailHash = pack.ManifestSha256;

    var root = _paths.GetPackRoot(pack.PackId);
    PackDetailRoot = root;

    var controls = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
    var manual = controls.Count(c => c.IsManual);
    PackDetailControls = "Total: " + controls.Count + ", Manual: " + manual;
  }

  private void ClearPackDetails()
  {
    PackDetailName = string.Empty;
    PackDetailId = string.Empty;
    PackDetailReleaseDate = string.Empty;
    PackDetailImportedAt = string.Empty;
    PackDetailSource = string.Empty;
    PackDetailHash = string.Empty;
    PackDetailControls = string.Empty;
    PackDetailRoot = string.Empty;
  }

  [RelayCommand]
  private void OpenPackFolder()
  {
    if (string.IsNullOrWhiteSpace(PackDetailRoot)) return;
    if (!Directory.Exists(PackDetailRoot)) return;
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
      FileName = PackDetailRoot,
      UseShellExecute = true
    });
  }

  private void ApplyOverlaySelection(Profile profile)
  {
    var selected = new HashSet<string>(profile.OverlayIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    foreach (var item in OverlayItems)
      item.IsSelected = selected.Contains(item.Overlay.OverlayId);
  }

  private IReadOnlyList<string> GetSelectedOverlayIds()
  {
    return OverlayItems.Where(i => i.IsSelected).Select(i => i.Overlay.OverlayId).ToList();
  }

  private static HardeningMode ParseMode(string value)
  {
    return Enum.TryParse<HardeningMode>(value, true, out var m) ? m : HardeningMode.Safe;
  }

  private static ClassificationMode ParseClassification(string value)
  {
    return Enum.TryParse<ClassificationMode>(value, true, out var m) ? m : ClassificationMode.Classified;
  }

  private static void WritePowerStigMapCsv(string path, IReadOnlyList<ControlRecord> controls)
  {
    var sb = new System.Text.StringBuilder(controls.Count * 40 + 128);
    sb.AppendLine("RuleId,Title,SettingName,Value,HintSetting,HintValue");
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in controls)
    {
      var ruleId = c.ExternalIds.RuleId;
      if (string.IsNullOrWhiteSpace(ruleId)) continue;
      if (!seen.Add(ruleId)) continue;
      var hintSetting = ExtractHintSetting(c);
      var hintValue = ExtractHintValue(c);
      sb.AppendLine(string.Join(",", Csv(ruleId), Csv(c.Title), "", "", Csv(hintSetting), Csv(hintValue)));
    }
    File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
  }

  private static string ExtractHintSetting(ControlRecord control)
  {
    var text = (control.FixText ?? string.Empty) + "\n" + (control.CheckText ?? string.Empty);
    return ExtractAfterLabel(text, new[] { "Value Name", "Value name", "ValueName" });
  }

  private static string ExtractHintValue(ControlRecord control)
  {
    var text = (control.FixText ?? string.Empty) + "\n" + (control.CheckText ?? string.Empty);
    return ExtractAfterLabel(text, new[] { "Value Data", "Value data", "Value:" });
  }

  private static string ExtractAfterLabel(string text, string[] labels)
  {
    foreach (var label in labels)
    {
      var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
      if (idx < 0) continue;

      var start = idx + label.Length;
      var line = text.Substring(start);
      var nl = line.IndexOfAny(new[] { '\r', '\n' });
      if (nl >= 0) line = line.Substring(0, nl);
      var cleaned = line.Replace(":", string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
    }

    return string.Empty;
  }
}
