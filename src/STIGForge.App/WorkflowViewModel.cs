using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using STIGForge.Apply;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using STIGForge.App.Views;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using System.Collections.ObjectModel;

namespace STIGForge.App;

public class ImportedPackViewModel
{
    public string PackName { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public bool HasFiles => Files.Count > 0;
}

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

    private bool EnsureAdminPreflight(out string message)
    {
        if (_isElevatedResolver())
        {
            message = string.Empty;
            return true;
        }

        message = "Scan and Verify require Administrator mode. Close STIGForge and relaunch it as administrator.";
        return false;
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
    private ObservableCollection<ImportedPackViewModel> _importedPacks = new();

    [ObservableProperty]
    private int _importWarningCount;

    [ObservableProperty]
    private List<string> _importWarnings = new();

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

    public bool CanGoBack => CurrentStep > WorkflowStep.Setup && CurrentStep < WorkflowStep.Done;

    public bool CanRunImport => ImportState == StepState.Ready || ImportState == StepState.Complete || ImportState == StepState.Error;
    public bool CanRunScan => ScanState == StepState.Ready || ScanState == StepState.Complete || ScanState == StepState.Error;
    public bool CanRunHarden => HardenState == StepState.Ready || HardenState == StepState.Complete || HardenState == StepState.Error;
    public bool CanSkipHarden => HardenState == StepState.Ready || HardenState == StepState.Error;
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
            ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
            ImportedItemsCount = 0;
            ImportWarnings = new List<string>();
            ImportWarningCount = 0;
            return false;
        }

        try
        {
            var result = await _importScanner.ScanAsync(ImportFolderPath, CancellationToken.None);
            
            var packs = result.Candidates
                .GroupBy(c => c.FileName)
                .Select(g => new ImportedPackViewModel
                {
                    PackName = g.Key,
                    Files = g.SelectMany(c => c.ContentFileNames).Distinct().OrderBy(f => f).ToList()
                }).ToList();

            ImportedPacks = new ObservableCollection<ImportedPackViewModel>(packs);
            ImportedItemsCount = ImportedPacks.Count;

            var warnings = (result.Warnings ?? Array.Empty<string>())
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ImportWarnings = warnings;
            ImportWarningCount = warnings.Count;

            if (ImportedItemsCount == 0)
            {
                StatusText = ImportWarningCount > 0
                    ? $"No content packs found ({ImportWarningCount} warning(s))"
                    : "No content packs found in import folder";
            }
            else if (ImportWarningCount > 0)
            {
                StatusText = $"Found {ImportedItemsCount} content packs with {ImportWarningCount} warning(s)";
            }
            else
            {
                StatusText = $"Found {ImportedItemsCount} content packs";
            }

            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
            ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
            ImportedItemsCount = 0;
            ImportWarnings = new List<string> { ex.Message };
            ImportWarningCount = 1;
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

        if (ImportedItemsCount == 0)
        {
            StatusText = "No imported content detected. Run Import and confirm items in Imported Library.";
            BaselineFindingsCount = 0;
            return false;
        }

        if (!EnsureAdminPreflight(out var adminMessage))
        {
            StatusText = adminMessage;
            BaselineFindingsCount = 0;
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            BaselineFindingsCount = 0;
            return false;
        }

        if (!string.Equals(resolvedEvaluateToolPath, EvaluateStigToolPath, StringComparison.OrdinalIgnoreCase))
            EvaluateStigToolPath = resolvedEvaluateToolPath;

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            StatusText = "Output folder is required for scanning";
            BaselineFindingsCount = 0;
            return false;
        }

        Directory.CreateDirectory(OutputFolderPath);

