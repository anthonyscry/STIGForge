using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using STIGForge.App.Helpers;
using STIGForge.Build;
using STIGForge.Core.Constants;
using STIGForge.Core.Models;
using STIGForge.App.Views;

namespace STIGForge.App;

public partial class MainViewModel
{
  public List<ContentPack> SelectedMissionPacks { get; } = new();

  [RelayCommand]
  private async Task ImportContentPackAsync()
  {
    if (IsBusy) return;
    var ofd = new OpenFileDialog
    {
      Filter = "STIG ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
      Title = "Select STIG content ZIP — single pack or full library",
      Multiselect = true
    };

    if (ofd.ShowDialog() != true) return;

    await SmartImportAsync(ofd.FileNames, "stig_import", PackTypes.Stig);
  }

  [RelayCommand]
  private async Task ImportScapBenchmarkAsync()
  {
    if (IsBusy) return;
    var ofd = new OpenFileDialog
    {
      Filter = "SCAP ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
      Title = "Select SCAP benchmark ZIP — single benchmark or NIWC Atlantic bundle",
      Multiselect = true
    };

    if (ofd.ShowDialog() != true) return;

    await SmartImportAsync(ofd.FileNames, "scap_import", PackTypes.Scap);
  }

  [RelayCommand]
  private async Task ImportGpoPackageAsync()
  {
    if (IsBusy) return;

    var ofd = new OpenFileDialog
    {
      Filter = "GPO/LGPO ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
      Title = "Select GPO/LGPO package ZIP",
      Multiselect = true
    };

    if (ofd.ShowDialog() != true) return;

    await SmartImportAsync(ofd.FileNames, "gpo_lgpo_import", PackTypes.Gpo);
  }

