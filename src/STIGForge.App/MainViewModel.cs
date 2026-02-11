using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Data;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Verify;

namespace STIGForge.App;

public partial class MainViewModel : ObservableObject, IDisposable
{
  private readonly ContentPackImporter _importer;
  private readonly IContentPackRepository _packs;
  private readonly IProfileRepository _profiles;
  private readonly IControlRepository _controls;
  private readonly IOverlayRepository _overlays;
  private readonly BundleBuilder _builder;
  private readonly STIGForge.Apply.ApplyRunner _applyRunner;
  private readonly IVerificationWorkflowService _verificationWorkflow;
  private readonly STIGForge.Export.EmassExporter _emassExporter;
  private readonly IPathBuilder _paths;
  private readonly EvidenceCollector _evidence;
  private readonly ManualAnswerService _manualAnswerService;
  private readonly IBundleMissionSummaryService _bundleMissionSummary;
  private readonly VerificationArtifactAggregationService _artifactAggregation;
  private readonly IAuditTrailService? _audit;
  private readonly STIGForge.Infrastructure.System.ScheduledTaskService? _scheduledTaskService;
  private readonly STIGForge.Infrastructure.System.FleetService? _fleetService;
  private readonly CancellationTokenSource _cts = new();
  private bool _disposed;
  private ICollectionView? _manualView;

  [ObservableProperty] private string statusText = "Ready.";
  [ObservableProperty] private bool isDarkTheme = true;
  public string ThemeToggleLabel => IsDarkTheme ? "\u2600\uFE0F" : "\uD83C\uDF19";

  partial void OnIsDarkThemeChanged(bool value)
  {
    OnPropertyChanged(nameof(ThemeToggleLabel));
    ApplyTheme(value);
  }

  public static void ApplyTheme(bool dark)
  {
    var app = System.Windows.Application.Current;
    if (app == null) return;
    var dict = app.Resources.MergedDictionaries;
    dict.Clear();
    var uri = dark
      ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
      : new Uri("Themes/LightTheme.xaml", UriKind.Relative);
    dict.Add(new System.Windows.ResourceDictionary { Source = uri });

    // Apply dark/light title bar via DWM (Windows 10 1809+ / Windows 11)
    if (app.MainWindow != null)
      SetDarkTitleBar(app.MainWindow, dark);
  }

