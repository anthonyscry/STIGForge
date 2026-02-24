using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.App.Views;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;

namespace STIGForge.App;

public enum WorkflowStep
{
    Setup,
    Import,
    Scan,
    Harden,
    Verify,
    Done
}

public enum StepState
{
    Locked,
    Ready,
    Running,
    Complete,
    Error
}

public partial class WorkflowViewModel : ObservableObject
{
    private readonly ImportInboxScanner? _importScanner;
    private readonly IVerificationWorkflowService? _verifyService;
    // TODO: Wire ApplyRunner when DI infrastructure is available
    // ApplyRunner has many dependencies (SnapshotService, LcmService, RebootCoordinator, etc.)
    // that require proper DI container setup

    public WorkflowViewModel(
        ImportInboxScanner? importScanner = null,
        IVerificationWorkflowService? verifyService = null)
    {
        _importScanner = importScanner;
        _verifyService = verifyService;
        LoadSettings();
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
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _sccToolPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _machineTarget = "localhost";

    [ObservableProperty]
    private string _missionJsonPath = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    private int _importedItemsCount;

    [ObservableProperty]
    private List<string> _importedItems = new();

    [ObservableProperty]
    private int _baselineFindingsCount;

    [ObservableProperty]
    private int _appliedFixesCount;

    [ObservableProperty]
    private int _verifyFindingsCount;

    [ObservableProperty]
    private int _fixedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunImportStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _importState = StepState.Ready;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunScanStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAutoWorkflowCommand))]
    private StepState _scanState = StepState.Locked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunHardenStepCommand))]
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

    public bool CanGoBack => CurrentStep > WorkflowStep.Setup && CurrentStep < WorkflowStep.Done;

    public bool CanRunImport => ImportState == StepState.Ready || ImportState == StepState.Complete || ImportState == StepState.Error;
    public bool CanRunScan => ScanState == StepState.Ready || ScanState == StepState.Complete || ScanState == StepState.Error;
    public bool CanRunHarden => HardenState == StepState.Ready || HardenState == StepState.Complete || HardenState == StepState.Error;
    public bool CanRunVerify => VerifyState == StepState.Ready || VerifyState == StepState.Complete || VerifyState == StepState.Error;

    public bool CanRunAutoWorkflow => 
        ImportState != StepState.Running && 
        ScanState != StepState.Running && 
        HardenState != StepState.Running && 
        VerifyState != StepState.Running;

    public bool CanGoNext => CurrentStep switch
    {
        WorkflowStep.Setup => !string.IsNullOrWhiteSpace(ImportFolderPath)
                           && !string.IsNullOrWhiteSpace(EvaluateStigToolPath)
                           && !string.IsNullOrWhiteSpace(OutputFolderPath),
        WorkflowStep.Done => false,
        _ => !IsBusy
    };

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

            // Auto-run the step action when entering a new step (except Setup and Done)
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

    private async Task<bool> RunImportAsync()
    {
        StatusText = "Scanning import folder...";

        if (_importScanner == null || string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            StatusText = "Import scanner not configured or no import folder";
            ImportedItemsCount = 0;
            return false;
        }

        try
        {
            var result = await _importScanner.ScanAsync(ImportFolderPath, CancellationToken.None);
            ImportedItems = result.Candidates.Select(c => c.FileName).Distinct().ToList();
            ImportedItemsCount = ImportedItems.Count;
            StatusText = $"Found {ImportedItemsCount} content packs";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            ImportedItemsCount = 0;
            return false;
        }
    }

    private async Task<bool> RunScanAsync()
    {
        StatusText = "Running Evaluate-STIG baseline scan...";

        if (_verifyService == null)
        {
            StatusText = "Verification service not configured";
            BaselineFindingsCount = 0;
            return false;
        }

        try
        {
            var result = await _verifyService.RunAsync(new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = true,
                    ToolRoot = EvaluateStigToolPath
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = false
                }
            }, CancellationToken.None);

            BaselineFindingsCount = result.ConsolidatedResultCount;
            StatusText = $"Baseline scan complete: {BaselineFindingsCount} findings";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            BaselineFindingsCount = 0;
            return false;
        }
    }

    private async Task RunHardenAsync()
    {
        StatusText = "Applying hardening configurations...";

        // TODO: Wire ApplyRunner when DI infrastructure is available
        // ApplyRunner requires: ILogger<ApplyRunner>, SnapshotService, RollbackScriptGenerator,
        // LcmService, RebootCoordinator, and optional IAuditTrailService, EvidenceCollector,
        // LgpoRunner, PreflightRunner dependencies.
        // For now, this remains a placeholder until proper DI wiring is implemented.
        await Task.Delay(500);
        AppliedFixesCount = 0;
        StatusText = "Hardening step placeholder (ApplyRunner not wired)";
    }

    private async Task<bool> RunVerifyAsync()
    {
        StatusText = "Running verification scan (Evaluate-STIG + SCC)...";

        if (_verifyService == null)
        {
            StatusText = "Verification service not configured";
            VerifyFindingsCount = 0;
            return false;
        }

        try
        {
            var result = await _verifyService.RunAsync(new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = true,
                    ToolRoot = EvaluateStigToolPath
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = !string.IsNullOrWhiteSpace(SccToolPath),
                    CommandPath = SccToolPath
                }
            }, CancellationToken.None);

            VerifyFindingsCount = result.ConsolidatedResultCount;
            FixedCount = BaselineFindingsCount - VerifyFindingsCount;
            if (FixedCount < 0) FixedCount = 0;

            MissionJsonPath = Path.Combine(OutputFolderPath, "mission.json");
            StatusText = $"Verification complete: {VerifyFindingsCount} remaining ({FixedCount} fixed)";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Verification failed: {ex.Message}";
            VerifyFindingsCount = 0;
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunImport))]
    private async Task RunImportStepAsync()
    {
        IsBusy = true;
        ImportState = StepState.Running;
        ImportError = string.Empty;
        try
        {
            var success = await RunImportAsync();
            if (success)
            {
                ImportState = StepState.Complete;
                if (ScanState == StepState.Locked) ScanState = StepState.Ready;
            }
            else
            {
                ImportState = StepState.Error;
                ImportError = StatusText;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunScan))]
    private async Task RunScanStepAsync()
    {
        IsBusy = true;
        ScanState = StepState.Running;
        ScanError = string.Empty;
        try
        {
            var success = await RunScanAsync();
            if (success)
            {
                ScanState = StepState.Complete;
                if (HardenState == StepState.Locked) HardenState = StepState.Ready;
            }
            else
            {
                ScanState = StepState.Error;
                ScanError = StatusText;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunHarden))]
    private async Task RunHardenStepAsync()
    {
        IsBusy = true;
        HardenState = StepState.Running;
        HardenError = string.Empty;
        try
        {
            await RunHardenAsync();
            HardenState = StepState.Complete;
            if (VerifyState == StepState.Locked) VerifyState = StepState.Ready;
        }
        catch (Exception ex)
        {
            HardenState = StepState.Error;
            HardenError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunVerify))]
    private async Task RunVerifyStepAsync()
    {
        IsBusy = true;
        VerifyState = StepState.Running;
        VerifyError = string.Empty;
        try
        {
            var success = await RunVerifyAsync();
            if (success)
            {
                VerifyState = StepState.Complete;
            }
            else
            {
                VerifyState = StepState.Error;
                VerifyError = StatusText;
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
        // Run Import
        await RunImportStepAsync();
        if (ImportState == StepState.Error) return;
        
        // Run Scan
        await RunScanStepAsync();
        if (ScanState == StepState.Error) return;
        
        // Run Harden
        await RunHardenStepAsync();
        if (HardenState == StepState.Error) return;
        
        // Run Verify
        await RunVerifyStepAsync();
    }

    private void LoadSettings()
    {
        var settings = WorkflowSettings.Load();
        ImportFolderPath = settings.ImportFolderPath;
        EvaluateStigToolPath = settings.EvaluateStigToolPath;
        SccToolPath = settings.SccToolPath;
        OutputFolderPath = settings.OutputFolderPath;
        MachineTarget = settings.MachineTarget;
        ExportCkl = settings.ExportCkl;
        ExportCsv = settings.ExportCsv;
        ExportXccdf = settings.ExportXccdf;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        WorkflowSettings.Save(new WorkflowSettings
        {
            ImportFolderPath = ImportFolderPath,
            EvaluateStigToolPath = EvaluateStigToolPath,
            SccToolPath = SccToolPath,
            OutputFolderPath = OutputFolderPath,
            MachineTarget = MachineTarget,
            ExportCkl = ExportCkl,
            ExportCsv = ExportCsv,
            ExportXccdf = ExportXccdf
        });
    }

    [RelayCommand]
    private void ShowSettings()
    {
        var settingsWindow = new Views.SettingsWindow
        {
            DataContext = this,
            Owner = Application.Current.MainWindow
        };

        if (settingsWindow.ShowDialog() != true)
            LoadSettings();
    }

    [RelayCommand]
    private void BrowseImportFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Import Folder" };
        if (dialog.ShowDialog() == true)
            ImportFolderPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseEvaluateStig()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Evaluate-STIG Folder" };
        if (dialog.ShowDialog() == true)
            EvaluateStigToolPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseScc()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select SCC Folder" };
        if (dialog.ShowDialog() == true)
            SccToolPath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Output Folder" };
        if (dialog.ShowDialog() == true)
            OutputFolderPath = dialog.FolderName;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (!string.IsNullOrWhiteSpace(OutputFolderPath) && System.IO.Directory.Exists(OutputFolderPath))
            System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var dataRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "STIGForge");
        var dialog = new AboutDialog(dataRoot, 0, 0, 0)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        MessageBox.Show(
            "Open the Settings menu to configure paths and export formats, then run Import, Scan, Harden, and Verify in order or use Auto Workflow.",
            "Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        AppliedFixesCount = 0;
        ImportedItemsCount = 0;
        ImportedItems = new List<string>();
        MissionJsonPath = string.Empty;
        StatusText = string.Empty;
        ProgressValue = 0;
    }
}