  private async Task SmartImportAsync(IEnumerable<string> zipFiles, string sourceLabel, string contentLabel)
  {
    var candidates = (zipFiles ?? Array.Empty<string>())
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (candidates.Count == 0) return;

    try
    {
      IsBusy = true;
      StatusText = candidates.Count == 1
        ? "Importing " + contentLabel + " content..."
        : "Importing " + candidates.Count + " " + contentLabel + " files...";

      var totalImported = 0;
      var failed = new List<string>();

      foreach (var zip in candidates)
      {
        try
        {
          var imported = await _importer.ImportConsolidatedZipAsync(zip, sourceLabel, CancellationToken.None);
          foreach (var pack in imported)
            AddImportedPack(pack);
          totalImported += imported.Count;
        }
        catch (Exception ex)
        {
          failed.Add(GetSafeFileLabel(zip) + ": " + ex.Message);
        }
      }

      OnPropertyChanged(nameof(ContentPacks));
      RefreshImportLibrary();
      await AutoPopulateApplicablePacksAsync();

      var firstFailure = failed.FirstOrDefault() ?? "Unknown error";
      if (totalImported == 0)
        StatusText = failed.Count > 0 ? "Import failed: " + firstFailure : "No importable content found.";
      else if (failed.Count == 0)
        StatusText = totalImported == 1
          ? "Imported: " + (SelectedPack?.Name ?? "package")
          : "Imported " + totalImported + " " + contentLabel + " packages.";
      else
        StatusText = "Imported " + totalImported + " packages; " + failed.Count + " failed. First error: " + firstFailure;

      if (totalImported > 0)
        _notifications.Success(totalImported == 1 ? "Import complete." : "Import complete: " + totalImported + " packages.");
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

  private void AddImportedPack(ContentPack pack)
  {
    ContentPacks.Insert(0, pack);
    SelectedPack = pack;
  }

  [RelayCommand]
   private void RefreshImportLibrary()
   {
     _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
      {
         var stigItems = new List<ImportedLibraryItem>();
        var scapItems = new List<ImportedLibraryItem>();
        var gpoItems = new List<ImportedLibraryItem>();
        var otherItems = new List<ImportedLibraryItem>();
        var allItems = new List<ImportedLibraryItem>();

        foreach (var pack in ContentPacks.OrderByDescending(p => p.ImportedAt))
        {
         var format = ResolvePackFormat(pack);
         var item = new ImportedLibraryItem
         {
           PackId = pack.PackId,
           Name = pack.Name,
           Format = format,
           SourceLabel = pack.SourceLabel,
           ImportedAt = pack.ImportedAt,
           ImportedAtLabel = pack.ImportedAt.ToString("yyyy-MM-dd HH:mm"),
           ReleaseDateLabel = pack.ReleaseDate?.ToString("yyyy-MM-dd") ?? "(unknown)",
           RootPath = _paths.GetPackRoot(pack.PackId)
         };

          allItems.Add(item);

           if (string.Equals(format, PackTypes.Stig, StringComparison.OrdinalIgnoreCase))
           {
             stigItems.Add(item);
           }
           else if (string.Equals(format, PackTypes.Scap, StringComparison.OrdinalIgnoreCase))
           {
             scapItems.Add(item);
           }
           else if (string.Equals(format, PackTypes.Gpo, StringComparison.OrdinalIgnoreCase)
                  || string.Equals(format, PackTypes.Admx, StringComparison.OrdinalIgnoreCase)
                  || (item.SourceLabel != null && (item.SourceLabel.IndexOf("/" + PackTypes.LocalPolicy, StringComparison.OrdinalIgnoreCase) >= 0 || item.SourceLabel.IndexOf("/" + PackTypes.Admx, StringComparison.OrdinalIgnoreCase) >= 0)))
           {
            gpoItems.Add(item);
           }
           else
           {
            otherItems.Add(item);
           }
        }

        StigLibraryItems.ReplaceAll(stigItems);
        ScapLibraryItems.ReplaceAll(scapItems);
        GpoLibraryItems.ReplaceAll(gpoItems);
        OtherLibraryItems.ReplaceAll(otherItems);
        AllLibraryItems.ReplaceAll(allItems);

        _filteredContentLibrary?.Refresh();
        ImportLibraryStatus = "STIG: " + StigLibraryItems.Count
          + " | SCAP: " + ScapLibraryItems.Count
          + " | GPO: " + GpoLibraryItems.Count
          + " | Other: " + OtherLibraryItems.Count;
     });
   }

  [RelayCommand]
  private void OpenSelectedLibraryItem()
  {
    var selected = SelectedStigLibraryItem ?? SelectedScapLibraryItem ?? SelectedGpoLibraryItem ?? SelectedOtherLibraryItem;
    if (selected == null)
    {
      StatusText = "Select a library item first.";
      return;
    }

    if (!Directory.Exists(selected.RootPath))
    {
      StatusText = "Library path not found: " + selected.RootPath;
      return;
    }

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = selected.RootPath,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      StatusText = "Failed to open library path: " + ex.Message;
    }
  }

  private string ResolvePackFormat(ContentPack pack)
  {
    var compatibilityPath = Path.Combine(_paths.GetPackRoot(pack.PackId), "compatibility_matrix.json");
    if (File.Exists(compatibilityPath))
    {
      try
      {
        using var doc = JsonDocument.Parse(File.ReadAllText(compatibilityPath));
        if (doc.RootElement.TryGetProperty("detectedFormat", out var detected)
            && detected.ValueKind == JsonValueKind.String)
        {
          var detectedValue = detected.GetString();
          if (!string.IsNullOrWhiteSpace(detectedValue)
              && !string.Equals(detectedValue, "Unknown", StringComparison.OrdinalIgnoreCase))
          {
             if (string.Equals(detectedValue, "Scap", StringComparison.OrdinalIgnoreCase)) return PackTypes.Scap;
             if (string.Equals(detectedValue, "Stig", StringComparison.OrdinalIgnoreCase)) return PackTypes.Stig;
             if (string.Equals(detectedValue, "Gpo", StringComparison.OrdinalIgnoreCase))
             {
               var admxHint = (pack.SourceLabel + " " + pack.Name);
               if (admxHint.IndexOf("admx", StringComparison.OrdinalIgnoreCase) >= 0)
                 return PackTypes.Admx;
               return PackTypes.Gpo;
             }
            return detectedValue.ToUpperInvariant();
          }
        }
      }
      catch
      {
      }
    }

    var hint = (pack.SourceLabel + " " + pack.Name);
    if (hint.IndexOf("scap", StringComparison.OrdinalIgnoreCase) >= 0) return PackTypes.Scap;
    if (hint.IndexOf("admx", StringComparison.OrdinalIgnoreCase) >= 0) return PackTypes.Admx;
    if (hint.IndexOf("gpo", StringComparison.OrdinalIgnoreCase) >= 0 || hint.IndexOf("lgpo", StringComparison.OrdinalIgnoreCase) >= 0) return PackTypes.Gpo;
    return PackTypes.Stig;
  }