  [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
  private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

  public static void SetDarkTitleBar(System.Windows.Window window, bool dark)
  {
    try
    {
      var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
      if (hwnd == IntPtr.Zero) return;
      int useDark = dark ? 1 : 0;
      // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
      DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
    }
    catch { /* Silently ignore on older Windows versions */ }
  }
  [ObservableProperty] private string importHint = "Import single ZIPs, consolidated bundles, or GPO/LGPO ZIPs (ADMX auto-import enabled).";
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
  [ObservableProperty] private bool applySkipSnapshot;
  [ObservableProperty] private bool breakGlassAcknowledged;
  [ObservableProperty] private string breakGlassReason = "";
  [ObservableProperty] private string powerStigModulePath = "";
  [ObservableProperty] private string powerStigDataFile = "";
  [ObservableProperty] private string powerStigOutputPath = "";
  [ObservableProperty] private bool powerStigVerbose;
  [ObservableProperty] private string localToolkitRoot = "STIG_SCAP";
  [ObservableProperty] private string toolkitActivationStatus = "Toolkit activation pending.";
  [ObservableProperty] private string importLibraryStatus = "Library not loaded.";
  [ObservableProperty] private string machineApplicabilityStatus = "";
  [ObservableProperty] private string selectedContentSummary = "No content selected. Import or select packs above.";
  [ObservableProperty] private string selectedMissionPreset = "Workstation/VM Compliance";
  [ObservableProperty] private string missionPresetGuidance = "Use snapshot-protected apply for managed endpoints.";
  [ObservableProperty] private bool simpleBuildBeforeRun = true;
  [ObservableProperty] private string guidedNextAction = "Run orchestration to generate next action.";

  // Dashboard
  [ObservableProperty] private string dashBundleLabel = "(no bundle selected)";
  [ObservableProperty] private string dashPackLabel = "";
  [ObservableProperty] private string dashProfileLabel = "";
  [ObservableProperty] private int dashTotalControls;
  [ObservableProperty] private int dashAutoControls;
  [ObservableProperty] private int dashManualControls;
  [ObservableProperty] private int dashVerifyClosed;
  [ObservableProperty] private int dashVerifyOpen;
  [ObservableProperty] private int dashVerifyTotal;
  [ObservableProperty] private string dashVerifyPercent = "—";
  [ObservableProperty] private int dashManualPass;
  [ObservableProperty] private int dashManualFail;
  [ObservableProperty] private int dashManualNa;
  [ObservableProperty] private int dashManualOpen;
  [ObservableProperty] private string dashManualPercent = "—";
  [ObservableProperty] private string dashMissionSeverity = "";
  [ObservableProperty] private string dashRecoveryGuidance = "";
  [ObservableProperty] private bool dashHasBundle;
  [ObservableProperty] private string dashLastVerify = "";
  [ObservableProperty] private string dashLastExport = "";

  // Orchestrate
  [ObservableProperty] private bool orchRunApply = true;
  [ObservableProperty] private bool orchRunVerify = true;
  [ObservableProperty] private bool orchRunExport = true;
  [ObservableProperty] private string orchStatus = "";
  [ObservableProperty] private string orchLog = "";

  public bool ActionsEnabled => !IsBusy;

  public IReadOnlyList<string> MissionPresets { get; } = new[]
  {
    "Workstation/VM Compliance",
    "Golden VM Image",
    "SCCM PXE Image"
  };

  public IList<ContentPack> ContentPacks { get; } = new List<ContentPack>();
  public IList<Profile> Profiles { get; } = new List<Profile>();
  public ObservableCollection<OverlayItem> OverlayItems { get; } = new();
  public ObservableCollection<string> RecentBundles { get; } = new();
  public ObservableCollection<OverlapItem> OverlapItems { get; } = new();
  public ObservableCollection<ManualControlItem> ManualControls { get; } = new();
   public ObservableCollection<ImportedLibraryItem> StigLibraryItems { get; } = new();
   public ObservableCollection<ImportedLibraryItem> ScapLibraryItems { get; } = new();
   public ObservableCollection<ImportedLibraryItem> OtherLibraryItems { get; } = new();
   public ObservableCollection<ImportedLibraryItem> AllLibraryItems { get; } = new();
    public HashSet<string> ApplicablePackIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    private ICollectionView? _filteredContentLibrary;
   public ICollectionView FilteredContentLibrary => _filteredContentLibrary ??= CreateFilteredLibraryView();
   public ICollectionView ManualControlsView => _manualView ??= CollectionViewSource.GetDefaultView(ManualControls);

  [ObservableProperty] private ContentPack? selectedPack;
  [ObservableProperty] private Profile? selectedProfile;
  [ObservableProperty] private string profileName = "";
  [ObservableProperty] private string profileMode = "Safe";
  [ObservableProperty] private string profileClassification = "Classified";
  [ObservableProperty] private int profileGraceDays = 30;
  [ObservableProperty] private bool profileAutoNa = true;
  [ObservableProperty] private string profileNaComment = "Not applicable: control is out of scope for the current classification.";

  partial void OnProfileClassificationChanged(string value)
  {
    ProfileNaComment = value switch
    {
      "Unclassified" => "Not applicable: unclassified-only control; system is classified.",
      "Classified"   => "Not applicable: classified-only control; system is unclassified.",
      _              => "Not applicable: control is out of scope for the current classification."
    };
  }
  [ObservableProperty] private string packDetailName = "";
  [ObservableProperty] private string packDetailId = "";
  [ObservableProperty] private string packDetailReleaseDate = "";
  [ObservableProperty] private string packDetailImportedAt = "";
  [ObservableProperty] private string packDetailSource = "";
  [ObservableProperty] private string packDetailHash = "";
  [ObservableProperty] private string packDetailControls = "";
   [ObservableProperty] private string packDetailRoot = "";
   [ObservableProperty] private string packDetailFormat = "";
   [ObservableProperty] private string contentLibraryFilter = "All";
   [ObservableProperty] private string contentSearchText = "";
   [ObservableProperty] private ImportedLibraryItem? selectedLibraryItem;
   public List<ImportedLibraryItem> SelectedLibraryItems { get; } = new();
   [ObservableProperty] private string evidenceRuleId = "";
  [ObservableProperty] private string evidenceType = "Command";
  [ObservableProperty] private string evidenceText = "";
  [ObservableProperty] private string evidenceFilePath = "";
  [ObservableProperty] private string evidenceStatus = "";
  [ObservableProperty] private string selectedRecentBundle = "";
  [ObservableProperty] private ManualControlItem? selectedManualControl;
  [ObservableProperty] private ImportedLibraryItem? selectedStigLibraryItem;
  [ObservableProperty] private ImportedLibraryItem? selectedScapLibraryItem;
  [ObservableProperty] private ImportedLibraryItem? selectedOtherLibraryItem;
  [ObservableProperty] private string manualStatus = "Open";
  [ObservableProperty] private string manualReason = "";
  [ObservableProperty] private string manualComment = "";
  [ObservableProperty] private string manualFilterText = "";
  [ObservableProperty] private string manualStatusFilter = "All";
  [ObservableProperty] private string manualSummary = "";

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

   // Content Library Filter Properties
   public bool IsFilterAll { get => ContentLibraryFilter == "All"; set { if (value) ContentLibraryFilter = "All"; } }
   public bool IsFilterStig { get => ContentLibraryFilter == "STIG"; set { if (value) ContentLibraryFilter = "STIG"; } }
   public bool IsFilterScap { get => ContentLibraryFilter == "SCAP"; set { if (value) ContentLibraryFilter = "SCAP"; } }
   public bool IsFilterGpo { get => ContentLibraryFilter == "GPO"; set { if (value) ContentLibraryFilter = "GPO"; } }

   public MainViewModel(ContentPackImporter importer, IContentPackRepository packs, IProfileRepository profiles, IControlRepository controls, IOverlayRepository overlays, BundleBuilder builder, STIGForge.Apply.ApplyRunner applyRunner, IVerificationWorkflowService verificationWorkflow, STIGForge.Export.EmassExporter emassExporter, IPathBuilder paths, EvidenceCollector evidence, IBundleMissionSummaryService bundleMissionSummary, VerificationArtifactAggregationService artifactAggregation, IAuditTrailService? audit = null, STIGForge.Infrastructure.System.ScheduledTaskService? scheduledTaskService = null, STIGForge.Infrastructure.System.FleetService? fleetService = null)
  {
    _importer = importer;
    _packs = packs;
    _profiles = profiles;
    _controls = controls;
    _overlays = overlays;
    _builder = builder;
    _applyRunner = applyRunner;
    _verificationWorkflow = verificationWorkflow;
    _emassExporter = emassExporter;
    _paths = paths;
    _evidence = evidence;
    _bundleMissionSummary = bundleMissionSummary;
    _artifactAggregation = artifactAggregation;
    _audit = audit;
    _manualAnswerService = new ManualAnswerService(_audit);
    _scheduledTaskService = scheduledTaskService;
    _fleetService = fleetService;
    _ = LoadAsync().ContinueWith(t =>
    {
      if (t.IsFaulted)
        System.Diagnostics.Trace.TraceError("LoadAsync failed: " + t.Exception?.Flatten().Message);
    }, System.Threading.Tasks.TaskScheduler.Default);
  }

  private async Task LoadAsync()
  {
    try
    {
      IsBusy = true;
      var list = await _packs.ListAsync(CancellationToken.None);
      var profiles = await _profiles.ListAsync(CancellationToken.None);
      var overlays = await _overlays.ListAsync(CancellationToken.None);

      System.Windows.Application.Current.Dispatcher.Invoke(() =>
      {
        ContentPacks.Clear();
        foreach (var p in list) ContentPacks.Add(p);
        OnPropertyChanged(nameof(ContentPacks));
        RefreshImportLibrary();

      if (SelectedPack == null)
      {
        if (ContentPacks.Count > 0)
          SelectedPack = ContentPacks[0];
        else
          SelectedPack = null;
      }

        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);
        OnPropertyChanged(nameof(Profiles));

      if (SelectedProfile == null)
      {
        if (Profiles.Count > 0)
          SelectedProfile = Profiles[0];
        else
          SelectedProfile = null;
      }

        if (SelectedProfile != null)
          LoadProfileFields(SelectedProfile);

        OverlayItems.Clear();
        foreach (var o in overlays) OverlayItems.Add(new OverlayItem(o));
        OnPropertyChanged(nameof(OverlayItems));

        if (SelectedProfile != null)
          ApplyOverlaySelection(SelectedProfile);
      });

      LoadUiState();
      AutoFillHostnameDefaults();
      LoadFleetInventory();

      // Apply default preset args when EvaluateStigArgs is empty
      if (string.IsNullOrWhiteSpace(EvaluateStigArgs) && EvalStigPresetArgs.TryGetValue(SelectedEvalStigPreset, out var defaultArgs))
        EvaluateStigArgs = defaultArgs;

      if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath))
        await TryActivateToolkitAsync(userInitiated: false, _cts.Token);
      LoadCoverageOverlap();
      LoadManualControls();
      ConfigureManualView();
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

