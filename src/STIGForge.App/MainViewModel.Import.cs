using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using STIGForge.Build;
using STIGForge.Core.Models;
using STIGForge.Content.Import;
using STIGForge.Infrastructure.Hashing;
using STIGForge.App.Views;

namespace STIGForge.App;

public partial class MainViewModel
{
  public List<ContentPack> SelectedMissionPacks { get; } = new();
  private readonly Dictionary<string, string> _formatCache = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, HashSet<string>> _benchmarkIdCache = new(StringComparer.OrdinalIgnoreCase);

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

    await SmartImportAsync(ofd.FileNames, "stig_import", "STIG");
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

    await SmartImportAsync(ofd.FileNames, "scap_import", "SCAP");
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

    await SmartImportAsync(ofd.FileNames, "gpo_lgpo_import", "GPO");
  }

  [RelayCommand]
  private async Task ScanImportFolderAsync()
  {
    if (IsBusy) return;

    try
    {
      IsBusy = true;
      var importFolder = ResolveScanImportFolderPath();
      if (!Directory.Exists(importFolder))
      {
        StatusText = "Import folder not found: " + importFolder;
        return;
      }

      StatusText = "Scanning import folder...";
      var scanner = new ImportInboxScanner(new Sha256HashingService());
      var scan = await scanner.ScanAsync(importFolder, _cts.Token);
      var dedup = new ImportDedupService();
      var deduped = dedup.Resolve(scan.Candidates);

      var contentWinners = deduped.Winners
        .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
        .ToList();
      var toolWinners = deduped.Winners
        .Where(c => c.ArtifactKind == ImportArtifactKind.Tool)
        .ToList();

      var importedPackCount = 0;
      var importedToolCount = 0;
      var failures = new List<string>();

      foreach (var candidate in contentWinners)
      {
        try
        {
          var imported = await _importer.ImportConsolidatedZipAsync(
            candidate.ZipPath,
            MapSourceLabel(candidate.ArtifactKind),
            _cts.Token);
          importedPackCount += imported.Count;
          foreach (var pack in imported)
            AddImportedPack(pack);
        }
        catch (Exception ex)
        {
          failures.Add(candidate.FileName + ": " + ex.Message);
        }
      }

      if (toolWinners.Count > 0)
      {
        try
        {
          importedToolCount = ImportToolBundles(toolWinners);
        }
        catch (Exception ex)
        {
          failures.Add("Tool import failed: " + ex.Message);
        }
      }

      RefreshImportLibrary();
      var warningCount = scan.Warnings.Count + deduped.Suppressed.Count;

      StatusText = "Scan complete: imported packs=" + importedPackCount
        + ", tools=" + importedToolCount
        + ", warnings=" + warningCount
        + (failures.Count > 0 ? ", failures=" + failures.Count : "");
    }
    catch (Exception ex)
    {
      StatusText = "Scan import folder failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private string ResolveScanImportFolderPath()
  {
    var appRoot = _paths.GetAppDataRoot();
    var repoRoot = Directory.GetParent(appRoot)?.FullName;
    if (!string.IsNullOrWhiteSpace(repoRoot))
    {
      var projectImport = Path.Combine(repoRoot, "import");
      if (Directory.Exists(projectImport))
        return projectImport;
    }

    var fallback = _paths.GetImportInboxRoot();
    Directory.CreateDirectory(fallback);
    return fallback;
  }

  private static string MapSourceLabel(ImportArtifactKind kind)
  {
    return kind switch
    {
      ImportArtifactKind.Stig => "stig_import",
      ImportArtifactKind.Scap => "scap_import",
      ImportArtifactKind.Gpo => "gpo_lgpo_import",
      ImportArtifactKind.Admx => "admx_import",
      _ => "import_scan"
    };
  }

  private int ImportToolBundles(IReadOnlyList<ImportInboxCandidate> toolCandidates)
  {
    var imported = 0;
    var toolsRoot = _paths.GetToolsRoot();
    Directory.CreateDirectory(toolsRoot);
    var notes = new List<string>();

    foreach (var candidate in toolCandidates)
    {
      var destination = candidate.ToolKind switch
      {
        ToolArtifactKind.EvaluateStig => Path.Combine(toolsRoot, "Evaluate-STIG"),
        ToolArtifactKind.Scc => Path.Combine(toolsRoot, "SCC"),
        ToolArtifactKind.PowerStig => Path.Combine(toolsRoot, "PowerSTIG"),
        _ => string.Empty
      };

      if (string.IsNullOrWhiteSpace(destination))
        continue;

      ExtractArchiveWithNestedZips(candidate.ZipPath, destination, nestedPasses: 2, notes);
      imported++;

      if (candidate.ToolKind == ToolArtifactKind.EvaluateStig)
      {
        var script = FindFirstFileByName(destination, "Evaluate-STIG.ps1");
        if (!string.IsNullOrWhiteSpace(script))
          EvaluateStigRoot = Path.GetDirectoryName(script) ?? EvaluateStigRoot;
      }
      else if (candidate.ToolKind == ToolArtifactKind.Scc)
      {
        var scc = FindFirstFileByName(destination, "scc.exe");
        if (!string.IsNullOrWhiteSpace(scc))
          ScapCommandPath = scc;
      }
      else if (candidate.ToolKind == ToolArtifactKind.PowerStig)
      {
        var module = FindFirstFileByName(destination, "PowerSTIG.psd1")
          ?? FindFirstFileByName(destination, "PowerStig.psd1");
        if (!string.IsNullOrWhiteSpace(module))
          PowerStigModulePath = module;
      }
    }

    return imported;
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
      IsProgressIndeterminate = false;
      ProgressValue = 0;
      ProgressMax = candidates.Count;
      StatusText = candidates.Count == 1
        ? "Importing " + contentLabel + " content..."
        : "Importing " + candidates.Count + " " + contentLabel + " files...";

      var totalImported = 0;
      var failed = new List<string>();
      var importLock = new object();

      await Parallel.ForEachAsync(candidates, new ParallelOptions
      {
          MaxDegreeOfParallelism = Math.Min(4, candidates.Count),
          CancellationToken = _cts.Token
      }, async (zip, ct) =>
      {
        try
        {
          var imported = await _importer.ImportConsolidatedZipAsync(zip, sourceLabel, ct);
          lock (importLock)
          {
            totalImported += imported.Count;
          }
          System.Windows.Application.Current.Dispatcher.Invoke(() =>
          {
            foreach (var pack in imported)
              AddImportedPack(pack);
            ProgressValue++;
          });
        }
        catch (Exception ex)
        {
          lock (importLock)
          {
            failed.Add(GetSafeFileLabel(zip) + ": " + ex.Message);
          }
          System.Windows.Application.Current.Dispatcher.Invoke(() => ProgressValue++);
        }
      });

      RefreshImportLibrary();

       var firstFailure = failed.FirstOrDefault() ?? "Unknown error";
       if (totalImported == 0)
         SetStatus(failed.Count > 0 ? "Import failed: " + firstFailure : "No importable content found.", failed.Count > 0 ? "Error" : "Warning");
       else if (failed.Count == 0)
         SetStatus(totalImported == 1
           ? "Imported: " + (SelectedPack?.Name ?? "package")
           : "Imported " + totalImported + " " + contentLabel + " packages.", "Success");
       else
         SetStatus("Imported " + totalImported + " packages; " + failed.Count + " failed. First error: " + firstFailure, "Warning");
    }
     catch (Exception ex)
     {
       SetStatus("Import failed: " + ex.Message, "Error");
     }
     finally
     {
       IsProgressIndeterminate = true;
       IsBusy = false;
     }
  }

  private void AddImportedPack(ContentPack pack)
  {
    ContentPacks.Insert(0, pack);
    _benchmarkIdCache.Remove(pack.PackId);
    SelectedPack = pack;
  }

  [RelayCommand]
   private void RefreshImportLibrary()
   {
     System.Windows.Application.Current.Dispatcher.Invoke(() =>
     {
       StigLibraryItems.Clear();
       ScapLibraryItems.Clear();
       GpoLibraryItems.Clear();
       AdmxLibraryItems.Clear();
       OtherLibraryItems.Clear();
       AllLibraryItems.Clear();

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

         AllLibraryItems.Add(item);

          if (string.Equals(format, "STIG", StringComparison.OrdinalIgnoreCase))
          {
            StigLibraryItems.Add(item);
          }
          else if (string.Equals(format, "SCAP", StringComparison.OrdinalIgnoreCase))
          {
            ScapLibraryItems.Add(item);
          }
          else if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
          {
            GpoLibraryItems.Add(item);
          }
          else if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase))
          {
            AdmxLibraryItems.Add(item);
          }
          else
          {
            OtherLibraryItems.Add(item);
          }
        }

        _filteredContentLibrary?.Refresh();
        ImportLibraryStatus = "STIG: " + StigLibraryItems.Count
          + " | SCAP: " + ScapLibraryItems.Count
          + " | GPO: " + GpoLibraryItems.Count
          + " | ADMX: " + AdmxLibraryItems.Count
          + " | Other: " + OtherLibraryItems.Count;
      });
    }

  [RelayCommand]
  private void OpenSelectedLibraryItem()
  {
    var selected = SelectedStigLibraryItem ?? SelectedScapLibraryItem ?? SelectedOtherLibraryItem;
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
    if (_formatCache.TryGetValue(pack.PackId, out var cached))
        return cached;

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
            string result;
            if (string.Equals(detectedValue, "Scap", StringComparison.OrdinalIgnoreCase)) result = "SCAP";
            else if (string.Equals(detectedValue, "Stig", StringComparison.OrdinalIgnoreCase)) result = "STIG";
            else if (string.Equals(detectedValue, "Gpo", StringComparison.OrdinalIgnoreCase))
            {
              var isAdmx = false;
              if (doc.RootElement.TryGetProperty("support", out var support)
                  && support.ValueKind == JsonValueKind.Object
                  && support.TryGetProperty("admx", out var admx)
                  && admx.ValueKind == JsonValueKind.True)
              {
                isAdmx = true;
              }

              result = isAdmx ? "ADMX" : "GPO";
            }
            else result = detectedValue.ToUpperInvariant();
            _formatCache[pack.PackId] = result;
            return result;
          }
        }
      }
      catch (Exception ex)
      {
          System.Diagnostics.Trace.TraceWarning("ResolvePackFormat failed for " + pack.PackId + ": " + ex.Message);
      }
    }

    var hint = (pack.SourceLabel + " " + pack.Name);
    string format;
    if (hint.IndexOf("scap", StringComparison.OrdinalIgnoreCase) >= 0) format = "SCAP";
    else if (hint.IndexOf("admx", StringComparison.OrdinalIgnoreCase) >= 0) format = "ADMX";
    else if (hint.IndexOf("gpo", StringComparison.OrdinalIgnoreCase) >= 0 || hint.IndexOf("lgpo", StringComparison.OrdinalIgnoreCase) >= 0) format = "GPO";
    else format = "STIG";
    _formatCache[pack.PackId] = format;
    return format;
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

      var stigPacks = packs
        .Where(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase))
        .ToList();

      if (stigPacks.Count == 0)
      {
        StatusText = "No STIG content selected. Select at least one STIG pack.";
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
      foreach (var pack in stigPacks)
      {
        built++;
        StatusText = stigPacks.Count == 1
          ? "Building bundle for " + pack.Name + "..."
          : "Building bundle " + built + " of " + stigPacks.Count + ": " + pack.Name + "...";

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
        var gatePath = Path.Combine(lastResult.BundleRoot, "Reports", "automation_gate.json");
        BuildGateStatus = File.Exists(gatePath) ? "Automation gate: " + gatePath : "Automation gate: (not found)";
        AutomationGatePath = File.Exists(gatePath) ? gatePath : string.Empty;
        BundleRoot = lastResult.BundleRoot;
      }

      StatusText = stigPacks.Count == 1
        ? "Build complete: " + stigPacks[0].Name
        : "Build complete: " + stigPacks.Count + " STIG bundles built.";
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
        _formatCache.Remove(pack.PackId);
        _benchmarkIdCache.Remove(pack.PackId);
        deleted++;
      }

      SelectedPack = ContentPacks.Count > 0 ? ContentPacks[0] : null;
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
    if (SelectedOtherLibraryItem != null)
      SelectedOtherLibraryItem = null;
  }

  partial void OnSelectedScapLibraryItemChanged(ImportedLibraryItem? value)
  {
    if (value == null) return;
    if (SelectedStigLibraryItem != null)
      SelectedStigLibraryItem = null;
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
     OnPropertyChanged(nameof(IsFilterAdmx));
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
       if (ContentLibraryFilter != "All" &&
           !string.Equals(item.Format, ContentLibraryFilter, StringComparison.OrdinalIgnoreCase))
         return false;
       if (!string.IsNullOrWhiteSpace(ContentSearchText) &&
           !item.Name.Contains(ContentSearchText, StringComparison.OrdinalIgnoreCase) &&
           !item.PackId.Contains(ContentSearchText, StringComparison.OrdinalIgnoreCase))
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

      if (!string.Equals(PackDetailFormat, "STIG", StringComparison.OrdinalIgnoreCase))
      {
        PackDetailControls = "Total: 0, Manual: 0 (STIG-only count)";
        return;
      }

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

  // ── Content Picker ─────────────────────────────────────────────────

  [RelayCommand]
  private async Task OpenContentPicker()
  {
    if (ContentPacks.Count == 0)
    {
      StatusText = "No content imported yet. Import STIG, SCAP, or GPO packages first.";
      return;
    }

    MachineInfo? machineInfo = null;
    try
    {
      machineInfo = await Task.Run(() => DetectMachineInfo(), _cts.Token);
    }
    catch
    {
      machineInfo = null;
    }

    var selectedStigIds = SelectedMissionPacks
      .Where(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase))
      .Select(p => p.PackId)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var allStigPacks = ContentPacks
      .Where(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase))
      .ToList();
    var allScapPacks = ContentPacks
      .Where(p => string.Equals(ResolvePackFormat(p), "SCAP", StringComparison.OrdinalIgnoreCase))
      .ToList();

    var benchmarkIdsByPackId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    foreach (var pack in allStigPacks.Concat(allScapPacks))
      benchmarkIdsByPackId[pack.PackId] = await GetPackBenchmarkIdsAsync(pack);

    string BuildPickerStatus(IReadOnlyCollection<string> selectedStigPackIds)
    {
      if (selectedStigPackIds.Count == 0)
        return "Select one or more STIGs to derive SCAP content.";
      if (allScapPacks.Count == 0)
        return "No SCAP packs imported.";

      var selectedBenchmarkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var selectedStigWithBenchmarkIds = 0;
      foreach (var stigId in selectedStigPackIds)
      {
        if (benchmarkIdsByPackId.TryGetValue(stigId, out var ids) && ids.Count > 0)
        {
          selectedStigWithBenchmarkIds++;
          selectedBenchmarkIds.UnionWith(ids);
        }
      }

      if (selectedBenchmarkIds.Count == 0)
      {
        return "Warning: No SCAP benchmark match found (selected STIGs are missing benchmark IDs).";
      }

      var matchCount = 0;
      var scapPacksWithBenchmarkIds = 0;
      foreach (var scapPack in allScapPacks)
      {
        if (benchmarkIdsByPackId.TryGetValue(scapPack.PackId, out var scapIds)
            && scapIds.Count > 0)
        {
          scapPacksWithBenchmarkIds++;
          if (scapIds.Overlaps(selectedBenchmarkIds))
            matchCount++;
        }
      }

      return matchCount == 0
        ? scapPacksWithBenchmarkIds == 0
          ? "Warning: No SCAP benchmark match found (imported SCAP packs are missing benchmark IDs)."
          : "Warning: No SCAP benchmark match found (selected STIG and imported SCAP benchmark IDs do not overlap)."
        : "Auto SCAP matches: " + matchCount
          + (selectedStigWithBenchmarkIds < selectedStigPackIds.Count
              ? " (some selected STIGs are missing benchmark IDs)"
              : string.Empty);
    }

    var initialDerived = await ResolveDerivedPackIdsAsync(selectedStigIds, machineInfo);
    var initialSelected = new HashSet<string>(selectedStigIds, StringComparer.OrdinalIgnoreCase);
    initialSelected.UnionWith(initialDerived);

    var items = ContentPacks.Select(pack =>
    {
      var format = ResolvePackFormat(pack);
      var isLocked = string.Equals(format, "SCAP", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase);

      var lockReason = string.Empty;
      if (string.Equals(format, "SCAP", StringComparison.OrdinalIgnoreCase))
        lockReason = "Auto-selected to match selected STIG content.";
      else if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
        lockReason = "Auto-selected based on host OS and selected STIG content.";
      else if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase))
        lockReason = "Auto-selected based on detected host software and selected STIG content.";

      return new ContentPickerItem
      {
        PackId = pack.PackId,
        Name = pack.Name,
        Format = format,
        SourceLabel = pack.SourceLabel,
        ImportedAtLabel = pack.ImportedAt.ToString("yyyy-MM-dd HH:mm"),
        IsSelected = initialSelected.Contains(pack.PackId),
        IsLocked = isLocked,
        LockReason = lockReason
      };
    }).ToList();

    var dialog = new ContentPickerDialog(items, ApplicablePackIds, BuildPickerStatus);
    dialog.Owner = System.Windows.Application.Current.MainWindow;
    if (dialog.ShowDialog() != true) return;

    var chosenStigIds = dialog.Items
      .Where(i => string.Equals(i.Format, "STIG", StringComparison.OrdinalIgnoreCase) && i.IsSelected)
      .Select(i => i.PackId)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (chosenStigIds.Count == 0)
    {
      System.Windows.MessageBox.Show(
        "Select at least one STIG. SCAP, GPO, and ADMX are automatically derived from STIG selection.",
        "STIG Selection Required",
        System.Windows.MessageBoxButton.OK,
        System.Windows.MessageBoxImage.Warning);
      StatusText = "Content selection not updated. No STIG selected.";
      return;
    }

    var derivedIds = await ResolveDerivedPackIdsAsync(chosenStigIds, machineInfo);
    var selectedIds = new HashSet<string>(chosenStigIds, StringComparer.OrdinalIgnoreCase);
    selectedIds.UnionWith(derivedIds);

    SelectedMissionPacks.Clear();
    foreach (var pack in ContentPacks)
    {
      if (selectedIds.Contains(pack.PackId))
        SelectedMissionPacks.Add(pack);
    }

    var applicableIds = ApplicablePackIds.Count > 0
      ? new HashSet<string>(ApplicablePackIds, StringComparer.OrdinalIgnoreCase)
      : machineInfo != null
        ? await ComputeApplicablePackIdsAsync(machineInfo)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (applicableIds.Count > 0)
    {
      var nonMatchingStigs = ContentPacks
        .Where(p => chosenStigIds.Contains(p.PackId) && !applicableIds.Contains(p.PackId))
        .Select(p => p.Name)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToList();

      if (nonMatchingStigs.Count > 0)
      {
        System.Windows.MessageBox.Show(
          "Selected STIG(s) may not apply to this system:\n\n- " + string.Join("\n- ", nonMatchingStigs),
          "Applicability Warning",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Warning);
      }
    }

    if (SelectedMissionPacks.Count == 0)
    {
      SelectedPack = null;
      SelectedContentSummary = "No content selected.";
    }
    else
    {
      var firstStig = SelectedMissionPacks
        .FirstOrDefault(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase));
      SelectedPack = firstStig ?? SelectedMissionPacks[0];

      var stigCount = SelectedMissionPacks.Count(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase));
      var scapCount = SelectedMissionPacks.Count(p => string.Equals(ResolvePackFormat(p), "SCAP", StringComparison.OrdinalIgnoreCase));
      var gpoCount = SelectedMissionPacks.Count(p => string.Equals(ResolvePackFormat(p), "GPO", StringComparison.OrdinalIgnoreCase));
      var admxCount = SelectedMissionPacks.Count(p => string.Equals(ResolvePackFormat(p), "ADMX", StringComparison.OrdinalIgnoreCase));

      SelectedContentSummary = "STIG: " + stigCount
        + " | Auto SCAP: " + scapCount
        + " | Auto GPO: " + gpoCount
        + " | Auto ADMX: " + admxCount;
    }

    var pickerStatus = BuildPickerStatus(chosenStigIds);
    StatusText = pickerStatus.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)
      ? "Content selection updated. " + pickerStatus
      : "Content selection updated. STIG is source-of-truth; SCAP/GPO/ADMX auto-derived.";
  }

  private async Task<HashSet<string>> ResolveDerivedPackIdsAsync(HashSet<string> selectedStigIds, MachineInfo? machineInfo)
  {
    var derived = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (selectedStigIds.Count == 0)
      return derived;

    var selectedStigPacks = ContentPacks
      .Where(p => selectedStigIds.Contains(p.PackId))
      .ToList();

    var selectedStigTags = selectedStigPacks
      .SelectMany(p => ExtractMatchingTags(p.Name + " " + p.SourceLabel))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var selectedStigBenchmarkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var stigPack in selectedStigPacks)
    {
      var ids = await GetPackBenchmarkIdsAsync(stigPack);
      selectedStigBenchmarkIds.UnionWith(ids);
    }

    var machineFeatureTags = machineInfo != null
      ? GetMachineFeatureTags(machineInfo)
      : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var pack in ContentPacks)
    {
      if (selectedStigIds.Contains(pack.PackId))
        continue;

      var format = ResolvePackFormat(pack);
      var packTags = ExtractMatchingTags(pack.Name + " " + pack.SourceLabel)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (string.Equals(format, "SCAP", StringComparison.OrdinalIgnoreCase))
      {
        var scapBenchmarkIds = await GetPackBenchmarkIdsAsync(pack);
        if (selectedStigBenchmarkIds.Count > 0
            && scapBenchmarkIds.Count > 0
            && scapBenchmarkIds.Overlaps(selectedStigBenchmarkIds))
          derived.Add(pack.PackId);
      }
      else if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase) && machineInfo != null)
      {
        if (!IsPackOsCompatible(packTags, machineInfo.OsTarget, requireOsTag: true))
          continue;

        var packFeatureTags = packTags.Where(IsFeatureTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packFeatureTags.Count == 0 || packFeatureTags.Overlaps(selectedStigTags))
          derived.Add(pack.PackId);
      }
      else if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase) && machineInfo != null)
      {
        if (!IsPackOsCompatible(packTags, machineInfo.OsTarget, requireOsTag: false))
          continue;

        var packFeatureTags = packTags.Where(IsFeatureTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packFeatureTags.Count > 0 && packFeatureTags.Overlaps(machineFeatureTags))
          derived.Add(pack.PackId);
      }
    }

    return derived;
  }

  private async Task<HashSet<string>> GetPackBenchmarkIdsAsync(ContentPack pack)
  {
    if (_benchmarkIdCache.TryGetValue(pack.PackId, out var cached))
      return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);

    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    try
    {
      var controls = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
      foreach (var control in controls)
      {
        var benchmarkId = NormalizeBenchmarkIdentifier(control.ExternalIds.BenchmarkId);
        if (!string.IsNullOrWhiteSpace(benchmarkId))
          ids.Add(benchmarkId);
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Failed to read benchmark ids for " + pack.PackId + ": " + ex.Message);
    }

    _benchmarkIdCache[pack.PackId] = ids;
    return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
  }

  private static string NormalizeBenchmarkIdentifier(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return string.Empty;

    var source = value.Trim().ToLowerInvariant();
    var sb = new System.Text.StringBuilder(source.Length);
    foreach (var ch in source)
    {
      if (char.IsLetterOrDigit(ch))
        sb.Append(ch);
    }

    return sb.ToString();
  }

  private static bool IsFeatureTag(string tag)
  {
    return tag == "defender"
      || tag == "firewall"
      || tag == "edge"
      || tag == "dotnet"
      || tag == "iis"
      || tag == "sql"
      || tag == "dns"
      || tag == "dhcp"
      || tag == "chrome"
      || tag == "firefox"
      || tag == "adobe"
      || tag == "office";
  }

  private static bool IsOsTag(string tag)
  {
    return tag == "win11" || tag == "win10" || tag == "server2022" || tag == "server2019";
  }

  private static string GetOsTag(OsTarget target)
  {
    return target switch
    {
      OsTarget.Win11 => "win11",
      OsTarget.Win10 => "win10",
      OsTarget.Server2022 => "server2022",
      OsTarget.Server2019 => "server2019",
      _ => string.Empty
    };
  }

  private static bool IsPackOsCompatible(HashSet<string> packTags, OsTarget machineOs, bool requireOsTag)
  {
    var osTags = packTags.Where(IsOsTag).ToList();
    if (osTags.Count == 0)
      return !requireOsTag;

    var expectedTag = GetOsTag(machineOs);
    if (string.IsNullOrWhiteSpace(expectedTag))
      return false;

    return osTags.Contains(expectedTag, StringComparer.OrdinalIgnoreCase);
  }

  private static HashSet<string> GetMachineFeatureTags(MachineInfo info)
  {
    var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      "defender",
      "firewall",
      "edge",
      "dotnet"
    };

    foreach (var feature in info.InstalledFeatures)
    {
      if (feature.IndexOf("IIS", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("iis");
      if (feature.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("sql");
      if (feature.IndexOf("DNS", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dns");
      if (feature.IndexOf("DHCP", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dhcp");
      if (feature.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("dotnet");
      if (feature.IndexOf("Chrome", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("chrome");
      if (feature.IndexOf("Firefox", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("firefox");
      if (feature.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("adobe");
      if (feature.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("office");
    }

    return tags;
  }

  private async Task<HashSet<string>> ComputeApplicablePackIdsAsync(MachineInfo info)
  {
    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pack in ContentPacks)
    {
      if (await IsPackApplicableAsync(pack, info))
        ids.Add(pack.PackId);
    }

    return ids;
  }

  private static IEnumerable<string> ExtractMatchingTags(string text)
  {
    var normalized = (text ?? string.Empty).ToLowerInvariant();

    if (normalized.Contains("windows 11", StringComparison.Ordinal) || normalized.Contains("win11", StringComparison.Ordinal))
      yield return "win11";
    if (normalized.Contains("windows 10", StringComparison.Ordinal) || normalized.Contains("win10", StringComparison.Ordinal))
      yield return "win10";
    if (normalized.Contains("server 2022", StringComparison.Ordinal))
      yield return "server2022";
    if (normalized.Contains("server 2019", StringComparison.Ordinal))
      yield return "server2019";
    if (normalized.Contains("defender", StringComparison.Ordinal))
      yield return "defender";
    if (normalized.Contains("firewall", StringComparison.Ordinal))
      yield return "firewall";
    if (normalized.Contains("edge", StringComparison.Ordinal))
      yield return "edge";
    if (normalized.Contains(".net", StringComparison.Ordinal) || normalized.Contains("dotnet", StringComparison.Ordinal))
      yield return "dotnet";
    if (normalized.Contains("iis", StringComparison.Ordinal))
      yield return "iis";
    if (normalized.Contains("sql", StringComparison.Ordinal))
      yield return "sql";
    if (normalized.Contains("dns", StringComparison.Ordinal))
      yield return "dns";
    if (normalized.Contains("dhcp", StringComparison.Ordinal))
      yield return "dhcp";
    if (normalized.Contains("chrome", StringComparison.Ordinal))
      yield return "chrome";
    if (normalized.Contains("firefox", StringComparison.Ordinal))
      yield return "firefox";
    if (normalized.Contains("adobe", StringComparison.Ordinal))
      yield return "adobe";
    if (normalized.Contains("office", StringComparison.Ordinal))
      yield return "office";
  }

  [RelayCommand]
  private async Task ScanRemoteMachineApplicabilityAsync()
  {
    if (_fleetService == null)
    {
      AdRemoteScanStatus = "Fleet service unavailable.";
      return;
    }

    if (IsBusy) return;
    if (string.IsNullOrWhiteSpace(AdRemoteTargets))
    {
      AdRemoteScanStatus = "Enter remote hostnames (comma or newline separated).";
      return;
    }

    try
    {
      IsBusy = true;
      AdRemoteScanStatus = "Scanning remote hosts...";

      var normalizedTargets = AdRemoteTargets.Replace("\r", "\n").Replace("\n", ",");
      var targets = ParseFleetTargets(normalizedTargets);
      if (targets.Count == 0)
      {
        AdRemoteScanStatus = "No valid targets parsed.";
        return;
      }

      var status = await _fleetService.CheckStatusAsync(targets, CancellationToken.None);
      var lines = new List<string>
      {
        "Total: " + status.TotalMachines,
        "Reachable: " + status.ReachableCount,
        "Unreachable: " + status.UnreachableCount,
        "",
        "Hosts:"
      };

      foreach (var machine in status.MachineStatuses.OrderBy(m => m.MachineName, StringComparer.OrdinalIgnoreCase))
      {
        lines.Add("  - " + machine.MachineName + " : " + (machine.IsReachable ? "Reachable" : "Unreachable"));
      }

      AdRemoteScanStatus = string.Join("\n", lines);
      StatusText = "Remote scan complete.";
    }
    catch (Exception ex)
    {
      AdRemoteScanStatus = "Remote scan failed: " + ex.Message;
      StatusText = "Remote scan failed.";
    }
    finally
    {
      IsBusy = false;
    }
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

      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);

      var lines = new List<string>
      {
        "Machine: " + info.Hostname,
        "OS: " + info.ProductName + " (Build " + info.BuildNumber + ")",
        "Role: " + info.Role,
        "Detected target: " + info.OsTarget
      };

      if (info.IsServer && info.InstalledFeatures.Count > 0)
      {
        lines.Add("Server features: " + string.Join(", ", info.InstalledFeatures.Take(10)));
        if (info.InstalledFeatures.Count > 10)
          lines.Add("  ... and " + (info.InstalledFeatures.Count - 10) + " more");
      }

      ApplicablePackIds.Clear();
      if (ContentPacks.Count > 0)
      {
        var applicable = new List<string>();
        foreach (var pack in ContentPacks)
        {
          if (await IsPackApplicableAsync(pack, info))
          {
            applicable.Add(pack.Name);
            ApplicablePackIds.Add(pack.PackId);
          }
        }

        if (applicable.Count > 0)
        {
          lines.Add("");
          lines.Add("Applicable imported packs (" + applicable.Count + "):");
          foreach (var name in applicable)
            lines.Add("  - " + name);
        }
        else
        {
          lines.Add("");
          lines.Add("No imported packs match this machine. Import the applicable STIG content above.");
        }
      }
      else
      {
        lines.Add("");
        lines.Add("No content packs imported yet. Import STIGs above, then re-scan to see which apply.");
      }

      var recommendations = GetStigRecommendations(info);
      if (recommendations.Count > 0)
      {
        lines.Add("");
        lines.Add("Recommended STIGs for this machine:");
        foreach (var rec in recommendations)
          lines.Add("  - " + rec);
      }

      MachineApplicabilityStatus = string.Join("\n", lines);
      StatusText = "Machine scan complete: " + info.OsTarget + " / " + info.Role;
    }
    catch (Exception ex)
    {
      MachineApplicabilityStatus = "Scan failed: " + ex.Message;
      StatusText = "Machine scan failed.";
    }
    finally
    {
      IsBusy = false;
    }
  }

  private static MachineInfo DetectMachineInfo()
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
    catch { }

    // Detect Mozilla Firefox
    try
    {
      using var ffKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
      if (ffKey != null)
        info.InstalledFeatures.Add("Mozilla Firefox");
    }
    catch { }

    // Detect Adobe Acrobat / Reader
    try
    {
      using var adobeKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Acrobat Reader");
      using var acrobatKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Adobe\Adobe Acrobat");
      if (adobeKey != null || acrobatKey != null)
        info.InstalledFeatures.Add("Adobe Acrobat");
    }
    catch { }

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
    catch { }

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
      if (feature.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Adobe", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (feature.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0
          && name.IndexOf("Office", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    var format = ResolvePackFormat(pack);
    if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
    {
      if (name.IndexOf("Baseline", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
      if (name.IndexOf("LGPO", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
    }

    if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase))
    {
      if (name.IndexOf("ADMX", StringComparison.OrdinalIgnoreCase) >= 0)
        return true;
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
    recs.Add("[SCAP Benchmarks]  (auto-selected with matching STIGs)");
    if (osLabel != null)
      recs.Add("  " + osLabel + roleTag + " SCAP Benchmark");
    recs.Add("  Defender / Firewall / Edge / .NET benchmarks (if imported)");

    recs.Add("");
    recs.Add("[GPO / ADMX]  (auto-selected with matching STIGs)");
    if (osLabel != null)
      recs.Add("  " + osLabel + " Security Baseline GPO");
    recs.Add("  Defender / Edge / Firewall ADMX templates (if imported)");

    return recs.Distinct().ToList();
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