  private static string GetSafeFileLabel(string path)
  {
    try
    {
      var fileName = Path.GetFileName(path);
      return string.IsNullOrWhiteSpace(fileName) ? "package" : fileName;
    }
    catch
    {
      return "package";
    }
  }

  [RelayCommand]
  private async Task BuildBundleAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;

      var packs = SelectedMissionPacks.Count > 0
        ? SelectedMissionPacks.ToList()
        : SelectedPack != null ? new List<ContentPack> { SelectedPack } : new List<ContentPack>();

      if (packs.Count == 0)
      {
        StatusText = "No content selected. Use 'Select Content' on the Import tab first.";
        return;
      }

      var profile = SelectedProfile;
      if (profile == null)
      {
        StatusText = "No profile selected. Go to the Profile tab and save a profile first.";
        return;
      }

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

      var built = 0;
      BundleBuildResult? lastResult = null;
      foreach (var pack in packs)
      {
        built++;
        StatusText = packs.Count == 1
          ? "Building bundle for " + pack.Name + "..."
          : "Building bundle " + built + " of " + packs.Count + ": " + pack.Name + "...";

        var controlList = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
        lastResult = await _builder.BuildAsync(new BundleBuildRequest
        {
          Pack = pack,
          Profile = profile,
          Controls = controlList,
          Overlays = overlays,
          ToolVersion = "0.1.0-dev"
        }, CancellationToken.None);

        AddRecentBundle(lastResult.BundleRoot);
      }

      if (lastResult != null)
      {
        var gatePath = Path.Combine(lastResult.BundleRoot, BundlePaths.ReportsDirectory, "automation_gate.json");
        BuildGateStatus = File.Exists(gatePath) ? "Automation gate: " + gatePath : "Automation gate: (not found)";
        AutomationGatePath = File.Exists(gatePath) ? gatePath : string.Empty;
        BundleRoot = lastResult.BundleRoot;
      }

