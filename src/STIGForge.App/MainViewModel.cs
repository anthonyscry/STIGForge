using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using STIGForge.Build;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Verify;

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
  private ICollectionView? _manualView;

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
  [ObservableProperty] private bool applySkipSnapshot;
  [ObservableProperty] private bool breakGlassAcknowledged;
  [ObservableProperty] private string breakGlassReason = "";

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

  public IList<ContentPack> ContentPacks { get; } = new List<ContentPack>();
  public IList<Profile> Profiles { get; } = new List<Profile>();
  public ObservableCollection<OverlayItem> OverlayItems { get; } = new();
  public ObservableCollection<string> RecentBundles { get; } = new();
  public ObservableCollection<OverlapItem> OverlapItems { get; } = new();
  public ObservableCollection<ManualControlItem> ManualControls { get; } = new();
  public ICollectionView ManualControlsView => _manualView ??= CollectionViewSource.GetDefaultView(ManualControls);

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
}
