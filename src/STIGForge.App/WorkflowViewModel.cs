using System.Collections.ObjectModel;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Apply;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.App;

public partial class WorkflowViewModel : ObservableObject
{
    private const int DefaultSccTimeoutSeconds = 300;
    private const int MinSccTimeoutSeconds = 30;
    private const int MaxSccTimeoutSeconds = 3600;

    private static readonly string[] SccOutputSwitches =
    [
        "--results-dir",
        "--results-directory",
        "--output",
        "--output-dir",
        "--output-directory",
        "-u",
        "/u",
        "-o",
        "/o"
    ];

    private readonly ImportInboxScanner? _importScanner;
    private readonly IVerificationWorkflowService? _verifyService;
    private readonly Func<ApplyRequest, CancellationToken, Task<ApplyResult>>? _runApply;
    private readonly Func<string?> _autoScanRootResolver;
    private readonly Func<bool> _isElevatedResolver;

    public WorkflowViewModel(
        ImportInboxScanner? importScanner = null,
        IVerificationWorkflowService? verifyService = null,
        Func<ApplyRequest, CancellationToken, Task<ApplyResult>>? runApply = null,
        Func<string?>? autoScanRootResolver = null,
        Func<bool>? isElevatedResolver = null)
    {
        _importScanner = importScanner;
        _verifyService = verifyService;
        _runApply = runApply;
        _autoScanRootResolver = autoScanRootResolver ?? ResolveAutoScanRoot;
        _isElevatedResolver = isElevatedResolver ?? IsCurrentProcessElevated;
        LoadSettings();
    }