      StatusText = packs.Count == 1
        ? "Build complete: " + packs[0].Name
        : "Build complete: " + packs.Count + " bundles built.";
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
      OverlayItems.ReplaceAll(overlays.Select(o => new OverlayItem(o)));
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
  private async Task DeleteSelectedPacks()
  {
    var targets = SelectedLibraryItems.Count > 0
      ? SelectedLibraryItems.ToList()
      : SelectedLibraryItem != null ? new List<ImportedLibraryItem> { SelectedLibraryItem } : new List<ImportedLibraryItem>();

    if (targets.Count == 0)
    {
      StatusText = "No packs selected.";
      return;
    }

    var msg = targets.Count == 1
      ? $"Delete \"{targets[0].Name}\"?\n\nThis removes the pack and all its controls."
      : $"Delete {targets.Count} selected packs?\n\nThis removes them and all their controls.";

    var result = System.Windows.MessageBox.Show(msg, "Confirm Delete",
      System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
    if (result != System.Windows.MessageBoxResult.Yes) return;

    try
    {
      IsBusy = true;
      var deleted = 0;
      foreach (var item in targets)
      {
        var pack = ContentPacks.FirstOrDefault(p => p.PackId == item.PackId);
        if (pack == null) continue;

        await _packs.DeleteAsync(pack.PackId, CancellationToken.None);
        ContentPacks.Remove(pack);
        deleted++;
      }

      SelectedPack = ContentPacks.Count > 0 ? ContentPacks[0] : null;
      OnPropertyChanged(nameof(ContentPacks));
      RefreshImportLibrary();
      StatusText = deleted == 1 ? "Pack deleted." : $"{deleted} packs deleted.";
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
    await WritePowerStigMapCsvAsync(output, controls);
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

    _ = LoadPackDetailsAsync(value).ContinueWith(t =>
    {
      if (t.IsFaulted)
        System.Diagnostics.Trace.TraceError("LoadPackDetailsAsync failed: " + t.Exception?.Flatten().Message);
    }, System.Threading.Tasks.TaskScheduler.Default);
  }

  partial void OnSelectedStigLibraryItemChanged(ImportedLibraryItem? value)
  {
    if (value == null) return;
    if (SelectedScapLibraryItem != null)
      SelectedScapLibraryItem = null;
    if (SelectedGpoLibraryItem != null)
      SelectedGpoLibraryItem = null;
    if (SelectedOtherLibraryItem != null)
      SelectedOtherLibraryItem = null;
  }

  partial void OnSelectedScapLibraryItemChanged(ImportedLibraryItem? value)
  {
    if (value == null) return;
    if (SelectedStigLibraryItem != null)
      SelectedStigLibraryItem = null;
    if (SelectedGpoLibraryItem != null)
      SelectedGpoLibraryItem = null;
    if (SelectedOtherLibraryItem != null)
      SelectedOtherLibraryItem = null;
  }

  partial void OnSelectedGpoLibraryItemChanged(ImportedLibraryItem? value)
  {
    if (value == null) return;
    if (SelectedStigLibraryItem != null)
      SelectedStigLibraryItem = null;
    if (SelectedScapLibraryItem != null)
      SelectedScapLibraryItem = null;
    if (SelectedOtherLibraryItem != null)
      SelectedOtherLibraryItem = null;
  }

   partial void OnSelectedOtherLibraryItemChanged(ImportedLibraryItem? value)
   {
     if (value == null) return;
     if (SelectedStigLibraryItem != null)
       SelectedStigLibraryItem = null;
     if (SelectedScapLibraryItem != null)
       SelectedScapLibraryItem = null;
     if (SelectedGpoLibraryItem != null)
       SelectedGpoLibraryItem = null;
   }

   partial void OnSelectedLibraryItemChanged(ImportedLibraryItem? value)
   {
     if (value == null) return;
     var pack = ContentPacks.FirstOrDefault(p => p.PackId == value.PackId);
     if (pack != null) SelectedPack = pack;
   }

   partial void OnContentLibraryFilterChanged(string value)
   {
     _filteredContentLibrary?.Refresh();
     OnPropertyChanged(nameof(IsFilterAll));
     OnPropertyChanged(nameof(IsFilterStig));
     OnPropertyChanged(nameof(IsFilterScap));
     OnPropertyChanged(nameof(IsFilterGpo));
     OnPropertyChanged(nameof(FilteredContentLibrary));
   }

   partial void OnContentSearchTextChanged(string value)
   {
     _filteredContentLibrary?.Refresh();
   }

   private ICollectionView CreateFilteredLibraryView()
   {
      var view = CollectionViewSource.GetDefaultView(AllLibraryItems);
      view.Filter = obj =>
      {
        if (obj is not ImportedLibraryItem item) return false;
        if (ContentLibraryFilter != "All")
        {
          if (string.Equals(ContentLibraryFilter, PackTypes.Gpo, StringComparison.OrdinalIgnoreCase))
          {
            // GPO filter includes both GPO and ADMX formats
            if (!string.Equals(item.Format, PackTypes.Gpo, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.Format, PackTypes.Admx, StringComparison.OrdinalIgnoreCase))
              return false;
          }
          else if (!string.Equals(item.Format, ContentLibraryFilter, StringComparison.OrdinalIgnoreCase))
            return false;
        }
       if (!string.IsNullOrWhiteSpace(ContentSearchText) &&
            item.Name.IndexOf(ContentSearchText, StringComparison.OrdinalIgnoreCase) < 0 &&
            item.PackId.IndexOf(ContentSearchText, StringComparison.OrdinalIgnoreCase) < 0)
         return false;
       return true;
     };
     return view;
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
     PackDetailFormat = ResolvePackFormat(pack);
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
     PackDetailFormat = string.Empty;
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
    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = PackDetailRoot,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      StatusText = "Failed to open pack folder: " + ex.Message;
    }
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

  private static Task WritePowerStigMapCsvAsync(string path, IReadOnlyList<ControlRecord> controls)
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
    return File.WriteAllTextAsync(path, sb.ToString(), System.Text.Encoding.UTF8);
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
