using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows.Data;
using STIGForge.App.Models;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Evidence;

namespace STIGForge.App;

public partial class MainViewModel : ObservableObject
{
  private readonly ContentPackImporter _importer;
  private readonly IContentPackRepository _packs;
  private readonly IProfileRepository _profiles;
  private readonly IControlRepository _controls;
  private readonly IOverlayRepository _overlays;
  private readonly BundleBuilder _builder;
  private readonly STIGForge.Apply.ApplyRunner _applyRunner;
  private readonly STIGForge.Export.EmassExporter _emassExporter;
  private readonly IPathBuilder _paths;
  private readonly EvidenceCollector _evidence;
  private ICollectionView? _manualView;
  private ICollectionView? _diffView;

  [ObservableProperty] private string statusText = "Ready.";
  [ObservableProperty] private string importHint = "Import the quarterly DISA zip(s). v1: parses XCCDF lightly.";
  [ObservableProperty] private string buildGateStatus = "";
  [ObservableProperty] private string applyStatus = "";
  [ObservableProperty] private string verifyStatus = "";
  [ObservableProperty] private string exportStatus = "";
  [ObservableProperty] private string bundleRoot = "";
  [ObservableProperty] private string evaluateStigRoot = "";
  [ObservableProperty] private string evaluateStigArgs = "";
  [ObservableProperty] private string scapCommandPath = "";
  [ObservableProperty] private string scapArgs = "";
  [ObservableProperty] private string scapLabel = "DISA SCAP";
  [ObservableProperty] private string lastOutputPath = "";
  [ObservableProperty] private string reportSummary = "";
  [ObservableProperty] private string verifySummary = "";
  [ObservableProperty] private string automationGatePath = "";
  [ObservableProperty] private bool isBusy;

  public bool ActionsEnabled => !IsBusy;

  public IList<ContentPack> ContentPacks { get; } = new List<ContentPack>();
  public IList<Profile> Profiles { get; } = new List<Profile>();
  public ObservableCollection<OverlayItem> OverlayItems { get; } = new();
  public ObservableCollection<string> RecentBundles { get; } = new();
  public ObservableCollection<OverlapItem> OverlapItems { get; } = new();
  public ObservableCollection<ManualControlItem> ManualControls { get; } = new();
  public ICollectionView ManualControlsView => _manualView ??= CollectionViewSource.GetDefaultView(ManualControls);
  public ObservableCollection<DiffItem> DiffItems { get; } = new();
  public ICollectionView DiffItemsView => _diffView ??= CollectionViewSource.GetDefaultView(DiffItems);

  [ObservableProperty] private ContentPack? selectedPack;
  [ObservableProperty] private Profile? selectedProfile;
  [ObservableProperty] private string profileName = "";
  [ObservableProperty] private string profileMode = "Safe";
  [ObservableProperty] private string profileClassification = "Classified";
  [ObservableProperty] private int profileGraceDays = 30;
  [ObservableProperty] private bool profileAutoNa = true;
  [ObservableProperty] private string profileNaComment = "Not applicable: unclassified-only control; system is classified.";
  [ObservableProperty] private string packDetailName = "";
  [ObservableProperty] private string packDetailId = "";
  [ObservableProperty] private string packDetailReleaseDate = "";
  [ObservableProperty] private string packDetailImportedAt = "";
  [ObservableProperty] private string packDetailSource = "";
  [ObservableProperty] private string packDetailHash = "";
  [ObservableProperty] private string packDetailControls = "";
  [ObservableProperty] private string packDetailRoot = "";
  [ObservableProperty] private string evidenceRuleId = "";
  [ObservableProperty] private string evidenceType = "Command";
  [ObservableProperty] private string evidenceText = "";
  [ObservableProperty] private string evidenceFilePath = "";
  [ObservableProperty] private string evidenceStatus = "";
  [ObservableProperty] private string selectedRecentBundle = "";
  [ObservableProperty] private ManualControlItem? selectedManualControl;
  [ObservableProperty] private string manualStatus = "Open";
  [ObservableProperty] private string manualReason = "";
  [ObservableProperty] private string manualComment = "";
  [ObservableProperty] private string manualFilterText = "";
  [ObservableProperty] private string manualStatusFilter = "All";
  [ObservableProperty] private string manualSummary = "";
  [ObservableProperty] private bool showManualOnly;

