using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;

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
        ContentPacks.ToList());

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
    DashHasBundle = !string.IsNullOrWhiteSpace(BundleRoot) && Directory.Exists(BundleRoot);
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
      DashLastVerify = "";
      DashLastExport = "";
      return;
    }

    DashBundleLabel = Path.GetFileName(BundleRoot);
    try
    {
      var summary = _bundleMissionSummary.LoadSummary(BundleRoot);
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
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine("Dashboard summary load failed: " + ex.Message);
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
    }

    DashLastVerify = "";
    var verifyDir = Path.Combine(BundleRoot, "Verify");
    if (Directory.Exists(verifyDir))
    {
      try
      {
        var lastVerify = DateTimeOffset.MinValue;
        foreach (var reportFile in Directory.GetFiles(verifyDir, "consolidated-results.json", SearchOption.AllDirectories))
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
        System.Diagnostics.Debug.WriteLine("Dashboard verify timestamp load failed: " + ex.Message);
      }
    }

    // Check for eMASS export
    var emassDir = Path.Combine(BundleRoot, "Export");
    if (Directory.Exists(emassDir))
    {
      try
      {
        var latest = Directory.GetDirectories(emassDir).OrderByDescending(d => d).FirstOrDefault();
        DashLastExport = latest != null ? Path.GetFileName(latest) : "";
      }
      catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Dashboard export load failed: " + ex.Message); DashLastExport = ""; }
    }
    else
    {
      DashLastExport = "";
    }
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
  private void OpenBundleFolder()
  {
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;
    var path = Directory.Exists(BundleRoot) ? BundleRoot : Path.GetDirectoryName(BundleRoot);
    if (string.IsNullOrWhiteSpace(path)) return;
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
      FileName = path,
      UseShellExecute = true
    });
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
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = path,
        UseShellExecute = true
      });
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
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
      {
        FileName = path,
        UseShellExecute = true
      });
    }
  }

  [RelayCommand]
  private void UseRecentBundle()
  {
    if (string.IsNullOrWhiteSpace(SelectedRecentBundle)) return;
    BundleRoot = SelectedRecentBundle;
    LoadCoverageOverlap();
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

  private void LoadUiState()
  {
    var path = GetUiStatePath();
    if (!File.Exists(path)) return;

    try
    {
      var json = File.ReadAllText(path);
      var state = JsonSerializer.Deserialize<UiState>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      if (state == null) return;
      BundleRoot = state.BundleRoot ?? BundleRoot;
      EvaluateStigRoot = state.EvaluateStigRoot ?? EvaluateStigRoot;
      EvaluateStigArgs = state.EvaluateStigArgs ?? EvaluateStigArgs;
      ScapCommandPath = state.ScapCommandPath ?? ScapCommandPath;
      ScapArgs = state.ScapArgs ?? ScapArgs;
      ScapLabel = state.ScapLabel ?? ScapLabel;

      RecentBundles.Clear();
      if (state.RecentBundles != null)
      {
        foreach (var b in state.RecentBundles)
          RecentBundles.Add(b);
      }
    }
    catch
    {
    }
  }

  private void AddRecentBundle(string bundlePath)
  {
    if (string.IsNullOrWhiteSpace(bundlePath)) return;

    var existing = RecentBundles.FirstOrDefault(b =>
      string.Equals(b, bundlePath, StringComparison.OrdinalIgnoreCase));
    if (existing != null) RecentBundles.Remove(existing);

    RecentBundles.Insert(0, bundlePath);
    while (RecentBundles.Count > 5) RecentBundles.RemoveAt(RecentBundles.Count - 1);

    SaveUiState();
  }

  private void SaveUiState()
  {
    try
    {
      var path = GetUiStatePath();
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);

      var state = new UiState
      {
        BundleRoot = BundleRoot,
        EvaluateStigRoot = EvaluateStigRoot,
        EvaluateStigArgs = EvaluateStigArgs,
        ScapCommandPath = ScapCommandPath,
        ScapArgs = ScapArgs,
        ScapLabel = ScapLabel,
        RecentBundles = RecentBundles.ToList()
      };

      var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json);
    }
    catch
    {
    }
  }

  private string GetUiStatePath()
  {
    var root = _paths.GetAppDataRoot();
    return Path.Combine(root, "ui_state.json");
  }

  private void LoadCoverageOverlap()
  {
    OverlapItems.Clear();
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;
    var path = Path.Combine(BundleRoot, "Reports", "coverage_overlap.csv");
    if (!File.Exists(path)) return;

    var lines = File.ReadAllLines(path).Skip(1);
    foreach (var line in lines)
    {
      if (string.IsNullOrWhiteSpace(line)) continue;
      var parts = ParseCsvLine(line);
      if (parts.Length < 5) continue;

      OverlapItems.Add(new OverlapItem
      {
        SourcesKey = parts[0],
        SourceCount = SafeInt(parts[1]),
        ControlsCount = SafeInt(parts[2]),
        ClosedCount = SafeInt(parts[3]),
        OpenCount = SafeInt(parts[4])
      });
    }
  }

  private static int SafeInt(string value)
  {
    return int.TryParse(value, out var i) ? i : 0;
  }

  private static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new System.Text.StringBuilder();
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

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