  // ── Nested Types ──────────────────────────────────────────────────────────

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

  public sealed class ImportedLibraryItem
  {
    public string PackId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; }
    public string ImportedAtLabel { get; set; } = string.Empty;
    public string ReleaseDateLabel { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
  }

  private sealed class UiState
  {
    public string? BundleRoot { get; set; }
    public string? EvaluateStigRoot { get; set; }
    public string? EvaluateStigArgs { get; set; }
    public string? ScapCommandPath { get; set; }
    public string? ScapArgs { get; set; }
    public string? ScapLabel { get; set; }
    public string? PowerStigModulePath { get; set; }
    public string? PowerStigDataFile { get; set; }
    public string? PowerStigOutputPath { get; set; }
    public bool PowerStigVerbose { get; set; }
    public string? LocalToolkitRoot { get; set; }
    public bool ApplySkipSnapshot { get; set; }
    public bool BreakGlassAcknowledged { get; set; }
    public string? BreakGlassReason { get; set; }
    public string? SelectedMissionPreset { get; set; }
    public bool SimpleBuildBeforeRun { get; set; }
    public string? EvaluateStigPreset { get; set; }
    public List<string>? RecentBundles { get; set; }
    public bool IsDarkTheme { get; set; } = true;
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    _cts.Cancel();
    _cts.Dispose();
  }
}