    private static bool IsCurrentProcessElevated()
    {
        if (!OperatingSystem.IsWindows())
            return true;

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private WorkflowStep _currentStep = WorkflowStep.Setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _importFolderPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _evaluateStigToolPath = string.Empty;

    [ObservableProperty]
    private string _evaluateAfPath = string.Empty;

    [ObservableProperty]
    private string _evaluateSelectStig = string.Empty;

    [ObservableProperty]
    private string _evaluateAdditionalArgs = string.Empty;

    [ObservableProperty]
    private string _sccArguments = string.Empty;

    [ObservableProperty]
    private string _sccWorkingDirectory = string.Empty;

    [ObservableProperty]
    private int _sccTimeoutSeconds = DefaultSccTimeoutSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _sccToolPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _machineTarget = "localhost";

    [ObservableProperty]
    private bool _requireElevationForScan = true;

    [ObservableProperty]
    private string _missionJsonPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestSccHeadlessCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private long _lastImportDurationMs;

    [ObservableProperty]
    private long _lastScanDurationMs;

    [ObservableProperty]
    private long _lastHardenDurationMs;

    [ObservableProperty]
    private long _lastVerifyDurationMs;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    private int _importedItemsCount;

    [ObservableProperty]
    private bool _isImportedLibraryExpanded;

    [ObservableProperty]
    private ObservableCollection<ImportedPackViewModel> _importedPacks = new();

    [ObservableProperty]
    private int _importWarningCount;

    [ObservableProperty]
    private List<string> _importWarnings = new();

    private IReadOnlyList<ImportInboxCandidate> _lastImportCandidates = [];

    [ObservableProperty]
    private int _baselineFindingsCount;

    [ObservableProperty]
    private int _appliedFixesCount;

    [ObservableProperty]
    private int _verifyFindingsCount;

    [ObservableProperty]
    private int _fixedCount;

    [ObservableProperty]
    private int _totalRuleCount;

    [ObservableProperty]
    private int _compliancePassCount;

    [ObservableProperty]
    private int _complianceFailCount;

    [ObservableProperty]
    private int _complianceOtherCount;

    [ObservableProperty]
    private int _catIVulnerabilityCount;

    [ObservableProperty]
    private int _catIIVulnerabilityCount;

    [ObservableProperty]
    private int _catIIIVulnerabilityCount;

    [ObservableProperty]
    private int _totalCatVulnerabilityCount;

    [ObservableProperty]
    private double _compliancePercent;

    [ObservableProperty]
    private string _scanComplianceText = string.Empty;

    [ObservableProperty]
    private string _verifyComplianceText = string.Empty;

    private bool _scanBaselineComplianceIsValid;
    private double _scanBaselineCompliancePercent;
    private int _scanBaselineComplianceDenominator;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunImportStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _importState = StepState.Ready;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunScanStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipScanStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _scanState = StepState.Locked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunHardenStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipHardenStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _hardenState = StepState.Locked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunVerifyStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _verifyState = StepState.Locked;

    [ObservableProperty]
    private string _importError = string.Empty;

    [ObservableProperty]
    private string _scanError = string.Empty;

    [ObservableProperty]
    private string _hardenError = string.Empty;

    [ObservableProperty]
    private string _verifyError = string.Empty;

    [ObservableProperty]
    private bool _exportCkl = true;

    [ObservableProperty]
    private bool _exportCsv;

    [ObservableProperty]
    private bool _exportXccdf;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailureCardLiveRegionHint))]
    private WorkflowFailureCard? _currentFailureCard;

    public string FailureCardLiveRegionHint => CurrentFailureCard is null
        ? string.Empty
        : $"Recovery guidance updated. {CurrentFailureCard.Title}. {CurrentFailureCard.WhatHappened} Next step: {CurrentFailureCard.NextStep}";

    public bool CanGoBack => CurrentStep > WorkflowStep.Setup && CurrentStep < WorkflowStep.Done;
    public bool CanRunImport => ImportState == StepState.Ready || ImportState == StepState.Complete || ImportState == StepState.Error;
    public bool CanRunScan => ScanState == StepState.Ready || ScanState == StepState.Complete || ScanState == StepState.Error;
    public bool CanSkipScan => ScanState == StepState.Ready || ScanState == StepState.Error;
    public bool CanRunHarden => HardenState == StepState.Ready || HardenState == StepState.Complete || HardenState == StepState.Error;
    public bool CanSkipHarden => HardenState == StepState.Ready || HardenState == StepState.Error;
    public bool CanRunVerify => VerifyState == StepState.Ready || VerifyState == StepState.Complete || VerifyState == StepState.Error;
    public bool CanTestSccHeadless => !IsBusy;

    public bool CanRunAutoWorkflow =>
        ImportState != StepState.Running
        && ScanState != StepState.Running
        && HardenState != StepState.Running
        && VerifyState != StepState.Running;

    public bool CanGoNext => CurrentStep switch
    {
        WorkflowStep.Setup => !string.IsNullOrWhiteSpace(ImportFolderPath)
                           && !string.IsNullOrWhiteSpace(EvaluateStigToolPath)
                           && !string.IsNullOrWhiteSpace(OutputFolderPath),
        WorkflowStep.Done => false,
        _ => !IsBusy
    };

    partial void OnSccTimeoutSecondsChanged(int value)
    {
        if (value < MinSccTimeoutSeconds)
        {
            SccTimeoutSeconds = MinSccTimeoutSeconds;
            return;
        }

        if (value > MaxSccTimeoutSeconds)
            SccTimeoutSeconds = MaxSccTimeoutSeconds;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (CurrentStep > WorkflowStep.Setup)
            CurrentStep = CurrentStep - 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task GoNextAsync()
    {
        if (CurrentStep == WorkflowStep.Setup)
            SaveSettings();

        if (CurrentStep < WorkflowStep.Done)
        {
            CurrentStep = CurrentStep + 1;

            if (CurrentStep != WorkflowStep.Setup && CurrentStep != WorkflowStep.Done)
                await RunCurrentStepAsync();
        }
    }

    public async Task RunCurrentStepAsync()
    {
        IsBusy = true;
        StatusText = "Starting...";
        try
        {
            switch (CurrentStep)
            {
                case WorkflowStep.Import:
                    await RunImportAsync();
                    break;
                case WorkflowStep.Scan:
                    await RunScanAsync();
                    break;
                case WorkflowStep.Harden:
                    await RunHardenAsync();
                    break;
                case WorkflowStep.Verify:
                    await RunVerifyAsync();
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAutoWorkflow))]
    private async Task RunAutoWorkflowAsync()
    {
        await RunImportStepAsync();
        if (ImportState == StepState.Error)
            return;

        await RunScanStepAsync();
        if (ScanState == StepState.Error)
            return;

        await RunHardenStepAsync();
        if (HardenState == StepState.Error)
            return;

        await RunVerifyStepAsync();
    }

    [RelayCommand]
    private void RestartWorkflow()
    {
        CurrentStep = WorkflowStep.Setup;
        ImportState = StepState.Ready;
        ScanState = StepState.Locked;
        HardenState = StepState.Locked;
        VerifyState = StepState.Locked;
        ImportError = string.Empty;
        ScanError = string.Empty;
        HardenError = string.Empty;
        VerifyError = string.Empty;
        BaselineFindingsCount = 0;
        VerifyFindingsCount = 0;
        FixedCount = 0;
        ScanComplianceText = string.Empty;
        VerifyComplianceText = string.Empty;
        InvalidateScanComplianceBaseline();
        AppliedFixesCount = 0;
        ImportedItemsCount = 0;
        IsImportedLibraryExpanded = false;
        ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
        ImportWarningCount = 0;
        ImportWarnings = new List<string>();
        MissionJsonPath = string.Empty;
        StatusText = string.Empty;
        ProgressValue = 0;
        ResetComplianceMetrics();
    }
}
