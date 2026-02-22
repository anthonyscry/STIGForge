using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using STIGForge.Build;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Content.Import;
using STIGForge.Infrastructure.Hashing;
using STIGForge.App.Views;

namespace STIGForge.App;

public partial class MainViewModel
{
  public List<ContentPack> SelectedMissionPacks { get; } = new();
  private readonly Dictionary<string, string> _formatCache = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, HashSet<string>> _benchmarkIdCache = new(StringComparer.OrdinalIgnoreCase);
  private readonly CanonicalScapSelector _canonicalScapSelector = new();
  private readonly Dictionary<string, string> _canonicalScapByStigId = new(StringComparer.OrdinalIgnoreCase);

  [RelayCommand]
  private async Task ScanImportFolderAsync()
  {
    if (IsBusy) return;

    var startedAt = DateTimeOffset.Now;
    var importFolder = ResolveScanImportFolderPath();

    try
    {
      IsBusy = true;
      ProgressValue = 0;
      ProgressMax = 1;
      IsProgressIndeterminate = true;
      ScanImportFolderPath = importFolder;
      var createdFolder = false;
      if (!Directory.Exists(importFolder))
      {
        Directory.CreateDirectory(importFolder);
        createdFolder = true;
      }

      StatusText = createdFolder
        ? "Import folder created. Scanning import folder..."
        : "Scanning import folder...";

      var scanner = new ImportInboxScanner(new Sha256HashingService());
      var scan = await Task.Run(() => scanner.ScanAsync(importFolder, _cts.Token), _cts.Token);

      if (scan.Candidates.Count == 0)
      {
        var emptySummary = new ImportScanSummary
        {
          ImportFolder = importFolder,
          StartedAt = startedAt,
          FinishedAt = DateTimeOffset.Now,
          ArchiveCount = 0,
          CandidateCount = 0,
          WinnerCount = 0,
          SuppressedCount = 0,
          ImportedPackCount = 0,
          ImportedToolCount = 0,
          Warnings = scan.Warnings,
          Failures = Array.Empty<string>()
        };
        PersistImportScanSummary(emptySummary);
        StatusText = "No importable files found in " + importFolder + ".";
        return;
      }

      var dedup = new ImportDedupService();
      var deduped = dedup.Resolve(scan.Candidates);

      var contentWinners = deduped.Winners
        .Where(c => c.ArtifactKind != ImportArtifactKind.Tool && c.ArtifactKind != ImportArtifactKind.Unknown)
        .ToList();

      var toolWinners = deduped.Winners
        .Where(c => c.ArtifactKind == ImportArtifactKind.Tool)
        .ToList();

      var suppressedWarnings = deduped.Suppressed
        .Select(c => "Suppressed duplicate: " + c.FileName + " [" + c.ArtifactKind + "] " + c.ContentKey)
        .ToList();
      var dedupDecisionNotes = deduped.Decisions.ToList();
      foreach (var decision in dedupDecisionNotes)
        System.Diagnostics.Trace.TraceInformation(decision);

      var contentImportQueue = ImportQueuePlanner.BuildContentImportPlan(contentWinners)
        .ToList();

      var plannedByZip = contentImportQueue
        .GroupBy(p => p.ZipPath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
          g => g.Key,
          g => g
            .Select(p => p.ArtifactKind + " (" + p.Route + ")")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList(),
          StringComparer.OrdinalIgnoreCase);

      var multiRouteWarnings = contentWinners
        .GroupBy(c => c.ZipPath, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g =>
        {
          var labels = string.Join(", ", g
            .Select(c => c.ArtifactKind.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

          var planned = plannedByZip.TryGetValue(g.Key, out var routes)
            ? string.Join(", ", routes)
            : "(none)";

          return "Multiple content signatures detected in " + Path.GetFileName(g.Key)
            + "; planned routes: " + planned + ". Detected: " + labels + ".";
        })
        .ToList();

      var importedPackCount = 0;
      var importedToolCount = 0;
      var failures = new List<string>();
      var importedByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      var importedPacks = new List<ContentPack>();
      var stagedOutcomes = new List<StagedOperationOutcome>();

      var totalWorkItems = contentImportQueue.Count + (toolWinners.Count > 0 ? 1 : 0);
      ProgressMax = Math.Max(1, totalWorkItems);
      ProgressValue = 0;
      IsProgressIndeterminate = false;

      for (var i = 0; i < contentImportQueue.Count; i++)
      {
        var planned = contentImportQueue[i];
        StatusText = "Importing " + (i + 1) + " of " + contentImportQueue.Count + ": " + planned.FileName + "...";

        try
        {
          var imported = await Task.Run(
            () => _importer.ExecutePlannedImportAsync(planned, _cts.Token),
            _cts.Token);

          importedPackCount += imported.Count;
          IncrementImportedType(importedByType, planned.ArtifactKind.ToString(), imported.Count);
          importedPacks.AddRange(imported);
        }
        catch (Exception ex)
        {
          failures.Add(planned.FileName + " (" + planned.Route + "): " + ex.Message);
        }

        // Capture staged outcome row regardless of success or failure.
        // planned.State and planned.FailureReason are updated by ExecutePlannedImportAsync.
        stagedOutcomes.Add(new StagedOperationOutcome
        {
          FileName = planned.FileName,
          ArtifactKind = planned.ArtifactKind.ToString(),
          Route = planned.Route.ToString(),
          SourceLabel = planned.SourceLabel,
          State = planned.State.ToString(),
          FailureReason = planned.FailureReason,
          CommittedPackCount = planned.State == ImportOperationState.Committed
            ? importedPacks.Count(p => string.Equals(
                Path.GetDirectoryName(planned.ZipPath),
                Path.GetDirectoryName(p.SourceLabel),
                StringComparison.OrdinalIgnoreCase))
            : 0
        });

        ProgressValue = i + 1;
      }

      if (toolWinners.Count > 0)
      {
        StatusText = "Importing tool bundles...";

        try
        {
          importedToolCount = ImportToolBundles(toolWinners);
          IncrementImportedType(importedByType, "Tool", importedToolCount);
        }
        catch (Exception ex)
        {
          failures.Add("Tool import failed: " + ex.Message);
        }

        ProgressValue = contentImportQueue.Count + 1;
      }

      AddImportedPacks(importedPacks);

      RefreshImportLibrary();
      var warningCount = scan.Warnings.Count + suppressedWarnings.Count + dedupDecisionNotes.Count + multiRouteWarnings.Count;

      var summary = new ImportScanSummary
      {
        ImportFolder = importFolder,
        StartedAt = startedAt,
        FinishedAt = DateTimeOffset.Now,
        ArchiveCount = scan.Candidates
          .Select(c => c.ZipPath)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .Count(),
        CandidateCount = scan.Candidates.Count,
        WinnerCount = deduped.Winners.Count,
        SuppressedCount = deduped.Suppressed.Count,
        ImportedPackCount = importedPackCount,
        ImportedToolCount = importedToolCount,
        ImportedByType = importedByType,
        Warnings = scan.Warnings.Concat(suppressedWarnings).Concat(dedupDecisionNotes).Concat(multiRouteWarnings).ToList(),
        Failures = failures,
        StagedOutcomes = stagedOutcomes,
        StagedCommittedCount = stagedOutcomes.Count(o => string.Equals(o.State, ImportOperationState.Committed.ToString(), StringComparison.Ordinal)),
        StagedFailedCount = stagedOutcomes.Count(o => string.Equals(o.State, ImportOperationState.Failed.ToString(), StringComparison.Ordinal))
      };
      PersistImportScanSummary(summary);

      StatusText = "Scan complete: imported packs=" + importedPackCount
        + ", tools=" + importedToolCount
        + ", warnings=" + warningCount
        + (stagedOutcomes.Count > 0 ? ", staged=" + summary.StagedCommittedCount + "/" + stagedOutcomes.Count + " committed" : "")
        + (failures.Count > 0 ? ", failures=" + failures.Count : "");
    }
    catch (Exception ex)
    {
      StatusText = "Scan import folder failed: " + ex.Message;
      PersistImportScanSummary(new ImportScanSummary
      {
        ImportFolder = importFolder,
        StartedAt = startedAt,
        FinishedAt = DateTimeOffset.Now,
        ArchiveCount = 0,
        CandidateCount = 0,
        WinnerCount = 0,
        SuppressedCount = 0,
        ImportedPackCount = 0,
        ImportedToolCount = 0,
        Warnings = Array.Empty<string>(),
        Failures = new[] { ex.Message }
      });
    }
    finally
    {
      ProgressValue = 0;
      ProgressMax = 1;
      IsProgressIndeterminate = true;
      IsBusy = false;
    }
  }

  private Task<IReadOnlyList<ContentPack>> ImportPlannedContentAsync(PlannedContentImport planned, CancellationToken ct)
  {
    return planned.Route == ContentImportRoute.AdmxTemplatesFromZip
      ? _importer.ImportAdmxTemplatesFromZipAsync(planned.ZipPath, planned.SourceLabel, ct)
      : _importer.ImportConsolidatedZipAsync(planned.ZipPath, planned.SourceLabel, ct);
  }

  private string ResolveScanImportFolderPath()
  {
    return _paths.GetImportRoot();
  }

  [RelayCommand]
  private void OpenImportFolder()
  {
    var folder = ResolveScanImportFolderPath();
    ScanImportFolderPath = folder;
    Directory.CreateDirectory(folder);
    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = folder,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      StatusText = "Failed to open import folder: " + ex.Message;
    }
  }

  private static void IncrementImportedType(IDictionary<string, int> counts, string type, int amount)
  {
    if (amount <= 0)
      return;

    if (!counts.TryGetValue(type, out var existing))
      counts[type] = amount;
    else
      counts[type] = existing + amount;
  }

  private void PersistImportScanSummary(ImportScanSummary summary)
  {
    var logsRoot = _paths.GetLogsRoot();
    Directory.CreateDirectory(logsRoot);
    var stamp = summary.FinishedAt.ToString("yyyyMMdd_HHmmss_fff");
    var jsonPath = BuildUniqueSummaryPath(logsRoot, stamp, ".json");
    var textPath = BuildUniqueSummaryPath(logsRoot, stamp, ".txt");

    var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
    {
      WriteIndented = true
    });
    File.WriteAllText(jsonPath, json);

    var lines = new List<string>
    {
      "Import scan summary",
      "Started: " + summary.StartedAt.ToString("o"),
      "Finished: " + summary.FinishedAt.ToString("o"),
      "Folder: " + summary.ImportFolder,
      "Archives: " + summary.ArchiveCount,
      "Candidates: " + summary.CandidateCount,
      "Winners: " + summary.WinnerCount,
      "Suppressed: " + summary.SuppressedCount,
      "Imported packs: " + summary.ImportedPackCount,
      "Imported tools: " + summary.ImportedToolCount,
      ""
    };

    if (summary.ImportedByType.Count > 0)
    {
      lines.Add("Imported by type:");
      foreach (var entry in summary.ImportedByType.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        lines.Add("- " + entry.Key + ": " + entry.Value);
      lines.Add(string.Empty);
    }

    if (summary.Warnings.Count > 0)
    {
      lines.Add("Warnings:");
      foreach (var warning in summary.Warnings)
        lines.Add("- " + warning);
      lines.Add(string.Empty);
    }

    if (summary.StagedOutcomes.Count > 0)
    {
      lines.Add("Staged transitions: " + summary.StagedCommittedCount + " committed, " + summary.StagedFailedCount + " failed");
      lines.Add("Operations:");
      foreach (var outcome in summary.StagedOutcomes)
      {
        var row = "- [" + outcome.State + "] " + outcome.FileName + " (" + outcome.ArtifactKind + "/" + outcome.Route + ")";
        if (!string.IsNullOrWhiteSpace(outcome.FailureReason))
          row += " -- " + outcome.FailureReason;
        lines.Add(row);
      }
      lines.Add(string.Empty);
    }

    if (summary.Failures.Count > 0)
    {
      lines.Add("Failures:");
      foreach (var failure in summary.Failures)
        lines.Add("- " + failure);
    }

    File.WriteAllLines(textPath, lines);
    LastOutputPath = jsonPath;
  }

  private static string BuildUniqueSummaryPath(string logsRoot, string stamp, string extension)
  {
    var suffix = string.Empty;
    var attempt = 0;
    while (true)
    {
      var path = Path.Combine(logsRoot, "import_scan_" + stamp + suffix + extension);
      if (!File.Exists(path))
        return path;

      attempt++;
      suffix = "_" + attempt.ToString("D2");
    }
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

  private void AddImportedPacks(IReadOnlyList<ContentPack> packs)
  {
    if (packs.Count == 0)
      return;

    foreach (var pack in packs)
    {
      ContentPacks.Insert(0, pack);
      _benchmarkIdCache.Remove(pack.PackId);
    }

    SelectedPack = packs[^1];
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
              var hasAdmxSupport = false;
              if (doc.RootElement.TryGetProperty("support", out var support)
                  && support.ValueKind == JsonValueKind.Object
                  && support.TryGetProperty("admx", out var admx)
                  && admx.ValueKind == JsonValueKind.True)
              {
                hasAdmxSupport = true;
              }

              var isAdmxTemplate = hasAdmxSupport && IsAdmxTemplatePack(pack);
              result = isAdmxTemplate ? "ADMX" : "GPO";
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

  private static bool IsAdmxTemplatePack(ContentPack pack)
  {
    return PackFormatClassifier.IsAdmxTemplatePack(pack.SourceLabel, pack.Name);
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

   partial void OnAdRemoteFilterTextChanged(string value)
   {
     _remoteDiscoveredHostsView?.Refresh();
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

   private ICollectionView CreateAdRemoteDiscoveredHostsView()
   {
     var view = CollectionViewSource.GetDefaultView(AdRemoteDiscoveredHosts);
     view.Filter = obj =>
     {
       if (obj is not RemoteHostScanItem item)
         return false;

       if (string.IsNullOrWhiteSpace(AdRemoteFilterText))
         return true;

       return item.HostName.Contains(AdRemoteFilterText, StringComparison.OrdinalIgnoreCase)
         || item.WinRmStatus.Contains(AdRemoteFilterText, StringComparison.OrdinalIgnoreCase)
         || item.ScanStatus.Contains(AdRemoteFilterText, StringComparison.OrdinalIgnoreCase);
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
      StatusText = "No content imported yet. Add ZIP files to the import folder and run Scan Import Folder first.";
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
        return "Select one or more STIGs to derive SCAP/GPO/ADMX (machine-scan aware).";
      if (allScapPacks.Count == 0)
        return "No SCAP packs imported.";

      var selectedStigTags = allStigPacks
        .Where(p => selectedStigPackIds.Contains(p.PackId))
        .SelectMany(p => ExtractMatchingTags(p.Name + " " + p.SourceLabel))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      var selectedBenchmarkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var selectedStigNamesMissingIds = new List<string>();
      var selectedStigWithBenchmarkIds = 0;
      foreach (var stigId in selectedStigPackIds)
      {
        if (benchmarkIdsByPackId.TryGetValue(stigId, out var ids) && ids.Count > 0)
        {
          selectedStigWithBenchmarkIds++;
          selectedBenchmarkIds.UnionWith(ids);
        }
        else
        {
          var stigName = allStigPacks
            .FirstOrDefault(p => string.Equals(p.PackId, stigId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
          if (!string.IsNullOrWhiteSpace(stigName))
            selectedStigNamesMissingIds.Add(stigName!);
        }
      }

      if (selectedBenchmarkIds.Count == 0)
      {
        var fallbackTagMatches = allScapPacks.Count(s =>
        {
          var normalized = (s.Name + " " + s.SourceLabel);
          if (normalized.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

          var tags = ExtractMatchingTags(normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
          return tags.Count > 0 && tags.Overlaps(selectedStigTags);
        });

        return fallbackTagMatches > 0
          ? "Auto SCAP fallback matches: " + fallbackTagMatches + " (tag-based)"
          : "Warning: No SCAP benchmark or fallback tag match found. STIGs missing benchmark IDs: "
            + string.Join(", ", selectedStigNamesMissingIds.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
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
          : "Warning: No SCAP benchmark match found for selected STIGs: "
            + string.Join(", ", allStigPacks
              .Where(p => selectedStigPackIds.Contains(p.PackId))
              .Select(p => p.Name)
              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        : "Auto SCAP matches: " + matchCount
          + (selectedStigWithBenchmarkIds < selectedStigPackIds.Count
              ? " (STIGs missing benchmark IDs: "
                + string.Join(", ", selectedStigNamesMissingIds.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                + ")"
              : string.Empty);
    }

    IReadOnlyList<string> BuildPickerWarningLines(IReadOnlyCollection<string> selectedStigPackIds)
    {
      var status = BuildPickerStatus(selectedStigPackIds);
      if (!status.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
        return Array.Empty<string>();

      return new[] { status };
    }

    ImportSelectionPlan BuildImportSelectionPlan(IReadOnlyCollection<string> selectedStigPackIds, IReadOnlyCollection<string> autoDerivedPackIds)
    {
      var selectedStigSet = selectedStigPackIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
      var autoDerivedSet = autoDerivedPackIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
      var candidates = new List<ImportSelectionCandidate>();

      foreach (var pack in ContentPacks)
      {
        var artifactType = ResolveImportSelectionArtifactType(pack);
        if (artifactType == null)
          continue;

        var include = artifactType == ImportSelectionArtifactType.Stig
          ? selectedStigSet.Contains(pack.PackId)
          : autoDerivedSet.Contains(pack.PackId);

        if (!include)
          continue;

        candidates.Add(new ImportSelectionCandidate
        {
          ArtifactType = artifactType.Value,
          Id = pack.PackId,
          IsSelected = artifactType == ImportSelectionArtifactType.Stig && selectedStigSet.Contains(pack.PackId)
        });
      }

      return _importSelectionOrchestrator.BuildPlan(candidates);
    }

    var initialDerived = await ResolveDerivedPackIdsAsync(selectedStigIds, machineInfo);
    var initialPlan = BuildImportSelectionPlan(selectedStigIds, initialDerived);
    var initialSelected = initialPlan.Rows
      .Where(x => x.IsSelected)
      .Select(x => x.Id)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

    var dialog = new ContentPickerDialog(items, ApplicablePackIds, BuildPickerStatus, BuildPickerWarningLines);
    dialog.Owner = System.Windows.Application.Current.MainWindow;
    if (dialog.ShowDialog() != true) return;

    var chosenStigIds = dialog.Items
      .Where(i => string.Equals(i.Format, "STIG", StringComparison.OrdinalIgnoreCase) && i.IsSelected)
      .Select(i => i.PackId)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (chosenStigIds.Count == 0)
    {
        System.Windows.MessageBox.Show(
          "Select at least one STIG. SCAP, GPO/LGPO, and ADMX are automatically derived from STIG selection and machine scan signals.",
          "STIG Selection Required",
          System.Windows.MessageBoxButton.OK,
          System.Windows.MessageBoxImage.Warning);
      StatusText = "Content selection not updated. No STIG selected.";
      return;
    }

    var derivedIds = await ResolveDerivedPackIdsAsync(chosenStigIds, machineInfo);
    var selectionPlan = BuildImportSelectionPlan(chosenStigIds, derivedIds);
    var selectedIds = selectionPlan.Rows
      .Where(x => x.IsSelected)
      .Select(x => x.Id)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    SelectedMissionPacks.Clear();
    var packsById = ContentPacks.ToDictionary(p => p.PackId, StringComparer.OrdinalIgnoreCase);
    foreach (var row in selectionPlan.Rows.Where(x => x.IsSelected))
    {
      if (packsById.TryGetValue(row.Id, out var pack))
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
      SelectedContentSummary = selectionPlan.StatusSummaryText;
    }

    StatusText = selectionPlan.Warnings.Count > 0
      ? "Content selection updated with warnings. " + selectionPlan.StatusSummaryText
      : "Content selection updated. " + selectionPlan.StatusSummaryText;
  }

  private ImportSelectionArtifactType? ResolveImportSelectionArtifactType(ContentPack pack)
  {
    var format = ResolvePackFormat(pack);
    if (string.Equals(format, "STIG", StringComparison.OrdinalIgnoreCase))
      return ImportSelectionArtifactType.Stig;
    if (string.Equals(format, "SCAP", StringComparison.OrdinalIgnoreCase))
      return ImportSelectionArtifactType.Scap;
    if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase))
      return ImportSelectionArtifactType.Gpo;
    if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase))
      return ImportSelectionArtifactType.Admx;

    return null;
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

    var machineFeatureTags = machineInfo != null
      ? GetMachineFeatureTags(machineInfo)
      : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var allScapPacks = ContentPacks
      .Where(p => !selectedStigIds.Contains(p.PackId)
        && string.Equals(ResolvePackFormat(p), "SCAP", StringComparison.OrdinalIgnoreCase))
      .ToList();

    foreach (var stigPack in selectedStigPacks)
    {
      var stigTags = ExtractMatchingTags(stigPack.Name + " " + stigPack.SourceLabel)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      var candidateScapPacks = await GetScapCandidatesForStigAsync(
        stigPack,
        allScapPacks,
        stigTags,
        machineInfo);

      var selection = await SelectCanonicalScapAsync(stigPack, candidateScapPacks);
      if (selection.Winner != null)
      {
        derived.Add(selection.Winner.PackId);
        _canonicalScapByStigId[stigPack.PackId] = selection.Winner.PackId;
      }
      else
      {
        _canonicalScapByStigId.Remove(stigPack.PackId);
      }
    }

    foreach (var pack in ContentPacks)
    {
      if (selectedStigIds.Contains(pack.PackId))
        continue;

      var format = ResolvePackFormat(pack);
      var packTags = ExtractMatchingTags(pack.Name + " " + pack.SourceLabel)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      if (string.Equals(format, "GPO", StringComparison.OrdinalIgnoreCase) && machineInfo != null)
      {
        if (!IsPackOsCompatible(packTags, machineInfo.OsTarget, requireOsTag: true))
          continue;

        var packFeatureTags = packTags.Where(IsFeatureTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packFeatureTags.Count == 0
            || packFeatureTags.Overlaps(selectedStigTags)
            || packFeatureTags.Overlaps(machineFeatureTags))
          derived.Add(pack.PackId);
      }
      else if (string.Equals(format, "ADMX", StringComparison.OrdinalIgnoreCase) && machineInfo != null)
      {
        if (!IsPackOsCompatible(packTags, machineInfo.OsTarget, requireOsTag: false))
          continue;

        var packFeatureTags = packTags.Where(IsFeatureTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (packFeatureTags.Count > 0 && packFeatureTags.Overlaps(machineFeatureTags))
        {
          derived.Add(pack.PackId);
          System.Diagnostics.Trace.TraceInformation("ADMX auto-select: " + pack.Name + " (feature match)");
        }
        else if (packFeatureTags.Count == 0
          && (pack.Name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0
              || pack.Name.IndexOf("Baseline", StringComparison.OrdinalIgnoreCase) >= 0))
        {
          derived.Add(pack.PackId);
          System.Diagnostics.Trace.TraceInformation("ADMX auto-select: " + pack.Name + " (OS baseline match)");
        }
        else
        {
          System.Diagnostics.Trace.TraceInformation("ADMX skipped (feature mismatch): " + pack.Name);
        }
      }
    }

    return derived;
  }

  private async Task<List<ContentPack>> GetScapCandidatesForStigAsync(
    ContentPack stigPack,
    IReadOnlyList<ContentPack> allScapPacks,
    HashSet<string> stigTags,
    MachineInfo? machineInfo)
  {
    var candidates = new List<ContentPack>();
    var stigBenchmarkIds = await GetPackBenchmarkIdsAsync(stigPack);

    foreach (var pack in allScapPacks)
    {
      var packTags = ExtractMatchingTags(pack.Name + " " + pack.SourceLabel)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      var containsAndroid = pack.Name.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0
        || pack.SourceLabel.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0;
      if (containsAndroid)
      {
        System.Diagnostics.Trace.TraceInformation("SCAP skipped (android guard): " + pack.Name);
        continue;
      }

      var scapBenchmarkIds = await GetPackBenchmarkIdsAsync(pack);
      var benchmarkMatch = stigBenchmarkIds.Count > 0
        && scapBenchmarkIds.Count > 0
        && scapBenchmarkIds.Overlaps(stigBenchmarkIds);
      if (benchmarkMatch)
      {
        candidates.Add(pack);
        System.Diagnostics.Trace.TraceInformation("SCAP candidate: " + pack.Name + " (benchmark overlap)");
        continue;
      }

      if (machineInfo != null && !IsPackOsCompatible(packTags, machineInfo.OsTarget, requireOsTag: false))
      {
        System.Diagnostics.Trace.TraceInformation("SCAP skipped (OS mismatch): " + pack.Name);
        continue;
      }

      if (PackApplicabilityRules.IsScapFallbackTagCompatible(stigTags, packTags))
      {
        candidates.Add(pack);
        System.Diagnostics.Trace.TraceInformation("SCAP candidate: " + pack.Name + " (fallback tag match)");
      }
      else
      {
        System.Diagnostics.Trace.TraceInformation("SCAP skipped (no mapping match): " + pack.Name);
      }
    }

    return candidates;
  }

  private async Task<CanonicalScapSelectionResult> SelectCanonicalScapAsync(ContentPack stigPack, IReadOnlyList<ContentPack> matchingScap)
  {
    var stigBenchmarkIds = await GetPackBenchmarkIdsAsync(stigPack);
    var candidates = new List<CanonicalScapCandidate>();
    foreach (var scapPack in matchingScap)
    {
      var benchmarkIds = await GetPackBenchmarkIdsAsync(scapPack);
      candidates.Add(new CanonicalScapCandidate
      {
        PackId = scapPack.PackId,
        Name = scapPack.Name,
        SourceLabel = scapPack.SourceLabel,
        ImportedAt = scapPack.ImportedAt,
        ReleaseDate = scapPack.ReleaseDate,
        BenchmarkIds = benchmarkIds.ToArray()
      });
    }

    return _canonicalScapSelector.Select(new CanonicalScapSelectionInput
    {
      StigPackId = stigPack.PackId,
      StigName = stigPack.Name,
      StigImportedAt = stigPack.ImportedAt,
      StigBenchmarkIds = stigBenchmarkIds.ToArray(),
      Candidates = candidates
    });
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
    => PackApplicabilityRules.IsFeatureTag(tag);

  private static bool IsOsTag(string tag)
    => PackApplicabilityRules.IsOsTag(tag);

  private static bool IsPackOsCompatible(HashSet<string> packTags, OsTarget machineOs, bool requireOsTag)
    => PackApplicabilityRules.IsPackOsCompatible(packTags, machineOs, requireOsTag);

  private static HashSet<string> GetMachineFeatureTags(MachineInfo info)
    => PackApplicabilityRules.GetMachineFeatureTags(info.InstalledFeatures);

  private async Task<HashSet<string>> ComputeApplicablePackIdsAsync(MachineInfo info)
  {
    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pack in ContentPacks)
    {
      var decision = await EvaluatePackApplicabilityAsync(pack, info);
      if (IsHighConfidenceApplicable(decision))
        ids.Add(pack.PackId);
    }

    return ids;
  }

  private static bool IsHighConfidenceApplicable(PackApplicabilityDecision decision)
    => decision.State == ApplicabilityState.Applicable
      && decision.Confidence == ApplicabilityConfidence.High;

  private static IEnumerable<string> ExtractMatchingTags(string text)
    => PackApplicabilityRules.ExtractMatchingTags(text);

  [RelayCommand]
  private async Task DiscoverRemoteHostsAsync()
  {
    if (_fleetService == null)
    {
      AdRemoteDiscoveryStatus = "Fleet service unavailable.";
      return;
    }

    if (IsBusy)
      return;

    if (string.IsNullOrWhiteSpace(AdRemoteTargets))
    {
      AdRemoteDiscoveryStatus = "Enter target hosts first (comma or newline separated), then click Discover Hosts.";
      return;
    }

    try
    {
      IsBusy = true;
      AdRemoteDiscoveryStatus = "Discovering hosts and testing WinRM...";
      AdRemoteScanStatus = string.Empty;

      var targets = ParseFleetTargets(AdRemoteTargets.Replace("\r", "\n").Replace("\n", ","));
      if (targets.Count == 0)
      {
        AdRemoteDiscoveryStatus = "No valid targets parsed.";
        return;
      }

      var status = await _fleetService.CheckStatusAsync(targets, CancellationToken.None);

      var existingSelection = AdRemoteDiscoveredHosts
        .Where(h => h.IsSelected)
        .Select(h => h.HostName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      AdRemoteDiscoveredHosts.Clear();
      foreach (var host in status.MachineStatuses.OrderBy(h => h.MachineName, StringComparer.OrdinalIgnoreCase))
      {
        AdRemoteDiscoveredHosts.Add(new RemoteHostScanItem
        {
          HostName = host.MachineName,
          WinRmStatus = host.IsReachable ? "Available" : "Unavailable",
          ScanStatus = "Not Scanned",
          Details = host.Message,
          IsSelected = existingSelection.Contains(host.MachineName) && host.IsReachable
        });
      }

      _remoteDiscoveredHostsView?.Refresh();
      OnPropertyChanged(nameof(AdRemoteDiscoveredHostsView));

      AdRemoteDiscoveryStatus = "Discovery complete: " + status.TotalMachines
        + " host(s), " + status.ReachableCount + " WinRM available.";
      StatusText = "Remote host discovery complete.";
    }
    catch (Exception ex)
    {
      AdRemoteDiscoveryStatus = "Host discovery failed: " + ex.Message;
      StatusText = "Remote host discovery failed.";
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ScanSelectedRemoteHostsAsync()
  {
    if (_fleetService == null)
    {
      AdRemoteScanStatus = "Fleet service unavailable.";
      return;
    }

    if (IsBusy)
      return;

    var selected = AdRemoteDiscoveredHosts
      .Where(h => h.IsSelected)
      .ToList();

    if (selected.Count == 0)
    {
      AdRemoteScanStatus = "Select one or more discovered hosts first.";
      return;
    }

    try
    {
      IsBusy = true;
      AdRemoteScanStatus = "Scanning selected hosts...";

      foreach (var host in selected)
      {
        host.ScanStatus = "Queued";
        if (!host.IsWinRmReachable)
        {
          host.ScanStatus = "Blocked";
          host.Details = "WinRM unavailable. Host cannot be scanned.";
        }
      }

      var scanTargets = selected
        .Where(h => h.IsWinRmReachable)
        .Select(h => new STIGForge.Infrastructure.System.FleetTarget { HostName = h.HostName })
        .ToList();

      if (scanTargets.Count == 0)
      {
        AdRemoteScanStatus = "No selected hosts are WinRM-accessible.";
        return;
      }

      foreach (var host in selected.Where(h => h.IsWinRmReachable))
        host.ScanStatus = "Running";

      var status = await _fleetService.CheckStatusAsync(scanTargets, CancellationToken.None);

      var byName = status.MachineStatuses.ToDictionary(m => m.MachineName, StringComparer.OrdinalIgnoreCase);
      foreach (var host in selected.Where(h => h.IsWinRmReachable))
      {
        if (byName.TryGetValue(host.HostName, out var machine))
        {
          host.WinRmStatus = machine.IsReachable ? "Available" : "Unavailable";
          host.ScanStatus = machine.IsReachable ? "Completed" : "Failed";
          host.Details = machine.Message;
        }
        else
        {
          host.WinRmStatus = "Unknown";
          host.ScanStatus = "Failed";
          host.Details = "No status returned.";
        }
      }

      var completed = selected.Count(h => string.Equals(h.ScanStatus, "Completed", StringComparison.OrdinalIgnoreCase));
      var failed = selected.Count(h => string.Equals(h.ScanStatus, "Failed", StringComparison.OrdinalIgnoreCase));
      var blocked = selected.Count(h => string.Equals(h.ScanStatus, "Blocked", StringComparison.OrdinalIgnoreCase));

      AdRemoteScanStatus = "Scan selected complete: completed=" + completed
        + ", failed=" + failed
        + ", blocked=" + blocked + ".";
      StatusText = "Remote selected scan complete.";
    }
    catch (Exception ex)
    {
      AdRemoteScanStatus = "Scan selected failed: " + ex.Message;
      StatusText = "Remote selected scan failed.";
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
      MachineScanSummary = "Scanning machine...";
      MachineSelectionDiagnostics = string.Empty;
      MachineScanTags.Clear();
      ApplicablePackPairs.Clear();
      SelectionReasons.Clear();

      var info = await Task.Run(() => DetectMachineInfo(), _cts.Token);
      PopulateMachineScanTags(info);

      var lines = new List<string>
      {
        "Detected target: " + info.OsTarget,
        "Role: " + info.Role
      };

      if (info.IsServer && info.InstalledFeatures.Count > 0)
      {
        lines.Add("Server features: " + string.Join(", ", info.InstalledFeatures.Take(10)));
        if (info.InstalledFeatures.Count > 10)
          lines.Add("  ... and " + (info.InstalledFeatures.Count - 10) + " more");
      }

      ApplicablePackIds.Clear();
      _canonicalScapByStigId.Clear();
      var summaryApplicableCount = 0;
      var summaryStigCount = 0;
      var summaryScapCount = 0;
      var summaryGpoCount = 0;
      var summaryAdmxCount = 0;

      if (ContentPacks.Count > 0)
      {
        var applicabilityByPackId = new Dictionary<string, PackApplicabilityDecision>(StringComparer.OrdinalIgnoreCase);
        var applicable = new List<string>();
        var unknown = new List<string>();
        foreach (var pack in ContentPacks)
        {
          var decision = await EvaluatePackApplicabilityAsync(pack, info);
          applicabilityByPackId[pack.PackId] = decision;
          if (IsHighConfidenceApplicable(decision))
          {
            applicable.Add(pack.Name);
            ApplicablePackIds.Add(pack.PackId);
          }
          else if (decision.State == ApplicabilityState.Unknown)
          {
            unknown.Add(pack.Name);
          }
        }

        if (applicable.Count > 0)
        {
          lines.Add("");
          lines.Add("High-confidence applicable packs (" + applicable.Count + "):");
          foreach (var name in applicable)
            lines.Add("  - " + name);

          var applicableStigs = ContentPacks
            .Where(p => ApplicablePackIds.Contains(p.PackId)
              && string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

          lines.Add("STIG rows shown in mapping table: " + applicableStigs.Count + ".");

          var allDerivedForApplicable = await ResolveDerivedPackIdsAsync(
            applicableStigs.Select(s => s.PackId).ToHashSet(StringComparer.OrdinalIgnoreCase),
            info);

          var derivedAdmxIds = allDerivedForApplicable
            .Where(id => ContentPacks.Any(p => string.Equals(p.PackId, id, StringComparison.OrdinalIgnoreCase)
              && string.Equals(ResolvePackFormat(p), "ADMX", StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

          var derivedGpoIds = allDerivedForApplicable
            .Where(id => ContentPacks.Any(p => string.Equals(p.PackId, id, StringComparison.OrdinalIgnoreCase)
              && string.Equals(ResolvePackFormat(p), "GPO", StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

          var canonicalScapIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
          var mappingDiagnostics = new List<string>();

          foreach (var stigPack in applicableStigs)
          {
            SelectionReasons.Add(new SelectionReasonRow
            {
              Type = "STIG",
              Name = stigPack.Name,
              Selected = true,
              Reason = applicabilityByPackId.TryGetValue(stigPack.PackId, out var stigDecision)
                ? "Matched host OS/role/features. " + stigDecision.ReasonCode
                : "Matched host OS/role/features."
            });

            var derivedForStig = await ResolveDerivedPackIdsAsync(
              new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stigPack.PackId },
              info);

            var matchingScap = ContentPacks
              .Where(p => derivedForStig.Contains(p.PackId)
                && string.Equals(ResolvePackFormat(p), "SCAP", StringComparison.OrdinalIgnoreCase))
              .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
              .ToList();

            var canonical = await SelectCanonicalScapAsync(stigPack, matchingScap);
            var canonicalWinner = canonical.Winner;

            if (canonicalWinner == null)
            {
              _canonicalScapByStigId.Remove(stigPack.PackId);
              ApplicablePackPairs.Add(new ApplicablePackPair
              {
                StigName = stigPack.Name,
                StigId = stigPack.PackId,
                ScapName = "(none)",
                ScapId = string.Empty,
                MatchState = "Missing",
                Reason = "No matching SCAP benchmark imported for this STIG."
              });
            }
            else
            {
              _canonicalScapByStigId[stigPack.PackId] = canonicalWinner.PackId;
              canonicalScapIds.Add(canonicalWinner.PackId);
              ApplicablePackPairs.Add(new ApplicablePackPair
              {
                StigName = stigPack.Name,
                StigId = stigPack.PackId,
                ScapName = canonicalWinner.Name,
                ScapId = canonicalWinner.PackId,
                MatchState = "Matched",
                Reason = canonical.HasConflict
                  ? "Canonical SCAP selected deterministically from multiple candidates."
                  : "Derived using STIG-to-SCAP mapping keys."
              });
            }

            if (canonical.Reasons.Count > 0 || canonical.HasConflict)
            {
              var mappingLine = "[MAP] " + stigPack.Name + " => " + (canonicalWinner?.Name ?? "(none)")
                + " | " + string.Join(" | ", canonical.Reasons);
              mappingDiagnostics.Add(mappingLine);
            }
          }

          var selectedPackIds = new HashSet<string>(applicableStigs.Select(p => p.PackId), StringComparer.OrdinalIgnoreCase);
          selectedPackIds.UnionWith(canonicalScapIds);
          selectedPackIds.UnionWith(derivedGpoIds);
          selectedPackIds.UnionWith(derivedAdmxIds);

          summaryStigCount = applicableStigs.Count;
          summaryScapCount = canonicalScapIds.Count;
          summaryGpoCount = derivedGpoIds.Count;
          summaryAdmxCount = derivedAdmxIds.Count;
          summaryApplicableCount = selectedPackIds.Count;

          AddMachineScanTag("Applicable Packs", summaryApplicableCount.ToString());
          AddMachineScanTag("Applicable STIGs", summaryStigCount.ToString());
          AddMachineScanTag("Auto SCAP", summaryScapCount.ToString());
          AddMachineScanTag("Auto GPO/LGPO", summaryGpoCount.ToString());
          AddMachineScanTag("Auto ADMX", summaryAdmxCount.ToString());
          if (unknown.Count > 0)
            AddMachineScanTag("Unknown Packs", unknown.Count.ToString());

          lines.Add("Auto-selected pack set: " + summaryApplicableCount
            + " (STIG " + summaryStigCount
            + ", SCAP " + summaryScapCount
            + ", GPO " + summaryGpoCount
            + ", ADMX " + summaryAdmxCount + ")");
          if (unknown.Count > 0)
            lines.Add("Unknown packs (needs confirmation): " + unknown.Count);

          foreach (var stigPack in ContentPacks
            .Where(p => string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase)
              && !ApplicablePackIds.Contains(p.PackId))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            SelectionReasons.Add(new SelectionReasonRow
            {
              Type = "STIG",
              Name = stigPack.Name,
              Selected = false,
              Reason = "Filtered out by OS/role/feature applicability rules."
            });
          }

          foreach (var scapPack in ContentPacks
            .Where(p => string.Equals(ResolvePackFormat(p), "SCAP", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            var selected = canonicalScapIds.Contains(scapPack.PackId);
            SelectionReasons.Add(new SelectionReasonRow
            {
              Type = "SCAP",
              Name = scapPack.Name,
              Selected = selected,
              Reason = selected
                ? "Canonical mapping from selected STIG(s)."
                : "Not selected as canonical SCAP for any selected STIG."
            });
          }

          foreach (var gpoPack in ContentPacks
            .Where(p => string.Equals(ResolvePackFormat(p), "GPO", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            var selected = derivedGpoIds.Contains(gpoPack.PackId);
            SelectionReasons.Add(new SelectionReasonRow
            {
              Type = "GPO",
              Name = gpoPack.Name,
              Selected = selected,
              Reason = selected
                ? "Auto-selected from machine OS + STIG-derived policy scope."
                : "Not selected: machine policy scope did not match this GPO/LGPO package."
            });
          }

          foreach (var admxPack in ContentPacks
            .Where(p => string.Equals(ResolvePackFormat(p), "ADMX", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            var selected = derivedAdmxIds.Contains(admxPack.PackId);
            SelectionReasons.Add(new SelectionReasonRow
            {
              Type = "ADMX",
              Name = admxPack.Name,
              Selected = selected,
              Reason = selected
                ? "Auto-selected from installed-feature + OS matching."
                : "Not selected: host software/OS signal did not match this template pack."
            });
          }

          var diagnostics = new List<string>();
          diagnostics.Add("[Why applicable?]");
          foreach (var pack in ContentPacks.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            if (!applicabilityByPackId.TryGetValue(pack.PackId, out var decision))
              continue;

            diagnostics.Add("[APP] " + pack.Name
              + " => state=" + decision.State
              + ", confidence=" + decision.Confidence
              + ", reason=" + decision.ReasonCode);
            foreach (var evidence in decision.Evidence)
              diagnostics.Add("  evidence: " + evidence);
          }

          if (mappingDiagnostics.Count > 0)
          {
            diagnostics.Add(string.Empty);
            diagnostics.Add("[Canonical mapping details]");
            diagnostics.AddRange(mappingDiagnostics);
          }

          diagnostics.Add(string.Empty);
          diagnostics.Add("[Selection summary]");
          diagnostics.AddRange(SelectionReasons
            .Select(r => "[" + r.Type + "] " + r.Name + " => selected=" + (r.Selected ? "true" : "false") + " | " + r.Reason)
            .ToList());

          MachineSelectionDiagnostics = string.Join("\n", diagnostics);
          foreach (var line in diagnostics.Take(40))
            System.Diagnostics.Trace.TraceInformation("Selection reason: " + line);
        }
        else if (unknown.Count > 0)
        {
          summaryApplicableCount = 0;
          summaryStigCount = 0;
          summaryScapCount = 0;
          summaryGpoCount = 0;
          summaryAdmxCount = 0;
          AddMachineScanTag("Unknown Packs", unknown.Count.ToString());
          lines.Add("");
          lines.Add("No high-confidence applicable packs were detected.");
          lines.Add("Unknown packs requiring confirmation: " + unknown.Count + ".");
          lines.Add("Applicable STIG rows shown in mapping table: 0.");

          var diagnostics = new List<string>
          {
            "[Why applicable?]"
          };
          foreach (var pack in ContentPacks.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
          {
            if (!applicabilityByPackId.TryGetValue(pack.PackId, out var decision))
              continue;

            diagnostics.Add("[APP] " + pack.Name
              + " => state=" + decision.State
              + ", confidence=" + decision.Confidence
              + ", reason=" + decision.ReasonCode);
            foreach (var evidence in decision.Evidence)
              diagnostics.Add("  evidence: " + evidence);
          }

          MachineSelectionDiagnostics = string.Join("\n", diagnostics);
        }
        else
        {
          lines.Add("");
          lines.Add("No imported packs match this machine. Add ZIP files to the import folder and run Scan Import Folder.");
        }
      }
      else
      {
        lines.Add("");
        lines.Add("No content packs imported yet. Add ZIP files to the import folder, scan, then re-run local machine scan.");
      }

      var recommendations = GetStigRecommendations(info);
      if (recommendations.Count > 0)
      {
        lines.Add("");
        lines.Add("Recommended STIGs for this machine:");
        foreach (var rec in recommendations)
          lines.Add("  - " + rec);
      }

      MachineScanSummary = info.OsTarget + " | " + info.Role
        + " | Applicable: " + summaryApplicableCount
        + " (STIG " + summaryStigCount
        + ", SCAP " + summaryScapCount
        + ", GPO " + summaryGpoCount
        + ", ADMX " + summaryAdmxCount + ")";
      MachineApplicabilityStatus = string.Join("\n", lines);
      StatusText = "Machine scan complete: " + info.OsTarget + " / " + info.Role;
    }
    catch (Exception ex)
    {
      MachineScanTags.Clear();
      MachineApplicabilityStatus = "Scan failed: " + ex.Message;
      StatusText = "Machine scan failed.";
    }
    finally
    {
      IsBusy = false;
    }
  }

  private void PopulateMachineScanTags(MachineInfo info)
  {
    MachineScanTags.Clear();
    AddMachineScanTag("Host", info.Hostname);
    AddMachineScanTag("OS", info.ProductName);
    AddMachineScanTag("Build", info.BuildNumber);
    AddMachineScanTag("Role", info.Role);
    AddMachineScanTag("Target", info.OsTarget.ToString());
    AddMachineScanTag("Type", info.IsServer ? "Server" : "Workstation");

    if (!string.IsNullOrWhiteSpace(info.DisplayVersion))
      AddMachineScanTag("Version", info.DisplayVersion);
    if (!string.IsNullOrWhiteSpace(info.EditionId))
      AddMachineScanTag("Edition", info.EditionId);

    var orderedFeatures = info.InstalledFeatures
      .OrderBy(feature => feature, StringComparer.OrdinalIgnoreCase)
      .ToList();
    var topFeatures = orderedFeatures.Take(8).ToList();
    foreach (var feature in topFeatures)
      AddMachineScanTag("Feature", feature);

    var remaining = orderedFeatures.Count - topFeatures.Count;
    if (remaining > 0)
      AddMachineScanTag("Features", "+" + remaining + " more");
  }

  private void AddMachineScanTag(string label, string value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return;

    var text = label + ": " + value;
    MachineScanTags.Add(new MachineScanTag
    {
      Text = text,
      ToolTip = text
    });
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

    // Detect OneDrive
    try
    {
      var oneDrivePaths = new[]
      {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft OneDrive", "OneDrive.exe")
      };

      if (oneDrivePaths.Any(File.Exists))
        info.InstalledFeatures.Add("Microsoft OneDrive");
    }
    catch { }

    CollectInstalledProgramSignals(info);
    CollectServiceSignals(info);
    CollectKnownFileSignals(info);

    return info;
  }

  private static void CollectInstalledProgramSignals(MachineInfo info)
  {
    var roots = new[]
    {
      @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
      @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    };

    foreach (var root in roots)
    {
      try
      {
        using var uninstallRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root);
        if (uninstallRoot == null)
          continue;

        foreach (var subName in uninstallRoot.GetSubKeyNames())
        {
          try
          {
            using var sub = uninstallRoot.OpenSubKey(subName);
            var displayName = (sub?.GetValue("DisplayName") as string) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(displayName))
              continue;

            var normalized = displayName.ToLowerInvariant();
            if (normalized.Contains("forti", StringComparison.Ordinal)
                || normalized.Contains("fortinet", StringComparison.Ordinal)
                || normalized.Contains("forticlient", StringComparison.Ordinal))
            {
              AddHostSignal(info, "registry:uninstall:" + displayName);
            }

            if (normalized.Contains("symantec", StringComparison.Ordinal)
                || normalized.Contains("endpoint protection", StringComparison.Ordinal)
                || normalized.Contains("sep", StringComparison.Ordinal))
            {
              AddHostSignal(info, "registry:uninstall:" + displayName);
            }
          }
          catch (Exception ex)
          {
            System.Diagnostics.Trace.TraceWarning("Installed program signal scan failed: " + ex.Message);
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Installed program root scan failed: " + ex.Message);
      }
    }
  }

  private static void CollectServiceSignals(MachineInfo info)
  {
    const string servicesRoot = @"SYSTEM\CurrentControlSet\Services";
    try
    {
      using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(servicesRoot);
      if (root == null)
        return;

      foreach (var serviceName in root.GetSubKeyNames())
      {
        try
        {
          var normalizedServiceName = serviceName.ToLowerInvariant();
          var serviceMatchedByName = normalizedServiceName.Contains("forti", StringComparison.Ordinal)
            || normalizedServiceName.Contains("fortinet", StringComparison.Ordinal)
            || normalizedServiceName.Contains("forticlient", StringComparison.Ordinal)
            || normalizedServiceName.Contains("symantec", StringComparison.Ordinal)
            || normalizedServiceName.Contains("sep", StringComparison.Ordinal);

          if (serviceMatchedByName)
          {
            AddHostSignal(info, "service:" + serviceName);
            continue;
          }

          using var serviceKey = root.OpenSubKey(serviceName);
          var displayName = (serviceKey?.GetValue("DisplayName") as string) ?? string.Empty;
          if (string.IsNullOrWhiteSpace(displayName))
            continue;

          var normalizedDisplayName = displayName.ToLowerInvariant();
          if (normalizedDisplayName.Contains("forti", StringComparison.Ordinal)
              || normalizedDisplayName.Contains("fortinet", StringComparison.Ordinal)
              || normalizedDisplayName.Contains("forticlient", StringComparison.Ordinal)
              || normalizedDisplayName.Contains("symantec", StringComparison.Ordinal)
              || normalizedDisplayName.Contains("endpoint protection", StringComparison.Ordinal)
              || normalizedDisplayName.Contains("sep", StringComparison.Ordinal))
          {
            AddHostSignal(info, "service:" + displayName);
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Trace.TraceWarning("Service signal scan failed: " + ex.Message);
        }
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Service root scan failed: " + ex.Message);
    }
  }

  private static void CollectKnownFileSignals(MachineInfo info)
  {
    try
    {
      var fileCandidates = new[]
      {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Fortinet", "FortiClient", "FortiClient.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Fortinet", "FortiClient", "FortiClient.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Symantec", "Symantec Endpoint Protection", "Smc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Symantec", "Symantec Endpoint Protection", "Smc.exe")
      };

      foreach (var candidate in fileCandidates)
      {
        if (File.Exists(candidate))
          AddHostSignal(info, "file:" + Path.GetFileName(candidate));
      }
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("File signal scan failed: " + ex.Message);
    }
  }

  private static void AddHostSignal(MachineInfo info, string signal)
  {
    if (string.IsNullOrWhiteSpace(signal))
      return;

    if (!info.HostSignals.Contains(signal, StringComparer.OrdinalIgnoreCase))
      info.HostSignals.Add(signal);
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

  private async Task<PackApplicabilityDecision> EvaluatePackApplicabilityAsync(ContentPack pack, MachineInfo info)
  {
    IReadOnlyList<ControlRecord> controls = Array.Empty<ControlRecord>();
    try
    {
      controls = await _controls.ListControlsAsync(pack.PackId, CancellationToken.None);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Pack applicability check failed: " + ex.Message);
    }

    var format = ResolvePackFormat(pack);
    var controlTargets = controls
      .Select(c => c.Applicability.OsTarget)
      .Where(t => t != OsTarget.Unknown)
      .Distinct()
      .ToArray();

    var androidNamedPack = pack.Name.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0
      || pack.SourceLabel.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0;
    if (androidNamedPack)
    {
      System.Diagnostics.Trace.TraceInformation("Applicability skipped (android guard): " + pack.Name);
      return new PackApplicabilityDecision
      {
        State = ApplicabilityState.NotApplicable,
        Confidence = ApplicabilityConfidence.High,
        ReasonCode = "android_on_windows",
        Evidence = new[] { "Android pack excluded on Windows host." }
      };
    }

    var input = new PackApplicabilityInput
    {
      PackName = pack.Name,
      SourceLabel = pack.SourceLabel,
      Format = format,
      MachineOs = info.OsTarget,
      MachineRole = info.RoleTemplate,
      InstalledFeatures = info.InstalledFeatures,
      ControlOsTargets = controlTargets,
      HostSignals = info.HostSignals
    };

    var decision = PackApplicabilityRules.Evaluate(input);
    System.Diagnostics.Trace.TraceInformation(
      "Applicability " + decision.State + " (" + decision.Confidence + "): " + pack.Name + " [" + format + "] " + decision.ReasonCode);
    return decision;
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
    public List<string> HostSignals { get; set; } = new();
  }
}
