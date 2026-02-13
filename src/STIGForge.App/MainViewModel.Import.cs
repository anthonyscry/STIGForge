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

  // ── Content Picker ─────────────────────────────────────────────────

  [RelayCommand]
  private async Task OpenContentPicker()
  {
    if (ContentPacks.Count == 0)
    {
      StatusText = "No content imported yet. Import STIG, SCAP, or GPO packages first.";
      return;
    }

    try
    {
      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);
      await RefreshApplicablePackIdsAsync(info);
    }
    catch
    {
    }

    var items = ContentPacks.Select(pack => new ContentPickerItem
    {
      PackId = pack.PackId,
      Name = pack.Name,
      Format = ResolvePackFormat(pack),
      SourceLabel = pack.SourceLabel,
      ImportedAtLabel = pack.ImportedAt.ToString("yyyy-MM-dd HH:mm"),
      IsSelected = SelectedMissionPacks.Any(s => s.PackId == pack.PackId)
    }).ToList();

    var dialog = new ContentPickerDialog(items, ApplicablePackIds);
    dialog.Owner = System.Windows.Application.Current.MainWindow;
    if (dialog.ShowDialog() != true) return;

    var selectedIds = new HashSet<string>(dialog.SelectedPackIds, StringComparer.OrdinalIgnoreCase);
    SelectedMissionPacks.Clear();
    foreach (var pack in ContentPacks)
    {
      if (selectedIds.Contains(pack.PackId))
        SelectedMissionPacks.Add(pack);
    }

    if (SelectedMissionPacks.Count == 0)
    {
      SelectedPack = null;
      SelectedContentSummary = "No content selected.";
    }
    else if (SelectedMissionPacks.Count == 1)
    {
      SelectedPack = SelectedMissionPacks[0];
      SelectedContentSummary = "Selected: " + SelectedMissionPacks[0].Name;
    }
    else
    {
      SelectedPack = SelectedMissionPacks[0];
      SelectedContentSummary = SelectedMissionPacks.Count + " packs selected: "
        + string.Join(", ", SelectedMissionPacks.Select(p => p.Name).Take(3));
      if (SelectedMissionPacks.Count > 3)
        SelectedContentSummary += " + " + (SelectedMissionPacks.Count - 3) + " more";
    }

    StatusText = "Content selection updated.";
  }

  // ── Machine Applicability Scan ──────────────────────────────────────

  [RelayCommand]
  private async Task ScanMachineApplicabilityAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      MachineApplicabilityStatus = "Scanning machine...";
      MachineApplicablePacks = "";
      MachineRecommendations = "";

      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);

      var machineLines = new List<string>
      {
        "Machine: " + info.Hostname,
        "OS: " + info.ProductName + " (Build " + info.BuildNumber + ")",
        "Role: " + info.Role,
        "Detected target: " + info.OsTarget
      };

      if (info.IsServer && info.InstalledFeatures.Count > 0)
      {
        machineLines.Add("Server features: " + string.Join(", ", info.InstalledFeatures.Take(10)));
        if (info.InstalledFeatures.Count > 10)
          machineLines.Add("  ... and " + (info.InstalledFeatures.Count - 10) + " more");
      }

      MachineApplicabilityStatus = string.Join("\n", machineLines);

      var filteredPacks = await RefreshApplicablePackIdsAsync(info);
      var packLines = new List<string>();
      if (ContentPacks.Count > 0)
      {
        var applicable = filteredPacks.Select(p => p.Name).ToList();
        if (applicable.Count > 0)
        {
          packLines.Add("Applicable imported packs (" + applicable.Count + "):");
          foreach (var name in applicable)
            packLines.Add("  " + name);
        }
        else
        {
          packLines.Add("No imported packs match this machine.");
          packLines.Add("Import the applicable STIG content above.");
        }
      }
      else
      {
        packLines.Add("No content packs imported yet.");
        packLines.Add("Import STIGs above, then re-scan.");
      }

      MachineApplicablePacks = string.Join("\n", packLines);

      var recommendations = GetStigRecommendations(info);
      MachineRecommendations = recommendations.Count > 0
        ? string.Join("\n", recommendations)
        : "";

      StatusText = "Machine scan complete: " + info.OsTarget + " / " + info.Role;
    }
    catch (Exception ex)
    {
      MachineApplicabilityStatus = "Scan failed: " + ex.Message;
      MachineApplicablePacks = "";
      MachineRecommendations = "";
      StatusText = "Machine scan failed.";
    }
    finally
    {
      IsBusy = false;
    }
  }

  private MachineInfo DetectMachineInfo()
  {
    var info = new MachineInfo { Hostname = Environment.MachineName };

    // Read OS info from registry
    try
    {
      using var ntKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
      if (ntKey != null)
      {
        info.ProductName = ntKey.GetValue("ProductName") as string ?? "Unknown Windows";
        info.BuildNumber = ntKey.GetValue("CurrentBuildNumber") as string
                           ?? ntKey.GetValue("CurrentBuild") as string
                           ?? "0";
        info.EditionId = ntKey.GetValue("EditionID") as string ?? "";
        info.DisplayVersion = ntKey.GetValue("DisplayVersion") as string ?? "";
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

    // Detect product type (Workstation / Server / DC)
    try
    {
      using var prodKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
      var productType = prodKey?.GetValue("ProductType") as string ?? "";
      if (string.Equals(productType, "WinNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Workstation";
        info.RoleTemplate = RoleTemplate.Workstation;
        info.IsServer = false;
      }
      else if (string.Equals(productType, "LanmanNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Domain Controller";
        info.RoleTemplate = RoleTemplate.DomainController;
        info.IsServer = true;
      }
      else if (string.Equals(productType, "ServerNT", StringComparison.OrdinalIgnoreCase))
      {
        info.Role = "Member Server";
        info.RoleTemplate = RoleTemplate.MemberServer;
        info.IsServer = true;
      }
      else
      {
        info.Role = "Unknown (" + productType + ")";
        info.RoleTemplate = RoleTemplate.Workstation;
        info.IsServer = false;
      }
    }
    catch
    {
      info.Role = "Unknown";
      info.RoleTemplate = RoleTemplate.Workstation;
    }

    // Map build number to OsTarget
    info.OsTarget = MapBuildToOsTarget(info.BuildNumber, info.IsServer, info.EditionId);

    // Detect installed server features (IIS, DNS, DHCP, etc.)
    if (info.IsServer)
    {
      try
      {
        using var featureKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\ServerManager\ServicingStorage\ServerComponentCache");
        if (featureKey != null)
        {
          foreach (var subName in featureKey.GetSubKeyNames())
          {
            try
            {
              using var sub = featureKey.OpenSubKey(subName);
              var installState = sub?.GetValue("InstallState");
              if (installState is int state && state == 1)
                info.InstalledFeatures.Add(subName);
            }
            catch (Exception ex)
            {
              System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
            }
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
      }
    }

    // Detect IIS on workstation
    if (!info.IsServer)
    {
      try
      {
        using var iisKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
        if (iisKey != null)
        {
          var majorVersion = iisKey.GetValue("MajorVersion");
          if (majorVersion != null)
            info.InstalledFeatures.Add("IIS " + majorVersion);
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
      }
    }

    // Detect SQL Server instances
    try
    {
      using var sqlKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
      if (sqlKey != null && sqlKey.GetValueNames().Length > 0)
        info.InstalledFeatures.Add("SQL Server");
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

    // Detect .NET Framework version
    try
    {
      using var ndpKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
      var release = ndpKey?.GetValue("Release") as int?;
      if (release != null)
        info.InstalledFeatures.Add(".NET Framework 4.x");
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Registry/feature detection failed: " + ex.Message);
    }

     // Detect Google Chrome
     try
     {
       using var chromeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
       if (chromeKey != null)
         info.InstalledFeatures.Add("Google Chrome");
     }
     catch (Exception ex)
     {
       _titleBarLogger?.LogDebug(ex, "Chrome registry detection failed");
     }

     // Detect Microsoft Edge
     try
     {
       using var edgeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Edge");
       if (edgeKey != null)
       {
         info.InstalledFeatures.Add("Microsoft Edge");
       }
       else
       {
         using var edgeKey64 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Edge");
         if (edgeKey64 != null)
           info.InstalledFeatures.Add("Microsoft Edge");
       }
     }
      catch (Exception ex)
      {
        _titleBarLogger?.LogDebug(ex, "Edge registry detection failed");
      }

     // Detect Mozilla Firefox
     try
     {
       using var ffKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
       if (ffKey != null)
         info.InstalledFeatures.Add("Mozilla Firefox");
     }
      catch (Exception ex)
      {
        _titleBarLogger?.LogDebug(ex, "Firefox registry detection failed");
      }

    // Detect Adobe Acrobat / Reader
    try
    {
      using var adobeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Acrobat Reader");
      using var acrobatKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Adobe Acrobat");
      if (adobeKey != null || acrobatKey != null)
        info.InstalledFeatures.Add("Adobe Acrobat");
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Adobe registry detection failed");
    }

    // Detect Microsoft Office
    try
    {
      using var officeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration");
      if (officeKey != null)
        info.InstalledFeatures.Add("Microsoft Office");
      else
      {
        using var office16 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Office\16.0\Common\InstallRoot");
        if (office16 != null)
          info.InstalledFeatures.Add("Microsoft Office");
      }
    }
    catch (Exception ex)
    {
      _titleBarLogger?.LogDebug(ex, "Office registry detection failed");
    }

    return info;
  }

  private static OsTarget MapBuildToOsTarget(string buildNumber, bool isServer, string editionId)
  {
    if (!int.TryParse(buildNumber, out var build))
      return OsTarget.Unknown;

    if (isServer)
    {
      // Server 2022: build 20348+
      // Server 2019: build 17763
      // Server 2016: build 14393
      if (build >= 20348) return OsTarget.Server2022;
      if (build >= 17763) return OsTarget.Server2019;
      return OsTarget.Unknown;
    }

    // Workstation
    // Windows 11: build 22000+
    // Windows 10: build 10240–21996
    if (build >= 22000) return OsTarget.Win11;
    if (build >= 10240) return OsTarget.Win10;
    return OsTarget.Unknown;
  }

   private List<ContentPack> PreferHigherVersionStigs(List<ContentPack> packs)
   {
     var grouped = packs
       .GroupBy(pack => BuildFormatScopedProductKey(pack), StringComparer.OrdinalIgnoreCase)
       .ToList();

     var result = new List<ContentPack>();

     foreach (var group in grouped)
     {
       if (group.Count() == 1)
       {
         result.Add(group.First());
       }
       else
       {
         var highest = group
           .OrderByDescending(pack => ExtractVersionTuple(pack.Name))
           .ThenByDescending(pack => pack.ImportedAt)
           .First();
         result.Add(highest);
       }
     }

     return result;
   }

   private string BuildFormatScopedProductKey(ContentPack pack)
   {
     var format = (ResolvePackFormat(pack) ?? string.Empty).Trim();
     if (string.IsNullOrWhiteSpace(format))
       format = "UNKNOWN";

     var product = ExtractProductName(pack.Name ?? string.Empty);
     return format.ToUpperInvariant() + "|" + product;
   }

   private static string ExtractProductName(string packName)
   {
     var match = System.Text.RegularExpressions.Regex.Match(packName, @"^(.+?)\s+V\d+R\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
     if (match.Success)
       return match.Groups[1].Value.Trim();
     return packName;
   }

   private static (int V, int R) ExtractVersionTuple(string packName)
   {
     var match = System.Text.RegularExpressions.Regex.Match(packName, @"V(\d+)R(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
     if (match.Success && int.TryParse(match.Groups[1].Value, out var v) && int.TryParse(match.Groups[2].Value, out var r))
       return (v, r);
     return (0, 0);
   }

   private static readonly string[] UniversalPackKeywords = new[]
   {
     "Defender", "Windows Firewall", "Firewall with Advanced Security",
     "Microsoft Edge", ".NET Framework", "DotNet"
   };

  private async Task<bool> IsPackApplicableAsync(ContentPack pack, MachineInfo info)
  {
    var name = (pack.Name + " " + pack.SourceLabel).Replace('_', ' ');

    // ── 1. Control-level OsTarget match (positive only — never early-exit false) ──
    // A positive hit means the pack's controls explicitly target this OS.
    // If controls target a DIFFERENT OS we still fall through — the pack may
    // be universal (Defender, Edge, etc.) or matched by name/feature below.
    try
    {
      var controls = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
      if (controls.Count > 0)
      {
        var packOsTargets = controls
          .Select(c => c.Applicability.OsTarget)
          .Where(t => t != OsTarget.Unknown)
          .Distinct()
          .ToList();

        if (packOsTargets.Count > 0 && packOsTargets.Contains(info.OsTarget))
          return true;
        // DO NOT return false — fall through to name/keyword/feature matching
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Pack applicability check failed: " + ex.Message);
    }

    // ── 2. OS label matching (works for STIG, SCAP, GPO, ADMX — any format) ──
    var osLabels = info.OsTarget switch
    {
      OsTarget.Win11 => new[] { "Windows 11", "Win11" },
      OsTarget.Win10 => new[] { "Windows 10", "Win10" },
      OsTarget.Server2022 => new[] { "Server 2022" },
      OsTarget.Server2019 => new[] { "Server 2019" },
      _ => Array.Empty<string>()
    };

    foreach (var label in osLabels)
    {
      if (name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // ── 3. Universal packs — always apply to any Windows machine ──
    // Defender, Firewall, Edge, .NET etc. ship with every Windows install.
    // This catches STIG, SCAP, and GPO/ADMX packs for these products.
    foreach (var keyword in UniversalPackKeywords)
    {
      if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // ── 4. Role-based matching ──
    if (info.RoleTemplate == RoleTemplate.DomainController
        && name.IndexOf("Domain Controller", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (info.RoleTemplate == RoleTemplate.MemberServer
        && name.IndexOf("Member Server", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (info.RoleTemplate == RoleTemplate.DomainController
        && (name.IndexOf("Active Directory", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Domain", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("AD Forest", StringComparison.OrdinalIgnoreCase) >= 0))
      return true;

    // ── 5. Feature-based matching (IIS, DNS, SQL, DHCP, .NET, etc.) ──
    foreach (var feature in info.InstalledFeatures)
    {
      if (feature.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
       if (feature.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0
           && name.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0)
         return true;
       if (feature.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0
            && name.IndexOf("Microsoft Edge", StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
       if (feature.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0
           && name.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0)
         return true;
      if (feature.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    // ── 6. GPO / ADMX / LocalPolicy — must match this machine's OS ──
    var format = ResolvePackFormat(pack);
    var isGpoFormat = string.Equals(format, PackTypes.Gpo, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(format, PackTypes.Admx, StringComparison.OrdinalIgnoreCase);
    var isLocalPolicy = pack.SourceLabel != null
      && pack.SourceLabel.IndexOf("/LocalPolicy", StringComparison.OrdinalIgnoreCase) >= 0;

    if (isGpoFormat || isLocalPolicy)
    {
      // LocalPolicy packs include the OS in their name ("Local Policy – Windows 11").
      // Only include if the pack name contains this machine's OS label.
      foreach (var label in osLabels)
      {
        if (name.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }

      // Also check control-level OsTarget (already checked in step 1 above,
      // but step 1 only fires if controls exist and have non-Unknown targets).
      // If we reach here, the GPO pack doesn't match this machine's OS.
      return false;
    }

    return false;
  }

  private static List<string> GetStigRecommendations(MachineInfo info)
  {
    var recs = new List<string>();

    var osLabel = info.OsTarget switch
    {
      OsTarget.Win11 => "Windows 11",
      OsTarget.Win10 => "Windows 10",
      OsTarget.Server2022 => "Windows Server 2022",
      OsTarget.Server2019 => "Windows Server 2019",
      _ => null
    };

    var roleTag = info.RoleTemplate switch
    {
      RoleTemplate.DomainController when info.IsServer => " Domain Controller",
      RoleTemplate.MemberServer when info.IsServer => " Member Server",
      _ => ""
    };

    recs.Add("[STIGs]");
    if (osLabel != null)
      recs.Add("  Microsoft " + osLabel + roleTag + " STIG");
    recs.Add("  Microsoft Windows Defender Antivirus STIG");
    recs.Add("  Microsoft Windows Firewall with Advanced Security STIG");
    recs.Add("  Microsoft Edge STIG");
    recs.Add("  Microsoft .NET Framework 4.0 STIG");

    foreach (var feature in info.InstalledFeatures)
    {
      if (feature.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        recs.Add("  Microsoft IIS 10.0 Site STIG");
        recs.Add("  Microsoft IIS 10.0 Server STIG");
      }
      if (feature.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft Windows DNS Server STIG");
      if (feature.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft SQL Server STIG");
      if (feature.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0)
        recs.Add("  Microsoft Windows DHCP Server STIG");
    }

    if (info.RoleTemplate == RoleTemplate.DomainController)
    {
      recs.Add("  Active Directory Domain STIG");
      recs.Add("  Active Directory Forest STIG");
    }

    recs.Add("");
    recs.Add("[SCAP Benchmarks]");
    if (osLabel != null)
      recs.Add("  " + osLabel + roleTag + " SCAP Benchmark");
    recs.Add("  Defender / Firewall / Edge / .NET benchmarks (if imported)");

    recs.Add("");
    recs.Add("");
    recs.Add("[GPO / ADMX]");
    if (osLabel != null)
      recs.Add("  " + osLabel + " Security Baseline GPO");
    recs.Add("  Defender / Edge / Firewall ADMX templates (if imported)");

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var normalized = new List<string>(recs.Count);
    foreach (var rec in recs)
    {
      if (string.IsNullOrWhiteSpace(rec))
      {
        normalized.Add(string.Empty);
        continue;
      }

      if (seen.Add(rec))
        normalized.Add(rec);
    }

    return normalized;
  }

  private async Task<List<ContentPack>> RefreshApplicablePackIdsAsync(MachineInfo info)
  {
    ApplicablePackIds.Clear();
    if (ContentPacks.Count == 0)
      return new List<ContentPack>();

    var applicablePacks = new List<ContentPack>();
    foreach (var pack in ContentPacks)
    {
      if (await IsPackApplicableAsync(pack, info))
        applicablePacks.Add(pack);
    }

    var filteredPacks = PreferHigherVersionStigs(applicablePacks);
    foreach (var pack in filteredPacks)
    {
      var packId = (pack.PackId ?? string.Empty).Trim();
      if (!string.IsNullOrWhiteSpace(packId))
        ApplicablePackIds.Add(packId);
    }

    return filteredPacks;
  }

  private async Task AutoPopulateApplicablePacksAsync()
  {
    if (ContentPacks.Count == 0) return;
    try
    {
      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);
      await RefreshApplicablePackIdsAsync(info);
    }
    catch
    {
    }
  }

  private sealed class MachineInfo
  {
    public string Hostname { get; set; } = "";
    public string ProductName { get; set; } = "Unknown Windows";
    public string BuildNumber { get; set; } = "0";
    public string EditionId { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Role { get; set; } = "Unknown";
    public RoleTemplate RoleTemplate { get; set; }
    public OsTarget OsTarget { get; set; } = OsTarget.Unknown;
    public bool IsServer { get; set; }
    public List<string> InstalledFeatures { get; set; } = new();
  }
}