        try
        {
            var request = new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = true,
                    ToolRoot = resolvedEvaluateToolPath
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = false
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, CancellationToken.None),
                CancellationToken.None);

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

    private async Task<bool> RunHardenAsync()
    {
        StatusText = "Applying hardening configurations...";
        AppliedFixesCount = 0;

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            StatusText = "Output folder is required for hardening";
            return false;
        }

        if (!Directory.Exists(OutputFolderPath))
        {
            StatusText = "Hardening cannot run: output folder does not exist";
            return false;
        }

        if (_runApply == null)
        {
            StatusText = "Apply runner not configured";
            return false;
        }

        try
        {
            var result = await _runApply(new ApplyRequest
            {
                BundleRoot = OutputFolderPath
            }, CancellationToken.None);

            if (!result.IsMissionComplete)
            {
                StatusText = "Hardening did not complete successfully";
                return false;
            }

            var blockingFailures = result.BlockingFailures ?? Array.Empty<string>();
            if (blockingFailures.Count > 0)
            {
                StatusText = "Hardening failed: " + string.Join(" | ", blockingFailures);
                return false;
            }

            var steps = result.Steps ?? Array.Empty<ApplyStepOutcome>();
            AppliedFixesCount = steps.Count(s => s.ExitCode == 0);

            StatusText = $"Hardening complete: {AppliedFixesCount} fixes applied";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Hardening failed: {ex.Message}";
            return false;
        }
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

        if (!EnsureAdminPreflight(out var adminMessage))
        {
            StatusText = adminMessage;
            VerifyFindingsCount = 0;
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            VerifyFindingsCount = 0;
            return false;
        }

        if (!string.Equals(resolvedEvaluateToolPath, EvaluateStigToolPath, StringComparison.OrdinalIgnoreCase))
            EvaluateStigToolPath = resolvedEvaluateToolPath;

        var sccWasConfigured = !string.IsNullOrWhiteSpace(SccToolPath);
        var resolvedSccCommandPath = ResolveSccCommandPath(SccToolPath);
        if (sccWasConfigured && string.IsNullOrWhiteSpace(resolvedSccCommandPath))
        {
            StatusText = LooksLikeUnsupportedSccGuiPath(SccToolPath)
                ? "SCC GUI executable (scc.exe) is not supported for automation. Use cscc.exe or cscc-remote.exe."
                : "SCC tool path is configured but no cscc.exe or cscc-remote.exe executable was found";
            VerifyFindingsCount = 0;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(resolvedSccCommandPath)
            && !string.Equals(resolvedSccCommandPath, SccToolPath, StringComparison.OrdinalIgnoreCase))
        {
            SccToolPath = resolvedSccCommandPath;
        }

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            StatusText = "Output folder is required for scanning";
            VerifyFindingsCount = 0;
            return false;
        }

        Directory.CreateDirectory(OutputFolderPath);

        try
        {
            var request = new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = true,
                    ToolRoot = resolvedEvaluateToolPath
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = !string.IsNullOrWhiteSpace(resolvedSccCommandPath),
                    CommandPath = resolvedSccCommandPath ?? string.Empty
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, CancellationToken.None),
                CancellationToken.None);

            VerifyFindingsCount = result.ConsolidatedResultCount;
            FixedCount = BaselineFindingsCount - VerifyFindingsCount;
            if (FixedCount < 0) FixedCount = 0;

            await WriteMissionJsonAsync(result, CancellationToken.None);
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

    private async Task WriteMissionJsonAsync(VerificationWorkflowResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(OutputFolderPath))
            throw new InvalidOperationException("Output folder is required to write mission.json");

        Directory.CreateDirectory(OutputFolderPath);

        var missionPath = Path.Combine(OutputFolderPath, "mission.json");
        var mission = new LocalWorkflowMission
        {
            Diagnostics = result.Diagnostics ?? Array.Empty<string>(),
            StageMetadata = new LocalWorkflowStageMetadata
            {
                MissionJsonPath = missionPath,
                ConsolidatedJsonPath = result.ConsolidatedJsonPath,
                ConsolidatedCsvPath = result.ConsolidatedCsvPath,
                CoverageSummaryJsonPath = result.CoverageSummaryJsonPath,
                CoverageSummaryCsvPath = result.CoverageSummaryCsvPath,
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt
            }
        };

        var json = JsonSerializer.Serialize(mission, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(missionPath, json, ct).ConfigureAwait(false);
        MissionJsonPath = missionPath;
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
            var success = await RunHardenAsync();
            if (success)
            {
                HardenState = StepState.Complete;
                if (VerifyState == StepState.Locked) VerifyState = StepState.Ready;
            }
            else
            {
                HardenState = StepState.Error;
                HardenError = StatusText;
            }
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

    [RelayCommand(CanExecute = nameof(CanSkipHarden))]
    private void SkipHardenStep()
    {
        HardenState = StepState.Complete;
        HardenError = string.Empty;
        AppliedFixesCount = 0;
        StatusText = "Hardening skipped by operator";

        if (VerifyState == StepState.Locked)
            VerifyState = StepState.Ready;
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
    private void AutoScanSetupPaths()
    {
        var detected = new List<string>();
        var scanRoot = _autoScanRootResolver();

        if (string.IsNullOrWhiteSpace(scanRoot))
        {
            StatusText = "Auto scan did not find new setup paths";
            return;
        }

        if (string.IsNullOrWhiteSpace(ImportFolderPath))
        {
            var importCandidate = Path.Combine(scanRoot, "import");
            if (Directory.Exists(importCandidate))
            {
                ImportFolderPath = importCandidate;
                detected.Add("Import folder");
            }
        }

        if (string.IsNullOrWhiteSpace(EvaluateStigToolPath))
        {
            var evaluateCandidate = FindFirstResolvedPath(GetEvaluateStigCandidates(scanRoot), ResolveEvaluateStigToolRoot);
            if (!string.IsNullOrWhiteSpace(evaluateCandidate))
            {
                EvaluateStigToolPath = evaluateCandidate;
                detected.Add("Evaluate-STIG path");
            }
        }

        if (string.IsNullOrWhiteSpace(SccToolPath))
        {
            var sccCandidate = FindFirstResolvedPath(GetSccCandidates(scanRoot), ResolveSccCommandPath);
            if (!string.IsNullOrWhiteSpace(sccCandidate))
            {
                SccToolPath = sccCandidate;
                detected.Add("SCC path");
            }
        }

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            var outputCandidate = Path.Combine(scanRoot, ".stigforge", "scans");
            Directory.CreateDirectory(outputCandidate);
            OutputFolderPath = outputCandidate;
            detected.Add("Output folder");
        }

        StatusText = detected.Count > 0
            ? "Auto-detected setup paths: " + string.Join(", ", detected)
            : "Auto scan did not find new setup paths";
    }

    private static string? ResolveAutoScanRoot()
    {
        var workspaceRoot = FindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            return workspaceRoot;

        if (Directory.Exists(AppContext.BaseDirectory))
            return AppContext.BaseDirectory;

        return null;
    }

    private static string? FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "STIGForge.sln")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    private static string? FindFirstResolvedPath(IEnumerable<string> candidates, Func<string, string?> resolver)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var resolved = resolver(candidate);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
    }

    private static IEnumerable<string> GetEvaluateStigCandidates(string scanRoot)
    {
        yield return Path.Combine(scanRoot, "tools", "Evaluate-STIG", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "tools", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "Evaluate-STIG", "Evaluate-STIG");
        yield return Path.Combine(scanRoot, "Evaluate-STIG");
    }

    private static IEnumerable<string> GetSccCandidates(string scanRoot)
    {
        yield return Path.Combine(scanRoot, "tools", "SCC", "cscc.exe");
        yield return Path.Combine(scanRoot, "tools", "SCC", "cscc-remote.exe");
        yield return Path.Combine(scanRoot, "tools", "SCC");
        yield return Path.Combine(scanRoot, "SCC", "cscc.exe");
        yield return Path.Combine(scanRoot, "SCC", "cscc-remote.exe");
        yield return Path.Combine(scanRoot, "SCC");
    }

    private static string? ResolveEvaluateStigToolRoot(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
        {
            if (string.Equals(Path.GetFileName(path), "Evaluate-STIG.ps1", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(path);

            return null;
        }

        if (!Directory.Exists(path))
            return null;

        var directScript = Path.Combine(path, "Evaluate-STIG.ps1");
        if (File.Exists(directScript))
            return path;

        try
        {
            var match = Directory.EnumerateFiles(path, "Evaluate-STIG.ps1", SearchOption.AllDirectories)
                .OrderBy(CountPathSeparators)
                .ThenBy(static p => p.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
                return Path.GetDirectoryName(match);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static string? ResolveSccCommandPath(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
        {
            var fileName = Path.GetFileName(path);
            return IsSupportedSccCliFileName(fileName)
                ? Path.GetFullPath(path)
                : null;
        }

        if (!Directory.Exists(path))
            return null;

        foreach (var fileName in SupportedSccCliFileNames)
        {
            var directCandidate = Path.Combine(path, fileName);
            if (File.Exists(directCandidate))
                return directCandidate;
        }

        try
        {
            var match = Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories)
                .Where(static p =>
                {
                    var fileName = Path.GetFileName(p);
                    return IsSupportedSccCliFileName(fileName);
                })
                .OrderBy(CountPathSeparators)
                .ThenBy(static p => p.Length)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static readonly string[] SupportedSccCliFileNames =
    [
        "cscc.exe",
        "cscc-remote.exe",
        "cscc",
        "cscc-remote"
    ];

    private static bool IsSupportedSccCliFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        return SupportedSccCliFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeUnsupportedSccGuiPath(string? candidate)
    {
        var path = candidate?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (File.Exists(path))
            return string.Equals(Path.GetFileName(path), "scc.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(path), "scc", StringComparison.OrdinalIgnoreCase);

        if (!Directory.Exists(path))
            return false;

        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Any(p => string.Equals(Path.GetFileName(p), "scc.exe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(p), "scc", StringComparison.OrdinalIgnoreCase));
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static int CountPathSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
            return int.MaxValue;

        return path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar);
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select SCC Executable",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            SccToolPath = dialog.FileName;
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
            "Open Settings to configure paths (or use Auto Scan Setup Paths), then run Import, Scan, Harden, and Verify in order or use Auto Workflow. You can also skip Harden when needed.",
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
        ImportedPacks = new ObservableCollection<ImportedPackViewModel>();
        ImportWarningCount = 0;
        ImportWarnings = new List<string>();
        MissionJsonPath = string.Empty;
        StatusText = string.Empty;
        ProgressValue = 0;
    }
}