  public IReadOnlyList<string> EvidenceTypes { get; } = new[]
  {
    "Command",
    "File",
    "Registry",
    "PolicyExport",
    "Screenshot",
    "Other"
  };

  public IReadOnlyList<string> ManualStatuses { get; } = new[]
  {
    "Pass",
    "Fail",
    "NotApplicable",
    "Open"
  };

  public IReadOnlyList<string> ManualStatusFilters { get; } = new[]
  {
    "All",
    "Pass",
    "Fail",
    "NotApplicable",
    "Open"
  };

  public MainViewModel(ContentPackImporter importer, IContentPackRepository packs, IProfileRepository profiles, IControlRepository controls, IOverlayRepository overlays, BundleBuilder builder, STIGForge.Apply.ApplyRunner applyRunner, STIGForge.Export.EmassExporter emassExporter, IPathBuilder paths, EvidenceCollector evidence)
  {
    _importer = importer;
    _packs = packs;
    _profiles = profiles;
    _controls = controls;
    _overlays = overlays;
    _builder = builder;
    _applyRunner = applyRunner;
    _emassExporter = emassExporter;
    _paths = paths;
    _evidence = evidence;
    _ = LoadAsync();
  }

  private async Task LoadAsync()
  {
    try
    {
      IsBusy = true;
      var list = await _packs.ListAsync(CancellationToken.None);
      ContentPacks.Clear();
      foreach (var p in list) ContentPacks.Add(p);
      OnPropertyChanged(nameof(ContentPacks));

      if (ContentPacks.Count > 0 && SelectedPack == null)
        SelectedPack = ContentPacks[0];

      var profiles = await _profiles.ListAsync(CancellationToken.None);
      Profiles.Clear();
      foreach (var p in profiles) Profiles.Add(p);
      OnPropertyChanged(nameof(Profiles));

      if (Profiles.Count > 0 && SelectedProfile == null)
        SelectedProfile = Profiles[0];

      if (SelectedProfile != null)
        LoadProfileFields(SelectedProfile);

      var overlays = await _overlays.ListAsync(CancellationToken.None);
      OverlayItems.Clear();
      foreach (var o in overlays) OverlayItems.Add(new OverlayItem(o));
      OnPropertyChanged(nameof(OverlayItems));

      if (SelectedProfile != null)
        ApplyOverlaySelection(SelectedProfile);

      LoadUiState();
      LoadCoverageOverlap();
      LoadManualControls();
      ConfigureManualView();
      ConfigureDiffView();
    }
    catch (Exception ex)
    {
      StatusText = "Load failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

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
  private async Task ApplyRunAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        ApplyStatus = "Select a bundle first.";
        return;
      }

      var script = Path.Combine(BundleRoot, "Apply", "RunApply.ps1");
      if (!File.Exists(script))
      {
        ApplyStatus = "RunApply.ps1 not found in bundle.";
        return;
      }

      ApplyStatus = "Running apply...";
      var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = BundleRoot,
        ScriptPath = script,
        ScriptArgs = "-BundleRoot \"" + BundleRoot + "\""
      }, CancellationToken.None);

      ApplyStatus = "Apply complete: " + result.LogPath;
      LastOutputPath = result.LogPath;
    }
    catch (Exception ex)
    {
      ApplyStatus = "Apply failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task VerifyRunAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        VerifyStatus = "Select a bundle first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath))
      {
        VerifyStatus = "Provide Evaluate-STIG root or SCAP command path.";
        return;
      }

      var verifyRoot = Path.Combine(BundleRoot, "Verify");
      Directory.CreateDirectory(verifyRoot);

      if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
      {
        var evalRunner = new STIGForge.Verify.EvaluateStigRunner();
        var evalResult = evalRunner.Run(EvaluateStigRoot, EvaluateStigArgs ?? string.Empty, EvaluateStigRoot);
        var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
        Directory.CreateDirectory(evalOutput);
        var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(evalOutput, "Evaluate-STIG");
        report.StartedAt = evalResult.StartedAt;
        report.FinishedAt = evalResult.FinishedAt;
        STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(evalOutput, "consolidated-results.json"), report);
        STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(evalOutput, "consolidated-results.csv"), report.Results);
      }

      if (!string.IsNullOrWhiteSpace(ScapCommandPath))
      {
        var scapRunner = new STIGForge.Verify.ScapRunner();
        var scapResult = scapRunner.Run(ScapCommandPath, ScapArgs ?? string.Empty, null);
        var scapOutput = Path.Combine(verifyRoot, "SCAP");
        Directory.CreateDirectory(scapOutput);
        var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? "SCAP" : ScapLabel;
        var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(scapOutput, toolName);
        report.StartedAt = scapResult.StartedAt;
        report.FinishedAt = scapResult.FinishedAt;
        STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(scapOutput, "consolidated-results.json"), report);
        STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(scapOutput, "consolidated-results.csv"), report.Results);
      }

      VerifyStatus = "Verify complete.";
      LastOutputPath = Path.Combine(BundleRoot, "Verify");
      ReportSummary = BuildReportSummary(BundleRoot);
      VerifySummary = BuildReportSummary(BundleRoot);
      LoadCoverageOverlap();
      await Task.CompletedTask;
    }
    catch (Exception ex)
    {
      VerifyStatus = "Verify failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ExportEmassAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        ExportStatus = "Select a bundle first.";
        return;
      }

      var result = await _emassExporter.ExportAsync(new STIGForge.Export.ExportRequest
      {
        BundleRoot = BundleRoot
      }, CancellationToken.None);

      ExportStatus = "Exported: " + result.OutputRoot;
      LastOutputPath = result.OutputRoot;
      ReportSummary = BuildReportSummary(BundleRoot);
      LoadCoverageOverlap();
    }
    catch (Exception ex)
    {
      ExportStatus = "Export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private void BrowseEvidenceFile()
  {
    var ofd = new OpenFileDialog
    {
      Filter = "All Files (*.*)|*.*",
      Title = "Select evidence file"
    };

    if (ofd.ShowDialog() != true) return;
    EvidenceFilePath = ofd.FileName;
  }

  [RelayCommand]
  private void SaveEvidence()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(EvidenceRuleId))
      {
        EvidenceStatus = "RuleId is required.";
        return;
      }

      var type = ParseEvidenceType(EvidenceType);
      var request = new EvidenceWriteRequest
      {
        BundleRoot = BundleRoot,
        RuleId = EvidenceRuleId,
        Type = type,
        ContentText = string.IsNullOrWhiteSpace(EvidenceText) ? null : EvidenceText,
        SourceFilePath = string.IsNullOrWhiteSpace(EvidenceFilePath) ? null : EvidenceFilePath
      };

      var result = _evidence.WriteEvidence(request);
      EvidenceStatus = "Saved: " + result.EvidencePath;
      LastOutputPath = result.EvidencePath;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Evidence save failed: " + ex.Message;
    }
  }

  [RelayCommand]
  private void SaveManualAnswer()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        EvidenceStatus = "Select a bundle first.";
        return;
      }

      if (SelectedManualControl == null)
      {
        EvidenceStatus = "Select a manual control.";
        return;
      }

      var file = LoadAnswerFile();
      if (SelectedProfile != null) file.ProfileId = SelectedProfile.ProfileId;
      if (SelectedPack != null) file.PackId = SelectedPack.PackId;
      if (file.CreatedAt == default) file.CreatedAt = DateTimeOffset.Now;
      var answer = FindAnswer(file, SelectedManualControl.Control) ?? new ManualAnswer
      {
        RuleId = SelectedManualControl.Control.ExternalIds.RuleId,
        VulnId = SelectedManualControl.Control.ExternalIds.VulnId
      };

      answer.Status = ManualStatus;
      answer.Reason = string.IsNullOrWhiteSpace(ManualReason) ? null : ManualReason;
      answer.Comment = string.IsNullOrWhiteSpace(ManualComment) ? null : ManualComment;
      answer.UpdatedAt = DateTimeOffset.Now;

      if (!file.Answers.Contains(answer))
        file.Answers.Add(answer);

      var path = Path.Combine(BundleRoot, "Manual", "answers.json");
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json, Encoding.UTF8);

      SelectedManualControl.Status = ManualStatus;
      SelectedManualControl.Reason = ManualReason;
      SelectedManualControl.Comment = ManualComment;
      OnPropertyChanged(nameof(ManualControls));
      UpdateManualSummary();

      EvidenceStatus = "Answer saved.";
      LastOutputPath = path;
    }
    catch (Exception ex)
    {
      EvidenceStatus = "Save failed: " + ex.Message;
    }
  }

  partial void OnSelectedManualControlChanged(ManualControlItem? value)
  {
    if (value == null) return;
    ManualStatus = value.Status;
    ManualReason = value.Reason ?? string.Empty;
    ManualComment = value.Comment ?? string.Empty;
  }

  partial void OnManualFilterTextChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnManualStatusFilterChanged(string value)
  {
    RefreshManualView();
  }

  partial void OnShowManualOnlyChanged(bool value)
  {
    RefreshDiffView();
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

  [RelayCommand]
  private void LoadDiff()
  {
    DiffItems.Clear();
    var pack = SelectedPack;
    if (pack == null)
    {
      StatusText = "Select a pack first.";
      return;
    }

    var path = FindLatestDiffJson(pack);
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      StatusText = "No diff report found for selected pack.";
      return;
    }

    var json = File.ReadAllText(path);
    var diff = JsonSerializer.Deserialize<ReleaseDiff>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (diff == null)
    {
      StatusText = "Diff report could not be parsed.";
      return;
    }

    foreach (var item in diff.Items)
    {
      DiffItems.Add(new DiffItem
      {
        RuleId = item.RuleId ?? string.Empty,
        VulnId = item.VulnId ?? string.Empty,
        Title = item.Title ?? string.Empty,
        Kind = item.Kind.ToString(),
        IsManual = item.IsManual,
        ManualChanged = item.ManualChanged,
        ChangedFields = item.ChangedFields == null ? string.Empty : string.Join(";", item.ChangedFields)
      });
    }

    RefreshDiffView();
    StatusText = "Loaded diff: " + Path.GetFileName(path);
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

  private string? FindLatestDiffJson(ContentPack pack)
  {
    var root = _paths.GetPackRoot(pack.PackId);
    var diffRoot = Path.Combine(root, "Reports", "Diff");
    if (!Directory.Exists(diffRoot)) return null;

    var known = Path.Combine(diffRoot, "release_diff.json");
    if (File.Exists(known)) return known;

    var files = Directory.GetFiles(diffRoot, "*.json");
    if (files.Length == 0) return null;

    return files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
  }

  private void LoadManualControls()
  {
    ManualControls.Clear();
    if (string.IsNullOrWhiteSpace(BundleRoot)) return;

    var controlsPath = Path.Combine(BundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath)) return;

    var json = File.ReadAllText(controlsPath);
    var controls = JsonSerializer.Deserialize<List<ControlRecord>>(json,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ControlRecord>();

    var answers = LoadAnswerFile();

    foreach (var c in controls.Where(c => c.IsManual))
    {
      var item = new ManualControlItem(c);
      var ans = FindAnswer(answers, c);
      if (ans != null)
      {
        item.Status = ans.Status;
        item.Reason = ans.Reason;
        item.Comment = ans.Comment;
      }
      ManualControls.Add(item);
    }
  }

  private void ConfigureDiffView()
  {
    var view = DiffItemsView;
    view.SortDescriptions.Clear();
    view.SortDescriptions.Add(new SortDescription(nameof(DiffItem.ManualChanged), ListSortDirection.Descending));
    view.SortDescriptions.Add(new SortDescription(nameof(DiffItem.RuleId), ListSortDirection.Ascending));
    view.Filter = item =>
    {
      if (!ShowManualOnly) return true;
      return item is DiffItem diff && diff.IsManual;
    };
  }

  private void RefreshDiffView()
  {
    DiffItemsView.Refresh();
  }

  private AnswerFile LoadAnswerFile()
  {
    var path = Path.Combine(BundleRoot, "Manual", "answers.json");
    if (!File.Exists(path)) return new AnswerFile();

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<AnswerFile>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    }) ?? new AnswerFile();
  }

  private static ManualAnswer? FindAnswer(AnswerFile file, ControlRecord control)
  {
    return file.Answers.FirstOrDefault(a =>
      (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, control.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase)) ||
      (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, control.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase)));
  }

  private static int SafeInt(string value)
  {
    return int.TryParse(value, out var i) ? i : 0;
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

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }

  [RelayCommand]
  private void OpenOverlayEditor()
  {
    var win = new OverlayEditorWindow();
    win.ShowDialog();
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

  public sealed class OverlayItem : ObservableObject
  {
    public OverlayItem(Overlay overlay)
    {
      Overlay = overlay;
    }

    public Overlay Overlay { get; }

    private bool _isSelected;
    public bool IsSelected
    {
      get => _isSelected;
      set => SetProperty(ref _isSelected, value);
    }
  }

  public sealed class ManualControlItem : ObservableObject
  {
    public ManualControlItem(ControlRecord control)
    {
      Control = control;
      _status = "Open";
    }

    public ControlRecord Control { get; }

    private string _status;
    public string Status
    {
      get => _status;
      set => SetProperty(ref _status, value);
    }

    private string? _reason;
    public string? Reason
    {
      get => _reason;
      set => SetProperty(ref _reason, value);
    }

    private string? _comment;
    public string? Comment
    {
      get => _comment;
      set => SetProperty(ref _comment, value);
    }
  }

  public sealed class OverlapItem
  {
    public string SourcesKey { get; set; } = string.Empty;
    public int SourceCount { get; set; }
    public int ControlsCount { get; set; }
    public int ClosedCount { get; set; }
    public int OpenCount { get; set; }
  }

  private sealed class UiState
  {
    public string? BundleRoot { get; set; }
    public string? EvaluateStigRoot { get; set; }
    public string? EvaluateStigArgs { get; set; }
    public string? ScapCommandPath { get; set; }
    public string? ScapArgs { get; set; }
    public string? ScapLabel { get; set; }
    public List<string>? RecentBundles { get; set; }
  }

  private static string BuildReportSummary(string bundleRoot)
  {
    var reportPath = Path.Combine(bundleRoot, "Verify", "consolidated-results.json");
    if (!File.Exists(reportPath)) return "";

    var report = STIGForge.Verify.VerifyReportReader.LoadFromJson(reportPath);
    var total = report.Results.Count;
    var open = report.Results.Count(r => r.Status != null && r.Status.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0);
    var closed = total - open;
    return "Summary: total=" + total + " closed=" + closed + " open=" + open;
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
    DiffItems.Clear();
    RefreshDiffView();
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

  private static HardeningMode ParseMode(string value)
  {
    return Enum.TryParse<HardeningMode>(value, true, out var m) ? m : HardeningMode.Safe;
  }

  private static ClassificationMode ParseClassification(string value)
  {
    return Enum.TryParse<ClassificationMode>(value, true, out var m) ? m : ClassificationMode.Classified;
  }

  private static EvidenceArtifactType ParseEvidenceType(string value)
  {
    return Enum.TryParse<EvidenceArtifactType>(value, true, out var t) ? t : EvidenceArtifactType.Other;
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

  private static void WritePowerStigMapCsv(string path, IReadOnlyList<ControlRecord> controls)
  {
    var sb = new StringBuilder(controls.Count * 40 + 128);
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
    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
}
