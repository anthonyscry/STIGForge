using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using STIGForge.App.Helpers;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Constants;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class MainViewModel
{
  [RelayCommand]
  private async Task ComparePacks()
  {
    try
    {
      if (ContentPacks.Count < 2)
      {
        StatusText = "Need at least 2 packs to compare.";
        return;
      }

      var dialog = new Views.PackComparisonDialog(ContentPacks.ToList());
      if (dialog.ShowDialog() != true)
        return;

      var baselinePackId = dialog.BaselinePackId;
      var targetPackId = dialog.TargetPackId;

      if (string.IsNullOrWhiteSpace(baselinePackId) || string.IsNullOrWhiteSpace(targetPackId))
      {
        StatusText = "Please select both baseline and target packs.";
        return;
      }

      if (baselinePackId == targetPackId)
      {
        StatusText = "Please select different packs to compare.";
        return;
      }

      IsBusy = true;
      StatusText = "Comparing packs...";

      var diffService = new Core.Services.BaselineDiffService(_controls);
      var diff = await diffService.ComparePacksAsync(baselinePackId, targetPackId, CancellationToken.None);

      var baselinePack = ContentPacks.First(p => p.PackId == baselinePackId);
      var targetPack = ContentPacks.First(p => p.PackId == targetPackId);

      var viewModel = new ViewModels.DiffViewerViewModel(diff, baselinePack.Name, targetPack.Name);
      var diffViewer = new Views.DiffViewer(viewModel);
      diffViewer.ShowDialog();

      StatusText = "Comparison complete.";
    }
    catch (Exception ex)
    {
      StatusText = "Comparison failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task RebaseOverlay()
  {
    try
    {
      if (OverlayItems.Count == 0)
      {
        StatusText = "No overlays available. Create an overlay first.";
        return;
      }

      if (ContentPacks.Count < 2)
      {
        StatusText = "Need at least 2 packs for rebase.";
        return;
      }

      var overlayList = await _overlays.ListAsync(CancellationToken.None);

      var viewModel = new ViewModels.RebaseWizardViewModel(
        _controls, 
        _overlays,
        overlayList.ToList(),
        ContentPacks.ToList(),
        _audit);

      var wizard = new Views.RebaseWizard(viewModel);
      wizard.ShowDialog();

      await RefreshOverlaysAsync();
      StatusText = "Rebase wizard closed.";
    }
    catch (Exception ex)
    {
      StatusText = "Rebase failed: " + ex.Message;
    }
  }

  private void RefreshDashboard()
  {
    var bundleRoot = BundleRoot;
    DashHasBundle = !string.IsNullOrWhiteSpace(bundleRoot) && Directory.Exists(bundleRoot);
    if (!DashHasBundle)
    {
      DashBundleLabel = "(no bundle selected)";
      DashPackLabel = "";
      DashProfileLabel = "";
      DashTotalControls = 0;
      DashAutoControls = 0;
      DashManualControls = 0;
      DashVerifyClosed = 0;
      DashVerifyOpen = 0;
      DashVerifyTotal = 0;
      DashVerifyPercent = "—";
      DashManualPass = 0;
      DashManualFail = 0;
      DashManualNa = 0;
      DashManualOpen = 0;
      DashManualPercent = "—";
      DashMissionSeverity = string.Empty;
      DashRecoveryGuidance = string.Empty;
      DashLastVerify = "";
      DashLastExport = "";
      TrendSnapshots = new ObservableCollection<TrendSnapshot>();
      return;
    }

    var bundlePath = bundleRoot!;
    DashBundleLabel = Path.GetFileName(bundlePath);
    try
    {
      var summary = _bundleMissionSummary.LoadSummary(bundlePath);
      DashPackLabel = summary.PackName;
      DashProfileLabel = summary.ProfileName;

      DashTotalControls = summary.TotalControls;
      DashAutoControls = summary.AutoControls;
      DashManualControls = summary.ManualControls;

      DashVerifyClosed = summary.Verify.ClosedCount;
      DashVerifyOpen = summary.Verify.OpenCount;
      DashVerifyTotal = summary.Verify.TotalCount;
      DashVerifyPercent = DashVerifyTotal > 0
        ? $"{(double)DashVerifyClosed / DashVerifyTotal:P0}"
        : "—";

      DashManualPass = summary.Manual.PassCount;
      DashManualFail = summary.Manual.FailCount;
      DashManualNa = summary.Manual.NotApplicableCount;
      DashManualOpen = summary.Manual.OpenCount;
      DashManualPercent = summary.Manual.TotalCount > 0
        ? $"{(double)summary.Manual.AnsweredCount / summary.Manual.TotalCount:P0}"
        : "—";
      DashMissionSeverity = BuildMissionSeverityLine(summary);
      DashRecoveryGuidance = BuildMissionRecoveryGuidance(summary, bundlePath);

      var snapshot = new TrendSnapshot
      {
        Timestamp = DateTimeOffset.Now,
        PassCount = DashManualPass,
        FailCount = DashManualFail,
        OpenCount = DashManualOpen,
        NotApplicableCount = DashManualNa,
        TotalControls = DashTotalControls
      };
      _trendService.RecordSnapshot(bundlePath, snapshot);
      var trendFile = _trendService.LoadTrend(bundlePath);
      TrendSnapshots = new ObservableCollection<TrendSnapshot>(trendFile.Snapshots);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Dashboard summary load failed: " + ex.Message);
      DashPackLabel = "";
      DashProfileLabel = "";
      DashTotalControls = 0;
      DashAutoControls = 0;
      DashManualControls = 0;
      DashVerifyClosed = 0;
      DashVerifyOpen = 0;
      DashVerifyTotal = 0;
      DashVerifyPercent = "—";
      DashManualPass = 0;
      DashManualFail = 0;
      DashManualNa = 0;
      DashManualOpen = 0;
      DashManualPercent = "—";
      DashMissionSeverity = "Mission severity unavailable";
      DashRecoveryGuidance = "Load verify artifacts to compute mission recovery guidance.";
      TrendSnapshots = new ObservableCollection<TrendSnapshot>();
    }

    DashLastVerify = "";
    var verifyDir = Path.Combine(bundlePath, BundlePaths.VerifyDirectory);
    if (Directory.Exists(verifyDir))
    {
      try
      {
        var lastVerify = DateTimeOffset.MinValue;
        foreach (var reportFile in Directory.GetFiles(verifyDir, BundlePaths.ConsolidatedResultsFileName, SearchOption.AllDirectories))
        {
          var report = STIGForge.Verify.VerifyReportReader.LoadFromJson(reportFile);
          if (report.FinishedAt > lastVerify)
            lastVerify = report.FinishedAt;
        }

        if (lastVerify > DateTimeOffset.MinValue)
          DashLastVerify = lastVerify.ToString("yyyy-MM-dd HH:mm");
      }
      catch (Exception ex)
      {
        System.Diagnostics.Trace.TraceWarning("Dashboard verify timestamp load failed: " + ex.Message);
      }
    }

    // Check for eMASS export
    var emassDir = Path.Combine(bundlePath, BundlePaths.ExportDirectory);
    if (Directory.Exists(emassDir))
    {
      try
      {
        var latest = Directory.GetDirectories(emassDir).OrderByDescending(d => d).FirstOrDefault();
        DashLastExport = latest != null ? Path.GetFileName(latest) : "";
      }
      catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Dashboard export load failed: " + ex.Message); DashLastExport = ""; }
    }
    else
    {
      DashLastExport = "";
    }
  }

  private static string BuildMissionSeverityLine(BundleMissionSummary summary)
  {
    return $"Mission severity: blocking={summary.Verify.BlockingFailureCount} warnings={summary.Verify.RecoverableWarningCount} optional-skips={summary.Verify.OptionalSkipCount}";
  }

  private static string BuildMissionRecoveryGuidance(BundleMissionSummary summary, string bundleRoot)
  {
    if (summary.Verify.BlockingFailureCount > 0)
    {
      var verifyRoot = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
      var rollbackHint = GetRollbackGuidance(bundleRoot);
      return "Blocking findings detected. Required artifacts: consolidated verify reports and coverage overlap artifacts. "
        + "Next action: resolve failing/open controls, rerun Verify, and regenerate mission summary. "
        + $"Recovery paths: {verifyRoot}. {rollbackHint}";
    }

    if (summary.Verify.RecoverableWarningCount > 0 || summary.Verify.OptionalSkipCount > 0)
    {
      return "Warnings or optional skips detected. Required artifacts: verify reports and optional-skip rationale. "
        + "Next action: review warning diagnostics and confirm skip intent before release promotion.";
    }

    return "No blocking mission findings. Next action: proceed with export and release evidence collection.";
  }

  private static string GetRollbackGuidance(string bundleRoot)
  {
      var snapshotsDir = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "Snapshots");
    if (!Directory.Exists(snapshotsDir))
      return "Rollback guidance: use the latest rollback script from Apply/Snapshots if rollback is required.";

    var latestRollback = Directory.GetFiles(snapshotsDir, "rollback_*.ps1", SearchOption.TopDirectoryOnly)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();

    return latestRollback == null
      ? "Rollback guidance: use the latest rollback script from Apply/Snapshots if rollback is required."
      : $"Rollback guidance: run '{latestRollback}' if operator-approved rollback is required.";
  }

  [RelayCommand]
  private void DashRefresh()
  {
    RefreshDashboard();
  }

  [RelayCommand]
  private void ShowAbout()
  {
    var dialog = new Views.AboutDialog(
      _paths.GetAppDataRoot(),
      ContentPacks.Count,
      Profiles.Count,
      OverlayItems.Count);
    dialog.ShowDialog();
  }

  [RelayCommand]
  private void ShowHelp()
  {
    var dialog = new Views.HelpDialog();
    dialog.ShowDialog();
  }

  [RelayCommand]
  private void ToggleTheme()
  {
    IsDarkTheme = !IsDarkTheme;
    SaveUiState();
  }

  [RelayCommand]
  private void BrowseBundle()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "manifest.json|manifest.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*",
      Title = "Select bundle Manifest\\manifest.json"
    };

    if (ofd.ShowDialog() != true) return;

    var manifestDir = Path.GetDirectoryName(ofd.FileName) ?? string.Empty;
    var bundle = Directory.GetParent(manifestDir)?.FullName ?? manifestDir;
    BundleRoot = bundle;
  }

  [RelayCommand]
  private void BrowseEvaluateStig()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "Evaluate-STIG.ps1|Evaluate-STIG.ps1|PowerShell (*.ps1)|*.ps1|All Files (*.*)|*.*",
      Title = "Select Evaluate-STIG.ps1"
    };

    if (ofd.ShowDialog() != true) return;

    EvaluateStigRoot = Path.GetDirectoryName(ofd.FileName) ?? string.Empty;
  }

  [RelayCommand]
  private void BrowseScapCommand()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "Executable (*.exe)|*.exe|All Files (*.*)|*.*",
      Title = "Select SCAP/SCC executable"
    };

    if (ofd.ShowDialog() != true) return;

    ScapCommandPath = ofd.FileName;
  }

  [RelayCommand]
  private void BrowsePowerStigModule()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "PowerSTIG module files (*.psd1;*.psm1)|*.psd1;*.psm1|All Files (*.*)|*.*",
      Title = "Select PowerSTIG module file"
    };

    if (ofd.ShowDialog() != true) return;

    PowerStigModulePath = ofd.FileName;
  }

  [RelayCommand]
  private void BrowsePowerStigData()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "PowerSTIG data file (*.psd1)|*.psd1|All Files (*.*)|*.*",
      Title = "Select PowerSTIG data file"
    };

    if (ofd.ShowDialog() != true) return;

    PowerStigDataFile = ofd.FileName;
  }

  [RelayCommand]
  private async Task ActivateToolkitAsync()
  {
    if (IsBusy)
      return;

    try
    {
      IsBusy = true;
    var activated = await TryActivateToolkitAsync(userInitiated: true, _cts.Token);
      if (!activated)
      {
        GuidedNextAction = "Toolkit activation incomplete. Confirm STIG_SCAP path and archives, then retry activation.";
      }
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void OpenToolkitRoot()
  {
    var root = string.IsNullOrWhiteSpace(LocalToolkitRoot)
      ? ResolveDefaultToolkitRoot()
      : LocalToolkitRoot.Trim();

    if (!Directory.Exists(root))
    {
      StatusText = "Toolkit root does not exist: " + root;
      ToolkitActivationStatus = StatusText;
      return;
    }

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = root,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      StatusText = "Failed to open toolkit root: " + ex.Message;
      ToolkitActivationStatus = StatusText;
    }
  }

  [RelayCommand]
  private void OpenBundleFolder()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;
    var path = Directory.Exists(BundleRoot) ? BundleRoot : Path.GetDirectoryName(BundleRoot);
    if (string.IsNullOrWhiteSpace(path)) return;
    if (!Directory.Exists(path))
    {
      StatusText = "Bundle folder does not exist: " + path;
      return;
    }

    try
    {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = path,
        UseShellExecute = true
      });
    }
    catch (Exception ex)
    {
      StatusText = "Failed to open bundle folder: " + ex.Message;
    }
  }

  [RelayCommand]
  private void OpenLastOutput()
  {
    if (string.IsNullOrWhiteSpace(LastOutputPath)) return;
    var path = LastOutputPath;
    if (File.Exists(path))
      path = Path.GetDirectoryName(path) ?? path;
    if (Directory.Exists(path))
    {
      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = path,
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        StatusText = "Failed to open output folder: " + ex.Message;
      }
    }
  }

  [RelayCommand]
  private void OpenAutomationGate()
  {
    if (string.IsNullOrWhiteSpace(AutomationGatePath)) return;
    var path = AutomationGatePath;
    if (File.Exists(path))
      path = Path.GetDirectoryName(path) ?? path;
    if (Directory.Exists(path))
    {
      try
      {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
          FileName = path,
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        StatusText = "Failed to open automation gate folder: " + ex.Message;
      }
    }
  }

  [RelayCommand]
  private void UseRecentBundle()
  {
    var candidate = ResolveSelectedRecentBundle();
    if (string.IsNullOrWhiteSpace(candidate))
    {
      StatusText = "Select a recent bundle first.";
      return;
    }

    if (!Directory.Exists(candidate))
    {
      StatusText = "Recent bundle path does not exist: " + candidate;
      var stale = RecentBundles.FirstOrDefault(b => string.Equals(b, candidate, StringComparison.OrdinalIgnoreCase));
      if (!string.IsNullOrWhiteSpace(stale))
        RecentBundles.Remove(stale);
      SaveUiState();
      return;
    }

      var manifestPath = Path.Combine(candidate, BundlePaths.ManifestDirectory, "manifest.json");
    if (!File.Exists(manifestPath))
    {
      StatusText = "Selected folder is not a valid bundle (missing Manifest/manifest.json).";
      return;
    }

    BundleRoot = candidate;
    AddRecentBundle(candidate);
    StatusText = "Active bundle set: " + Path.GetFileName(candidate);
  }

  [RelayCommand]
  private void DeleteSelectedRecentBundle()
  {
    var candidate = ResolveSelectedRecentBundle();
    if (string.IsNullOrWhiteSpace(candidate))
    {
      StatusText = "Select a recent bundle first.";
      return;
    }

    if (!Directory.Exists(candidate))
    {
      var stale = RecentBundles.FirstOrDefault(b => string.Equals(b, candidate, StringComparison.OrdinalIgnoreCase));
      if (!string.IsNullOrWhiteSpace(stale))
        RecentBundles.Remove(stale);

      SelectedRecentBundle = string.Empty;
      SaveUiState();
      StatusText = "Removed missing recent bundle entry.";
      return;
    }

    var result = System.Windows.MessageBox.Show(
      $"Delete bundle at:\n{candidate}\n\nThis permanently removes the bundle folder and all its contents.",
      "Confirm Delete Recent Bundle",
      System.Windows.MessageBoxButton.YesNo,
      System.Windows.MessageBoxImage.Warning);

    if (result != System.Windows.MessageBoxResult.Yes)
      return;

    try
    {
      Directory.Delete(candidate, recursive: true);

      var existing = RecentBundles.FirstOrDefault(b => string.Equals(b, candidate, StringComparison.OrdinalIgnoreCase));
      if (!string.IsNullOrWhiteSpace(existing))
        RecentBundles.Remove(existing);

      if (string.Equals(BundleRoot, candidate, StringComparison.OrdinalIgnoreCase))
        BundleRoot = string.Empty;

      SelectedRecentBundle = string.Empty;
      SaveUiState();
      StatusText = "Bundle deleted: " + Path.GetFileName(candidate);
    }
    catch (Exception ex)
    {
      StatusText = "Delete recent bundle failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void DeleteBundle()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot) || !Directory.Exists(BundleRoot))
    {
      StatusText = "No bundle selected or bundle path does not exist.";
      return;
    }

    var result = System.Windows.MessageBox.Show(
      $"Delete bundle at:\n{BundleRoot}\n\nThis permanently removes the bundle folder and all its contents (CKLs, POA&Ms, logs, snapshots).",
      "Confirm Delete Bundle",
      System.Windows.MessageBoxButton.YesNo,
      System.Windows.MessageBoxImage.Warning);

    if (result != System.Windows.MessageBoxResult.Yes) return;

    try
    {
      var path = BundleRoot;
      Directory.Delete(path, true);
      RecentBundles.Remove(RecentBundles.FirstOrDefault(b =>
        string.Equals(b, path, StringComparison.OrdinalIgnoreCase)) ?? "");
      BundleRoot = "";
      StatusText = "Bundle deleted: " + Path.GetFileName(path);
    }
    catch (Exception ex)
    {
      StatusText = "Delete failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void RefreshOverlap()
  {
    LoadCoverageOverlap();
  }

  partial void OnIsBusyChanged(bool value)
  {
    OnPropertyChanged(nameof(ActionsEnabled));
  }

  partial void OnBundleRootChanged(string value)
  {
    if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
      AddRecentBundle(value);

    SaveUiState();
    LoadCoverageOverlap();
    LoadManualControls();
    RefreshDashboard();
  }

  partial void OnEvaluateStigRootChanged(string value)
  {
    SaveUiState();
  }

  partial void OnEvaluateStigArgsChanged(string value)
  {
    SaveUiState();
  }

  partial void OnScapCommandPathChanged(string value)
  {
    SaveUiState();
  }

  partial void OnScapArgsChanged(string value)
  {
    SaveUiState();
  }

  partial void OnScapLabelChanged(string value)
  {
    SaveUiState();
  }

  partial void OnLocalToolkitRootChanged(string value)
  {
    SaveUiState();
  }

  partial void OnPowerStigModulePathChanged(string value)
  {
    SaveUiState();
  }

  partial void OnPowerStigDataFileChanged(string value)
  {
    SaveUiState();
  }

  partial void OnPowerStigOutputPathChanged(string value)
  {
    SaveUiState();
  }

  partial void OnPowerStigVerboseChanged(bool value)
  {
    SaveUiState();
  }

  partial void OnApplySkipSnapshotChanged(bool value)
  {
    SaveUiState();
  }

  partial void OnSimpleBuildBeforeRunChanged(bool value)
  {
    SaveUiState();
  }

  partial void OnSelectedMissionPresetChanged(string value)
  {
    ApplyMissionPreset(value);
  }

  private void ApplyMissionPreset(string preset)
  {
    if (string.Equals(preset, "Golden VM Image", StringComparison.Ordinal))
    {
      OrchRunApply = true;
      OrchRunVerify = true;
      OrchRunExport = true;
      ApplySkipSnapshot = true;
      BreakGlassAcknowledged = false;
      if (string.IsNullOrWhiteSpace(BreakGlassReason))
        BreakGlassReason = "Golden image bake pipeline. Hypervisor snapshot managed externally.";
      MissionPresetGuidance = "Use for reference image baking. Snapshot skip is high risk; capture a hypervisor snapshot before apply.";
      GuidedNextAction = "Set PowerSTIG inputs if used, acknowledge break-glass, then run orchestration.";
      SaveUiState();
      return;
    }

    if (string.Equals(preset, "SCCM PXE Image", StringComparison.Ordinal))
    {
      OrchRunApply = true;
      OrchRunVerify = true;
      OrchRunExport = true;
      ApplySkipSnapshot = true;
      BreakGlassAcknowledged = false;
      if (string.IsNullOrWhiteSpace(BreakGlassReason))
        BreakGlassReason = "SCCM PXE image bake. Snapshot handled by image lifecycle tooling.";
      MissionPresetGuidance = "Use for task-sequence image baking. Configure PowerSTIG module path, verify, then export evidence for release gates.";
      GuidedNextAction = "Provide PowerSTIG module path and run orchestration in your image bake workflow.";
      SaveUiState();
      return;
    }

    OrchRunApply = true;
    OrchRunVerify = true;
    OrchRunExport = true;
    ApplySkipSnapshot = false;
    BreakGlassAcknowledged = false;
    BreakGlassReason = string.Empty;
    MissionPresetGuidance = "Use for endpoint hardening on physical workstations or long-lived VMs with rollback snapshot protection.";
    GuidedNextAction = "Build bundle, run orchestration, then review blocking findings before export.";
    SaveUiState();
  }

  private async Task LoadUiStateAsync()
  {
    LocalToolkitRoot = ResolveDefaultToolkitRoot();
    var path = GetUiStatePath();
    if (!File.Exists(path))
    {
      PopulateRecentBundles(null);
      return;
    }

    try
    {
      var json = await File.ReadAllTextAsync(path);
      var state = JsonSerializer.Deserialize<UiState>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (state == null) return;

      var app = System.Windows.Application.Current;
      if (app?.Dispatcher != null)
      {
        _ = app.Dispatcher.InvokeAsync(() =>
        {
          ApplyTheme(state.IsDarkTheme);
          PopulateRecentBundles(state.RecentBundles);
        });
      }
      else
      {
        PopulateRecentBundles(state.RecentBundles);
      }

      SelectedMissionPreset = state.SelectedMissionPreset ?? SelectedMissionPreset;
      LocalToolkitRoot = string.IsNullOrWhiteSpace(state.LocalToolkitRoot)
        ? LocalToolkitRoot
        : state.LocalToolkitRoot;
      EvaluateStigRoot = state.EvaluateStigRoot ?? EvaluateStigRoot;
      EvaluateStigArgs = state.EvaluateStigArgs ?? EvaluateStigArgs;
      ScapCommandPath = state.ScapCommandPath ?? ScapCommandPath;
      ScapArgs = state.ScapArgs ?? ScapArgs;
      ScapLabel = state.ScapLabel ?? ScapLabel;
      PowerStigModulePath = state.PowerStigModulePath ?? PowerStigModulePath;
      PowerStigDataFile = state.PowerStigDataFile ?? PowerStigDataFile;
      PowerStigOutputPath = state.PowerStigOutputPath ?? PowerStigOutputPath;
      PowerStigVerbose = state.PowerStigVerbose;
      ApplySkipSnapshot = state.ApplySkipSnapshot;
      BreakGlassAcknowledged = state.BreakGlassAcknowledged;
      BreakGlassReason = state.BreakGlassReason ?? BreakGlassReason;
      SimpleBuildBeforeRun = state.SimpleBuildBeforeRun;
      SelectedEvalStigPreset = state.EvaluateStigPreset ?? SelectedEvalStigPreset;
      IsDarkTheme = state.IsDarkTheme;
      ManualReviewColumnWidths = state.ManualReviewColumnWidths;
      FullReviewColumnWidths = state.FullReviewColumnWidths;

      // Set BundleRoot LAST — OnBundleRootChanged triggers AddRecentBundle,
      // LoadManualControls, RefreshDashboard which all need other state loaded first
      BundleRoot = state.BundleRoot ?? BundleRoot;
    }
    catch
    {
      PopulateRecentBundles(null);
    }
  }

  private void PopulateRecentBundles(List<string>? bundles)
  {
    var recentBundleItems = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void AddCandidate(string? candidate)
    {
      var path = (candidate ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(path))
        return;
      if (!Directory.Exists(path))
        return;
      var manifestPath = Path.Combine(path, BundlePaths.ManifestDirectory, "manifest.json");
      if (!File.Exists(manifestPath))
        return;
      if (!seen.Add(path))
        return;
      recentBundleItems.Add(path);
    }

    if (bundles != null)
    {
      foreach (var b in bundles)
        AddCandidate(b);
    }

    var bundlesRoot = Path.Combine(_paths.GetAppDataRoot(), "bundles");
    if (Directory.Exists(bundlesRoot))
    {
      string[] dirs;
      try
      {
        dirs = Directory.GetDirectories(bundlesRoot);
      }
      catch
      {
        dirs = Array.Empty<string>();
      }

      foreach (var dir in dirs.OrderByDescending(GetDirectoryLastWriteUtcSafe))
        AddCandidate(dir);
    }

    while (recentBundleItems.Count > 20)
      recentBundleItems.RemoveAt(recentBundleItems.Count - 1);

    if (string.IsNullOrWhiteSpace(SelectedRecentBundle) && recentBundleItems.Count > 0)
      SelectedRecentBundle = recentBundleItems[0];

    if (!string.IsNullOrWhiteSpace(BundleRoot)
        && Directory.Exists(BundleRoot)
        && File.Exists(Path.Combine(BundleRoot, BundlePaths.ManifestDirectory, "manifest.json"))
        && !recentBundleItems.Any(b => string.Equals(b, BundleRoot, StringComparison.OrdinalIgnoreCase)))
    {
      recentBundleItems.Insert(0, BundleRoot);
    }

    RecentBundles.ReplaceAll(recentBundleItems);
    OnPropertyChanged(nameof(RecentBundles));
  }

  private void AddRecentBundle(string bundlePath)
  {
    if (string.IsNullOrWhiteSpace(bundlePath)) return;

    void UpdateCollection()
    {
      var existing = RecentBundles.FirstOrDefault(b =>
        string.Equals(b, bundlePath, StringComparison.OrdinalIgnoreCase));
      if (existing != null) RecentBundles.Remove(existing);

      RecentBundles.Insert(0, bundlePath);
      while (RecentBundles.Count > 20) RecentBundles.RemoveAt(RecentBundles.Count - 1);
    }

    var app = System.Windows.Application.Current;
    if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
      _ = app.Dispatcher.InvokeAsync(UpdateCollection);
    else
      UpdateCollection();

    SaveUiState();
  }

  private string ResolveSelectedRecentBundle()
  {
    var selected = (SelectedRecentBundle ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(selected))
      return selected;

    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(RecentBundles);
    var current = (view?.CurrentItem as string ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(current))
    {
      SelectedRecentBundle = current;
      return current;
    }

    return string.Empty;
  }

  private static DateTime GetDirectoryLastWriteUtcSafe(string path)
  {
    try
    {
      return Directory.GetLastWriteTimeUtc(path);
    }
    catch
    {
      return DateTime.MinValue;
    }
  }

   private void SaveUiState()
   {
     _saveDebounceTimer?.Dispose();
     _saveDebounceTimer = new System.Threading.Timer(
       _ => FlushUiState(),
       null,
       dueTime: 500,
       period: Timeout.Infinite);
   }

   private void FlushUiState()
   {
     try
     {
       var path = GetUiStatePath();
       Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var state = new UiState
        {
          LocalToolkitRoot = LocalToolkitRoot,
          BundleRoot = BundleRoot,
          EvaluateStigRoot = EvaluateStigRoot,
          EvaluateStigArgs = EvaluateStigArgs,
          ScapCommandPath = ScapCommandPath,
          ScapArgs = ScapArgs,
          ScapLabel = ScapLabel,
          PowerStigModulePath = PowerStigModulePath,
          PowerStigDataFile = PowerStigDataFile,
          PowerStigOutputPath = PowerStigOutputPath,
          PowerStigVerbose = PowerStigVerbose,
          ApplySkipSnapshot = ApplySkipSnapshot,
          BreakGlassAcknowledged = BreakGlassAcknowledged,
          BreakGlassReason = BreakGlassReason,
          SelectedMissionPreset = SelectedMissionPreset,
          SimpleBuildBeforeRun = SimpleBuildBeforeRun,
          EvaluateStigPreset = SelectedEvalStigPreset,
          RecentBundles = RecentBundles.ToList(),
          IsDarkTheme = IsDarkTheme,
          ManualReviewColumnWidths = ManualReviewColumnWidths,
          FullReviewColumnWidths = FullReviewColumnWidths
        };

       var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
       File.WriteAllText(path, json);
     }
      catch (Exception ex)
      {
        _logger?.LogWarning(ex, "Failed to save UI state");
      }
    }

  private async Task<bool> TryActivateToolkitAsync(bool userInitiated, CancellationToken ct)
  {
    var sourceRoot = string.IsNullOrWhiteSpace(LocalToolkitRoot)
      ? ResolveDefaultToolkitRoot()
      : LocalToolkitRoot.Trim();
    LocalToolkitRoot = sourceRoot;

    if (!Directory.Exists(sourceRoot))
    {
      ToolkitActivationStatus = "Toolkit root not found: " + sourceRoot;
      if (userInitiated)
        StatusText = ToolkitActivationStatus;
      return false;
    }

    ToolkitActivationStatus = "Activating toolkit from " + sourceRoot + "...";

    ToolkitActivationResult result;
    try
    {
      result = await Task.Run(() => ActivateToolkitFromSource(sourceRoot), ct);
    }
    catch (Exception ex)
    {
      ToolkitActivationStatus = "Toolkit activation failed: " + ex.Message;
      if (userInitiated)
        StatusText = ToolkitActivationStatus;
      return false;
    }

    if (!string.IsNullOrWhiteSpace(result.EvaluateStigRoot))
      EvaluateStigRoot = result.EvaluateStigRoot;

    if (!string.IsNullOrWhiteSpace(result.ScapCommandPath))
      ScapCommandPath = result.ScapCommandPath;

    if (!string.IsNullOrWhiteSpace(result.PowerStigModulePath))
      PowerStigModulePath = result.PowerStigModulePath;

    if (string.IsNullOrWhiteSpace(ScapArgs))
      ScapArgs = string.Empty;

    if (string.IsNullOrWhiteSpace(ScapLabel))
      ScapLabel = "DISA SCAP";

    var scannerReady = !string.IsNullOrWhiteSpace(EvaluateStigRoot) || !string.IsNullOrWhiteSpace(ScapCommandPath);
    var notes = result.Notes.Count == 0
      ? string.Empty
      : " " + string.Join(" | ", result.Notes.Take(3));

    ToolkitActivationStatus = scannerReady
      ? "Toolkit activation complete." + notes
      : "Toolkit activation completed with missing scanners." + notes;

    if (userInitiated || !scannerReady)
      StatusText = ToolkitActivationStatus;

    if (scannerReady)
      GuidedNextAction = "Scanner tooling configured. Continue with Verify or full orchestration.";

    return scannerReady;
  }

  private ToolkitActivationResult ActivateToolkitFromSource(string sourceRoot)
  {
    var result = new ToolkitActivationResult
    {
      SourceRoot = sourceRoot,
      InstallRoot = Path.Combine(_paths.GetAppDataRoot(), "tools")
    };

    Directory.CreateDirectory(result.InstallRoot);

    result.EvaluateStigRoot = ResolveEvaluateStigRoot(sourceRoot, result.InstallRoot, result.Notes);
    result.ScapCommandPath = ResolveScapCommandPath(sourceRoot, result.InstallRoot, result.Notes);
    result.PowerStigModulePath = ResolvePowerStigModulePath(sourceRoot, result.InstallRoot, result.Notes);

    return result;
  }

  private static string ResolveDefaultToolkitRoot()
  {
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
      var dir = new DirectoryInfo(start);
      while (dir != null)
      {
        var candidate = Path.Combine(dir.FullName, "STIG_SCAP");
        if (Directory.Exists(candidate))
          return candidate;

        dir = dir.Parent;
      }
    }

    return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "STIG_SCAP"));
  }

  private static string? ResolveEvaluateStigRoot(string sourceRoot, string installRoot, List<string> notes)
  {
    var scriptPath = FindFirstFileByName(sourceRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG script located in source root.");
      return Path.GetDirectoryName(scriptPath);
    }

    var cachedRoot = Path.Combine(installRoot, "Evaluate-STIG");
    scriptPath = FindFirstFileByName(cachedRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG resolved from managed tools cache.");
      return Path.GetDirectoryName(scriptPath);
    }

    var archive = FindArchiveCandidate(sourceRoot, "evaluate-stig");
    if (archive == null)
    {
      notes.Add("Evaluate-STIG archive not found.");
      return null;
    }

    var extractRoot = Path.Combine(installRoot, "Evaluate-STIG");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 1, notes);
    }
    catch (Exception ex)
    {
      notes.Add("Evaluate-STIG extraction failed: " + ex.Message);
      return null;
    }

    scriptPath = FindFirstFileByName(extractRoot, "Evaluate-STIG.ps1");
    if (!string.IsNullOrWhiteSpace(scriptPath))
    {
      notes.Add("Evaluate-STIG extracted from " + Path.GetFileName(archive) + ".");
      return Path.GetDirectoryName(scriptPath);
    }

    notes.Add("Evaluate-STIG.ps1 not found after extraction.");
    return null;
  }

  private static string? ResolveScapCommandPath(string sourceRoot, string installRoot, List<string> notes)
  {
    var commandPath = FindFirstFileByName(sourceRoot, "cscc.exe")
      ?? FindFirstFileByName(sourceRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC executable located in source root.");
      return commandPath;
    }

    var cachedRoot = Path.Combine(installRoot, "SCC");
    commandPath = FindFirstFileByName(cachedRoot, "cscc.exe")
      ?? FindFirstFileByName(cachedRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC resolved from managed tools cache.");
      return commandPath;
    }

    var archive = FindArchiveCandidate(sourceRoot, "scc");
    if (archive == null)
    {
      notes.Add("SCC archive not found.");
      return null;
    }

    var extractRoot = Path.Combine(installRoot, "SCC");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 2, notes);
    }
    catch (Exception ex)
    {
      notes.Add("SCC extraction failed: " + ex.Message);
      return null;
    }

    commandPath = FindFirstFileByName(extractRoot, "cscc.exe")
      ?? FindFirstFileByName(extractRoot, "scc.exe");
    if (!string.IsNullOrWhiteSpace(commandPath))
    {
      notes.Add("SCC extracted from " + Path.GetFileName(archive) + ".");
      return commandPath;
    }

    notes.Add("cscc.exe/scc.exe not found after extraction.");
    return null;
  }

  private static string? ResolvePowerStigModulePath(string sourceRoot, string installRoot, List<string> notes)
  {
    var modulePath = FindFirstFileByName(sourceRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(sourceRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG module located in source root.");
      return modulePath;
    }

    var cachedRoot = Path.Combine(installRoot, "PowerSTIG");
    modulePath = FindFirstFileByName(cachedRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(cachedRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG resolved from managed tools cache.");
      return modulePath;
    }

    var archive = FindArchiveCandidate(sourceRoot, "powerstig");
    if (archive == null)
      return null;

    var extractRoot = Path.Combine(installRoot, "PowerSTIG");
    try
    {
      ExtractArchiveWithNestedZips(archive, extractRoot, nestedPasses: 1, notes);
    }
    catch (Exception ex)
    {
      notes.Add("PowerSTIG extraction failed: " + ex.Message);
      return null;
    }

    modulePath = FindFirstFileByName(extractRoot, "PowerSTIG.psd1")
      ?? FindFirstFileByName(extractRoot, "PowerStig.psd1");
    if (!string.IsNullOrWhiteSpace(modulePath))
    {
      notes.Add("PowerSTIG extracted from " + Path.GetFileName(archive) + ".");
      return modulePath;
    }

    return null;
  }

  private static void ExtractArchiveWithNestedZips(string archivePath, string destinationRoot, int nestedPasses, List<string> notes)
  {
    if (Directory.Exists(destinationRoot))
      Directory.Delete(destinationRoot, recursive: true);

    Directory.CreateDirectory(destinationRoot);
    ExtractZipSafely(archivePath, destinationRoot);

    for (var pass = 0; pass < nestedPasses; pass++)
    {
      var nestedArchives = EnumerateFilesSafe(destinationRoot, "*.zip", SearchOption.AllDirectories).ToList();
      if (nestedArchives.Count == 0)
        break;

      foreach (var nestedArchive in nestedArchives)
      {
        var nestedExtractRoot = Path.Combine(
          Path.GetDirectoryName(nestedArchive) ?? destinationRoot,
          Path.GetFileNameWithoutExtension(nestedArchive));

        try
        {
          if (!Directory.Exists(nestedExtractRoot) || !Directory.EnumerateFileSystemEntries(nestedExtractRoot).Any())
          {
            Directory.CreateDirectory(nestedExtractRoot);
            ExtractZipSafely(nestedArchive, nestedExtractRoot);
          }
        }
        catch (Exception ex)
        {
          notes.Add("Skipped nested archive " + Path.GetFileName(nestedArchive) + ": " + ex.Message);
        }
      }
    }
  }

  private static void ExtractZipSafely(string zipPath, string destinationRoot)
  {
    var destinationFullPath = Path.GetFullPath(destinationRoot);
    var destinationPrefix = destinationFullPath.EndsWith(Path.DirectorySeparatorChar)
      ? destinationFullPath
      : destinationFullPath + Path.DirectorySeparatorChar;

    using var archive = ZipFile.OpenRead(zipPath);
    foreach (var entry in archive.Entries)
    {
      var destinationPath = Path.GetFullPath(Path.Combine(destinationFullPath, entry.FullName));
      if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(destinationPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidDataException("Blocked archive path traversal entry: " + entry.FullName);
      }

      if (string.IsNullOrEmpty(entry.Name))
      {
        Directory.CreateDirectory(destinationPath);
        continue;
      }

      var entryDirectory = Path.GetDirectoryName(destinationPath);
      if (!string.IsNullOrWhiteSpace(entryDirectory))
        Directory.CreateDirectory(entryDirectory);

      entry.ExtractToFile(destinationPath, overwrite: true);
    }
  }

  private static string? FindArchiveCandidate(string sourceRoot, string token)
  {
    var topLevel = EnumerateFilesSafe(sourceRoot, "*.zip", SearchOption.TopDirectoryOnly)
      .Where(path => Path.GetFileName(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(topLevel))
      return topLevel;

    return EnumerateFilesSafe(sourceRoot, "*.zip", SearchOption.AllDirectories)
      .Where(path => Path.GetFileName(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();
  }

  private static string? FindFirstFileByName(string root, string fileName)
  {
    return EnumerateFilesSafe(root, "*", SearchOption.AllDirectories)
      .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
  }

  private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
  {
    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
      yield break;

    var pending = new Stack<string>();
    pending.Push(root);
    while (pending.Count > 0)
    {
      var current = pending.Pop();

      string[] files;
      try
      {
        files = Directory.GetFiles(current, pattern, SearchOption.TopDirectoryOnly);
      }
      catch
      {
        files = Array.Empty<string>();
      }

      foreach (var file in files)
        yield return file;

      if (option != SearchOption.AllDirectories)
        continue;

      string[] children;
      try
      {
        children = Directory.GetDirectories(current);
      }
      catch
      {
        children = Array.Empty<string>();
      }

      foreach (var child in children)
        pending.Push(child);
    }
  }

  private sealed class ToolkitActivationResult
  {
    public string SourceRoot { get; set; } = string.Empty;
    public string InstallRoot { get; set; } = string.Empty;
    public string? EvaluateStigRoot { get; set; }
    public string? ScapCommandPath { get; set; }
    public string? PowerStigModulePath { get; set; }
    public List<string> Notes { get; } = new();
  }

  private string GetUiStatePath()
  {
    var root = _paths.GetAppDataRoot();
    return Path.Combine(root, "ui_state.json");
  }

  private void LoadCoverageOverlap()
  {
    var overlapItems = new List<OverlapItem>();
    if (!string.IsNullOrWhiteSpace(BundleRoot))
    {
      var path = Path.Combine(BundleRoot, BundlePaths.ReportsDirectory, "coverage_overlap.csv");
      if (File.Exists(path))
      {
        var lines = File.ReadLines(path).Skip(1);
        foreach (var line in lines)
        {
          if (string.IsNullOrWhiteSpace(line)) continue;
          var parts = ParseCsvLine(line);
          if (parts.Count < 5) continue;

          overlapItems.Add(new OverlapItem
          {
            SourcesKey = parts[0],
            SourceCount = SafeInt(parts[1]),
            ControlsCount = SafeInt(parts[2]),
            ClosedCount = SafeInt(parts[3]),
            OpenCount = SafeInt(parts[4])
          });
        }
      }
    }

    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
      OverlapItems.ReplaceAll(overlapItems);
    });
  }

  private static int SafeInt(string value)
  {
    return int.TryParse(value, out var i) ? i : 0;
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
