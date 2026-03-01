using System.IO;
using System.IO.Compression;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using STIGForge.Apply;
using STIGForge.Apply.Lgpo;
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

public enum WorkflowRootCauseCode
{
    ElevationRequired,
    EvaluatePathInvalid,
    NoCklOutput,
    ScapNoOutput,
    ScapArgumentsInvalid,
    ToolExitNonZero,
    OutputNotWritable,
    UnknownFailure
}

public sealed class WorkflowFailureCard
{
    public WorkflowRootCauseCode RootCauseCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string WhatHappened { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string Confidence { get; init; } = "Medium";
    public bool ShowOpenSettingsAction { get; init; }
    public bool ShowRetryScanAction { get; init; }
    public bool ShowRetryVerifyAction { get; init; }
    public bool ShowOpenOutputFolderAction { get; init; }
}

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

    private bool EnsureAdminPreflight(out string message)
    {
        if (!RequireElevationForScan)
        {
            message = string.Empty;
            return true;
        }

        if (_isElevatedResolver())
        {
            message = string.Empty;
            return true;
        }

        message = "Scan and Verify require Administrator mode. Close STIGForge and relaunch it as administrator.";
        return false;
    }

    private static VerificationToolRunResult? FindEvaluateRun(VerificationWorkflowResult result)
    {
        return result.ToolRuns?.FirstOrDefault(run =>
            string.Equals(run.Tool, "Evaluate-STIG", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNoCklDiagnostic(VerificationWorkflowResult result)
    {
        var diagnostics = result.Diagnostics ?? Array.Empty<string>();
        return diagnostics.Any(diagnostic =>
            diagnostic.Contains("No CKL results", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasScapDidNothingDiagnostic(VerificationWorkflowResult result)
    {
        var diagnostics = result.Diagnostics ?? Array.Empty<string>();
        return diagnostics.Any(diagnostic =>
            diagnostic.Contains("SCAP arguments were empty", StringComparison.OrdinalIgnoreCase)
            || diagnostic.Contains("produced no SCAP artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsZeroFindingsFailure(VerificationWorkflowResult result)
    {
        if (!HasAnyResults(result))
            return true;

        var evaluateRun = FindEvaluateRun(result);
        if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
            return true;

        return HasNoCklDiagnostic(result);
    }

    private static bool HasAnyResults(VerificationWorkflowResult result)
    {
        if (result == null)
            return false;

        if (result.TotalRuleCount > 0)
            return true;

        return result.ConsolidatedResultCount > 0;
    }

    private static string BuildZeroFindingsMessage(string operationName, VerificationWorkflowResult result)
    {
        var evaluateRun = FindEvaluateRun(result);
        if (evaluateRun is { Executed: true, ExitCode: 5 })
            return $"{operationName} did not complete: Evaluate-STIG exit code 5 (administrator mode required).";

        if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
            return $"{operationName} did not complete: Evaluate-STIG exit code {evaluateRun.ExitCode}.";

        if (HasNoCklDiagnostic(result))
            return $"{operationName} produced no CKL output. Confirm Evaluate-STIG output path and selected STIG scope.";

        return $"{operationName} complete: 0 findings";
    }

    private static WorkflowFailureCard CreateFailureCard(
        WorkflowRootCauseCode rootCauseCode,
        string title,
        string whatHappened,
        string nextStep,
        bool showOpenSettingsAction = false,
        bool showRetryScanAction = false,
        bool showRetryVerifyAction = false,
        bool showOpenOutputFolderAction = false)
    {
        return new WorkflowFailureCard
        {
            RootCauseCode = rootCauseCode,
            Title = title,
            WhatHappened = whatHappened,
            NextStep = nextStep,
            ShowOpenSettingsAction = showOpenSettingsAction,
            ShowRetryScanAction = showRetryScanAction,
            ShowRetryVerifyAction = showRetryVerifyAction,
            ShowOpenOutputFolderAction = showOpenOutputFolderAction
        };
    }

    private static WorkflowFailureCard BuildScanFailureCard(VerificationWorkflowResult result)
    {
        var evaluateRun = FindEvaluateRun(result);
        if (evaluateRun is { Executed: true, ExitCode: 5 })
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.ElevationRequired,
                "Administrator privileges required",
                "Evaluate-STIG exited with code 5 during baseline scan.",
                "Relaunch STIGForge as administrator and rerun Scan.",
                showRetryScanAction: true);
        }

        if (HasNoCklDiagnostic(result))
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.NoCklOutput,
                "No CKL output detected",
                "Scan completed without producing CKL results in the output folder.",
                "Verify CKL output path and STIG scope in Settings, then rerun Scan.",
                showOpenSettingsAction: true,
                showRetryScanAction: true,
                showOpenOutputFolderAction: true);
        }

        if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.ToolExitNonZero,
                "Evaluate-STIG exited with an error",
                $"Evaluate-STIG returned exit code {evaluateRun.ExitCode} during baseline scan.",
                "Review diagnostics in mission output and rerun Scan after correcting the issue.",
                showRetryScanAction: true,
                showOpenOutputFolderAction: true);
        }

        return CreateFailureCard(
            WorkflowRootCauseCode.UnknownFailure,
            "Scan could not be completed",
            "Baseline scan returned zero findings but did not provide a known failure signature.",
            "Review diagnostics in mission output, adjust settings, and rerun Scan.",
            showRetryScanAction: true,
            showOpenOutputFolderAction: true);
    }

    private static WorkflowFailureCard BuildVerifyFailureCard(VerificationWorkflowResult result)
    {
        var evaluateRun = FindEvaluateRun(result);
        if (evaluateRun is { Executed: true, ExitCode: 5 })
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.ElevationRequired,
                "Administrator privileges required",
                "Evaluate-STIG exited with code 5 during verification scan.",
                "Relaunch STIGForge as administrator and rerun Verify.",
                showRetryVerifyAction: true);
        }

        if (HasNoCklDiagnostic(result))
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.NoCklOutput,
                "No CKL output detected",
                "Verification completed without producing CKL results in the output folder.",
                "Verify CKL output path and STIG scope in Settings, then rerun Verify.",
                showOpenSettingsAction: true,
                showRetryVerifyAction: true,
                showOpenOutputFolderAction: true);
        }

        if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
        {
            return CreateFailureCard(
                WorkflowRootCauseCode.ToolExitNonZero,
                "Evaluate-STIG exited with an error",
                $"Evaluate-STIG returned exit code {evaluateRun.ExitCode} during verification scan.",
                "Review diagnostics in mission output and rerun Verify after correcting the issue.",
                showRetryVerifyAction: true,
                showOpenOutputFolderAction: true);
        }

        return CreateFailureCard(
            WorkflowRootCauseCode.UnknownFailure,
            "Verification could not be completed",
            "Verification returned zero findings but did not provide a known failure signature.",
            "Review diagnostics in mission output, adjust settings, and rerun Verify.",
            showRetryVerifyAction: true,
            showOpenOutputFolderAction: true);
    }

    private static WorkflowFailureCard CreateEvaluatePathInvalidCard()
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.EvaluatePathInvalid,
            "Evaluate-STIG path is invalid",
            "The configured Evaluate-STIG location does not contain a usable Evaluate-STIG.ps1 script.",
            "Open Settings, correct the Evaluate-STIG path, save, and rerun Scan.",
            showOpenSettingsAction: true,
            showRetryScanAction: true);
    }

    private static WorkflowFailureCard CreateVerifyEvaluatePathInvalidCard()
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.EvaluatePathInvalid,
            "Evaluate-STIG path is invalid",
            "The configured Evaluate-STIG location does not contain a usable Evaluate-STIG.ps1 script.",
            "Open Settings, correct the Evaluate-STIG path, save, and rerun Verify.",
            showOpenSettingsAction: true,
            showRetryVerifyAction: true);
    }

    private static WorkflowFailureCard CreateElevationRequiredCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.ElevationRequired,
            "Administrator privileges required",
            whatHappened,
            "Relaunch STIGForge as administrator and rerun Scan.",
            showRetryScanAction: true);
    }

    private static WorkflowFailureCard CreateVerifyElevationRequiredCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.ElevationRequired,
            "Administrator privileges required",
            whatHappened,
            "Relaunch STIGForge as administrator and rerun Verify.",
            showRetryVerifyAction: true);
    }

    private static WorkflowFailureCard CreateVerifyUnknownFailureCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.UnknownFailure,
            "Verification could not be completed",
            whatHappened,
            "Review diagnostics in mission output and rerun Verify.",
            showRetryVerifyAction: true,
            showOpenOutputFolderAction: true);
    }

    private static WorkflowFailureCard CreateVerifyScapNoOutputCard()
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.ScapNoOutput,
            "SCC produced no usable output",
            "SCC was configured for Verify, but no SCAP artifacts were produced for result consolidation.",
            "Open Settings and either clear SCC path for Evaluate-only verify or run SCC manually with explicit arguments/output, then rerun Verify.",
            showOpenSettingsAction: true,
            showRetryVerifyAction: true,
            showOpenOutputFolderAction: true);
    }

    private static WorkflowFailureCard CreateVerifyScapArgumentsInvalidCard(string details)
    {
        var detailText = string.IsNullOrWhiteSpace(details)
            ? "SCC arguments are missing required scan/content options."
            : details;

        return CreateFailureCard(
            WorkflowRootCauseCode.ScapArgumentsInvalid,
            "SCC arguments are invalid",
            detailText,
            "Open Settings and provide SCC arguments that include scan/content options. STIGForge auto-adds the output folder argument when missing.",
            showOpenSettingsAction: true,
            showRetryVerifyAction: true);
    }

    private string BuildEvaluateStigArguments(string outputRoot)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(EvaluateAfPath))
            parts.Add("-AFPath " + QuoteCommandLineArgument(EvaluateAfPath.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateSelectStig))
            parts.Add("-SelectSTIG " + QuoteCommandLineArgument(EvaluateSelectStig.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateAdditionalArgs))
            parts.Add(EvaluateAdditionalArgs.Trim());

        parts.Add("-Output CKL");
        parts.Add("-OutputPath " + QuoteCommandLineArgument(outputRoot));

        var target = MachineTarget?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "localhost", StringComparison.OrdinalIgnoreCase))
            parts.Add("-ComputerName " + QuoteCommandLineArgument(target));

        return string.Join(" ", parts);
    }

    private static string QuoteCommandLineArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string AppendCommandLineArgumentWithValue(string existingArguments, string switchName, string value)
    {
        var prefix = string.IsNullOrWhiteSpace(existingArguments)
            ? string.Empty
            : existingArguments.Trim() + " ";

        return prefix + switchName + " " + QuoteCommandLineArgument(value);
    }

    private static bool LooksLikeSwitchToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (token[0] == '-')
            return true;

        if (token[0] != '/')
            return false;

        return SccOutputSwitches
            .Where(candidate => candidate.StartsWith("/", StringComparison.Ordinal))
            .Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetSwitchAndInlineValue(string token, out string switchToken, out string inlineValue)
    {
        switchToken = string.Empty;
        inlineValue = string.Empty;

        if (!LooksLikeSwitchToken(token))
            return false;

        var equalsIndex = token.IndexOf('=');
        if (equalsIndex > 0)
        {
            switchToken = token[..equalsIndex];
            inlineValue = token[(equalsIndex + 1)..];
            return true;
        }

        switchToken = token;
        return true;
    }

    private static bool IsSccOutputSwitch(string switchToken)
    {
        if (string.IsNullOrWhiteSpace(switchToken))
            return false;

        return SccOutputSwitches.Any(candidate => string.Equals(candidate, switchToken, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> TokenizeCommandLineArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
            return tokens;

        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private readonly record struct SccArgumentAnalysis(
        bool HasAnyToken,
        bool HasOutputSwitch,
        bool HasOutputValue,
        bool MissingOutputValue,
        bool HasNonOutputDirective);

    private static SccArgumentAnalysis AnalyzeSccArguments(string arguments)
    {
        var tokens = TokenizeCommandLineArguments(arguments);
        if (tokens.Count == 0)
            return new SccArgumentAnalysis(false, false, false, false, false);

        var consumedAsOutputValue = new HashSet<int>();
        var hasOutputSwitch = false;
        var hasOutputValue = false;
        var missingOutputValue = false;
        var hasNonOutputDirective = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (consumedAsOutputValue.Contains(i))
                continue;

            var token = tokens[i];
            if (TryGetSwitchAndInlineValue(token, out var switchToken, out var inlineValue))
            {
                if (IsSccOutputSwitch(switchToken))
                {
                    hasOutputSwitch = true;

                    if (!string.IsNullOrWhiteSpace(inlineValue))
                    {
                        hasOutputValue = true;
                        continue;
                    }

                    if (i + 1 < tokens.Count && !LooksLikeSwitchToken(tokens[i + 1]))
                    {
                        hasOutputValue = true;
                        consumedAsOutputValue.Add(i + 1);
                    }
                    else
                    {
                        missingOutputValue = true;
                    }

                    continue;
                }

                hasNonOutputDirective = true;
                continue;
            }

            hasNonOutputDirective = true;
        }

        return new SccArgumentAnalysis(
            HasAnyToken: true,
            HasOutputSwitch: hasOutputSwitch,
            HasOutputValue: hasOutputValue,
            MissingOutputValue: missingOutputValue,
            HasNonOutputDirective: hasNonOutputDirective);
    }

    private static bool TryBuildSccHeadlessArguments(string rawArguments, string outputRoot, out string effectiveArguments, out string validationError)
    {
        effectiveArguments = (rawArguments ?? string.Empty).Trim();
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            validationError = "Output folder is required for SCC validation.";
            return false;
        }

        var initial = AnalyzeSccArguments(effectiveArguments);
        if (!initial.HasOutputSwitch)
            effectiveArguments = AppendCommandLineArgumentWithValue(effectiveArguments, "-u", outputRoot);

        var normalized = AnalyzeSccArguments(effectiveArguments);

        if (normalized.MissingOutputValue || (normalized.HasOutputSwitch && !normalized.HasOutputValue))
        {
            validationError = "SCC arguments include an output switch without a valid path value.";
            return false;
        }

        if (!normalized.HasAnyToken)
        {
            validationError = "SCC arguments are empty.";
            return false;
        }

        return true;
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

    private IReadOnlyList<ImportInboxCandidate> _lastImportCandidates = Array.Empty<ImportInboxCandidate>();

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FailureCardLiveRegionHint))]
    private WorkflowFailureCard? _currentFailureCard;

    public string FailureCardLiveRegionHint => CurrentFailureCard is null
        ? string.Empty
        : $"Recovery guidance updated. {CurrentFailureCard.Title}. {CurrentFailureCard.WhatHappened} Next step: {CurrentFailureCard.NextStep}";

    public bool CanGoBack => CurrentStep > WorkflowStep.Setup && CurrentStep < WorkflowStep.Done;

    public bool CanRunImport => ImportState == StepState.Ready || ImportState == StepState.Complete || ImportState == StepState.Error;
    public bool CanRunScan => ScanState == StepState.Ready || ScanState == StepState.Complete || ScanState == StepState.Error;
    public bool CanRunHarden => HardenState == StepState.Ready || HardenState == StepState.Complete || HardenState == StepState.Error;
    public bool CanSkipHarden => HardenState == StepState.Ready || HardenState == StepState.Error;
    public bool CanRunVerify => VerifyState == StepState.Ready || VerifyState == StepState.Complete || VerifyState == StepState.Error;
    public bool CanTestSccHeadless => !IsBusy;

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
            _lastImportCandidates = result.Candidates;

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

            if (ImportedItemsCount > 0 && !string.IsNullOrWhiteSpace(OutputFolderPath))
            {
                var staged = await Task.Run(() => StageApplyArtifacts(_lastImportCandidates, OutputFolderPath));
                if (staged > 0)
                    StatusText += $" ({staged} apply artifact(s) staged)";
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
        CurrentFailureCard = null;

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
            CurrentFailureCard = CreateElevationRequiredCard("Baseline scan preflight blocked because STIGForge is not running with administrator privileges.");
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            BaselineFindingsCount = 0;
            CurrentFailureCard = CreateEvaluatePathInvalidCard();
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
                    ToolRoot = resolvedEvaluateToolPath,
                    Arguments = BuildEvaluateStigArguments(OutputFolderPath)
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = false
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, CancellationToken.None),
                CancellationToken.None);

            var baselineOpenFindings = result.FailCount + result.ErrorCount;
            BaselineFindingsCount = baselineOpenFindings;
            if (baselineOpenFindings == 0)
            {
              StatusText = BuildZeroFindingsMessage("Baseline scan", result);
              var isFailure = IsZeroFindingsFailure(result);
              CurrentFailureCard = isFailure ? BuildScanFailureCard(result) : null;

              return !isFailure;
            }

            StatusText = $"Baseline scan complete: {BaselineFindingsCount} findings";
            CurrentFailureCard = null;
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
            var request = BuildHardenApplyRequest(OutputFolderPath);
            if (!HasAnyHardenApplyInput(request))
            {
                StatusText = BuildMissingHardenArtifactsMessage(OutputFolderPath);
                return false;
            }

            var result = await _runApply(request, CancellationToken.None);

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

    private static ApplyRequest BuildHardenApplyRequest(string bundleRoot)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        var powerStigModulePath = ResolvePowerStigModulePath(applyRoot);
        var dscMofPath = ResolveDscMofPath(applyRoot);
        if (string.IsNullOrWhiteSpace(dscMofPath) && !string.IsNullOrWhiteSpace(powerStigModulePath))
            dscMofPath = Path.Combine(applyRoot, "Dsc");

        var lgpoPolPath = ResolveLgpoPolFilePath(applyRoot, out var lgpoScope);
        var powerStigDataPath = ResolvePowerStigDataPath(applyRoot);

        // Resolve OS target and role from bundle manifest for PowerSTIG composite resource selection
        var (osTarget, roleTemplate) = ReadOsTargetFromManifest(bundleRoot);

        var request = new ApplyRequest
        {
            BundleRoot = bundleRoot,
            DscMofPath = dscMofPath,
            PowerStigModulePath = powerStigModulePath,
            PowerStigDataFile = powerStigDataPath,
            PowerStigOutputPath = string.IsNullOrWhiteSpace(powerStigModulePath)
                ? null
                : Path.Combine(applyRoot, "Dsc"),
            PowerStigDataGeneratedPath = powerStigDataPath,
            LgpoPolFilePath = lgpoPolPath,
            LgpoScope = lgpoScope,
            AdmxTemplateRootPath = ResolveAdmxTemplateRootPath(applyRoot),
            OsTarget = osTarget,
            RoleTemplate = roleTemplate
        };

        var lgpoExePath = ResolveLgpoExePath(bundleRoot);
        if (!string.IsNullOrWhiteSpace(lgpoExePath))
            request.LgpoExePath = lgpoExePath;

        return request;
    }

    private static (OsTarget? osTarget, RoleTemplate? roleTemplate) ReadOsTargetFromManifest(string bundleRoot)
    {
        try
        {
            var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
            if (!File.Exists(manifestPath))
                return (null, null);

            using var stream = File.OpenRead(manifestPath);
            using var doc = System.Text.Json.JsonDocument.Parse(stream);

            OsTarget? osTarget = null;
            RoleTemplate? roleTemplate = null;

            if (doc.RootElement.TryGetProperty("Profile", out var profile))
            {
                if (profile.TryGetProperty("OsTarget", out var osProp))
                {
                    var osStr = osProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? osProp.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(osStr) && Enum.TryParse<OsTarget>(osStr, true, out var parsed))
                        osTarget = parsed;
                }

                if (profile.TryGetProperty("RoleTemplate", out var roleProp))
                {
                    var roleStr = roleProp.ValueKind == System.Text.Json.JsonValueKind.String
                        ? roleProp.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(roleStr) && Enum.TryParse<RoleTemplate>(roleStr, true, out var parsedRole))
                        roleTemplate = parsedRole;
                }
            }

            // Also check root-level OsTarget (RunManifest format)
            if (!osTarget.HasValue && doc.RootElement.TryGetProperty("OsTarget", out var rootOs))
            {
                var osStr = rootOs.ValueKind == System.Text.Json.JsonValueKind.String
                    ? rootOs.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(osStr) && Enum.TryParse<OsTarget>(osStr, true, out var parsed))
                    osTarget = parsed;
            }

            if (!roleTemplate.HasValue && doc.RootElement.TryGetProperty("RoleTemplate", out var rootRole))
            {
                var roleStr = rootRole.ValueKind == System.Text.Json.JsonValueKind.String
                    ? rootRole.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(roleStr) && Enum.TryParse<RoleTemplate>(roleStr, true, out var parsedRole))
                    roleTemplate = parsedRole;
            }

            return (osTarget, roleTemplate);
        }
        catch
        {
            return (null, null);
        }
    }

    private static bool HasAnyHardenApplyInput(ApplyRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.PowerStigModulePath)
            || !string.IsNullOrWhiteSpace(request.DscMofPath)
            || !string.IsNullOrWhiteSpace(request.LgpoPolFilePath)
            || !string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath)
            || !string.IsNullOrWhiteSpace(request.ScriptPath);
    }

    private static string BuildMissingHardenArtifactsMessage(string bundleRoot)
    {
        var applyRoot = Path.Combine(bundleRoot, "Apply");
        var dscPath = Path.Combine(applyRoot, "Dsc");
        var lgpoPath = Path.Combine(applyRoot, "GPO", "Machine", "Registry.pol");
        var admxPath = Path.Combine(applyRoot, "ADMX Templates");

        return "Hardening cannot run: no apply artifacts were found under " + applyRoot
            + ". Expected at least one of: PowerSTIG module in Apply, DSC directory " + dscPath
            + ", LGPO policy " + lgpoPath + ", or ADMX templates under " + admxPath + ".";
    }

    private static int StageApplyArtifacts(IReadOnlyList<ImportInboxCandidate> candidates, string outputFolder)
    {
        var applyRoot = Path.Combine(outputFolder, "Apply");
        var staged = 0;
        var osTarget = DetectLocalOsTarget();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.ZipPath) || !File.Exists(candidate.ZipPath))
                continue;

            try
            {
                if (candidate.ArtifactKind == ImportArtifactKind.Gpo
                    || candidate.ArtifactKind == ImportArtifactKind.Admx)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-gpo-" + Guid.NewGuid().ToString("N")[..8]);
                    try
                    {
                        ZipFile.ExtractToDirectory(candidate.ZipPath, tempDir, overwriteFiles: true);
                        GpoPackageExtractor.StageForApply(tempDir, applyRoot, osTarget);
                        staged++;
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
                else if (candidate.ArtifactKind == ImportArtifactKind.Tool
                    && candidate.ToolKind == ToolArtifactKind.PowerStig)
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "stigforge-ps-" + Guid.NewGuid().ToString("N")[..8]);
                    try
                    {
                        ZipFile.ExtractToDirectory(candidate.ZipPath, tempDir, overwriteFiles: true);
                        var psd1 = Directory.EnumerateFiles(tempDir, "PowerSTIG.psd1", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(psd1))
                        {
                            var moduleDir = Path.GetDirectoryName(psd1)!;
                            var destDir = Path.Combine(applyRoot, "PowerSTIG");
                            CopyDirectoryRecursive(moduleDir, destDir);
                            staged++;
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }
            }
            catch
            {
                // Continue staging remaining artifacts if one fails
            }
        }

        return staged;
    }

    private static OsTarget DetectLocalOsTarget()
    {
        if (!OperatingSystem.IsWindows())
            return OsTarget.Unknown;

        try
        {
            var productName = (Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName", null) as string) ?? string.Empty;

            if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0) return OsTarget.Server2022;
                if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0) return OsTarget.Server2019;
            }

            if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0) return OsTarget.Win11;
            if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0) return OsTarget.Win10;
        }
        catch { }

        return OsTarget.Unknown;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }

    private static string? ResolveDscMofPath(string applyRoot)
    {
        var candidate = Path.Combine(applyRoot, "Dsc");
        if (!Directory.Exists(candidate))
            return null;

        var hasRootMof = Directory.EnumerateFiles(candidate, "*.mof", SearchOption.TopDirectoryOnly).Any();
        if (hasRootMof)
            return candidate;

        try
        {
            var osHints = GetOsDscHints();
            var childFolders = Directory.EnumerateDirectories(candidate, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new
                {
                    Path = path,
                    Name = Path.GetFileName(path) ?? string.Empty,
                    HasMof = Directory.EnumerateFiles(path, "*.mof", SearchOption.AllDirectories).Any()
                })
                .Where(x => x.HasMof)
                .ToList();

            if (childFolders.Count == 0)
                return candidate;

            var bestMatch = childFolders
                .Select(x => new
                {
                    x.Path,
                    Score = osHints.Any(h => x.Name.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0) ? 1 : 0
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return bestMatch?.Path ?? candidate;
        }
        catch (IOException)
        {
            return candidate;
        }
        catch (UnauthorizedAccessException)
        {
            return candidate;
        }
    }

    private static string[] GetOsDscHints()
    {
        var hints = new List<string>();
        var version = Environment.OSVersion.Version;
        var majorMinor = $"{version.Major}.{version.Minor}";
        hints.Add(majorMinor);

        if (OperatingSystem.IsWindows())
        {
            hints.Add("windows");
            hints.Add("win");

            var productName = ReadWindowsProductName();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0)
                    hints.Add("11");
                else if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
                    hints.Add("10");

                if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hints.Add("server");
                    if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0) hints.Add("2022");
                    else if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0) hints.Add("2019");
                    else if (productName.IndexOf("2016", StringComparison.OrdinalIgnoreCase) >= 0) hints.Add("2016");
                    else if (productName.IndexOf("2012", StringComparison.OrdinalIgnoreCase) >= 0) hints.Add("2012");
                }
            }
        }

        return hints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadWindowsProductName()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName",
                null);
            return value as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePowerStigDataPath(string applyRoot)
    {
        var candidate = Path.Combine(applyRoot, "PowerStigData", "stigdata.psd1");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolvePowerStigModulePath(string applyRoot)
    {
        if (!Directory.Exists(applyRoot))
            return null;

        try
        {
            var psd1 = Directory.EnumerateFiles(applyRoot, "PowerSTIG.psd1", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(psd1))
                return psd1;

            var psm1 = Directory.EnumerateFiles(applyRoot, "PowerSTIG.psm1", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(psm1))
                return psm1;

            var moduleDirectory = Directory.EnumerateDirectories(applyRoot, "PowerSTIG", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(moduleDirectory))
                return moduleDirectory;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? ResolveLgpoPolFilePath(string applyRoot, out LgpoScope? scope)
    {
        scope = null;
        if (!Directory.Exists(applyRoot))
            return null;

        var candidates = new[]
        {
            Path.Combine(applyRoot, "GPO", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "GPO", "Machine", "registry.pol"),
            Path.Combine(applyRoot, "GPO", "machine.pol"),
            Path.Combine(applyRoot, "LGPO", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "LGPO", "Machine", "registry.pol"),
            Path.Combine(applyRoot, "LGPO", "machine.pol"),
            Path.Combine(applyRoot, "Policies", "Machine", "Registry.pol"),
            Path.Combine(applyRoot, "Policies", "machine.pol")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            scope = ResolveLgpoScope(candidate);
            return candidate;
        }

        try
        {
            var discovered = Directory.EnumerateFiles(applyRoot, "*.pol", SearchOption.AllDirectories)
                .OrderBy(path => ResolveLgpoScope(path) == LgpoScope.Machine ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(discovered))
            {
                scope = ResolveLgpoScope(discovered);
                return discovered;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static LgpoScope ResolveLgpoScope(string polPath)
    {
        if (polPath.IndexOf($"{Path.DirectorySeparatorChar}User{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
            || polPath.IndexOf($"{Path.AltDirectorySeparatorChar}User{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0)
            return LgpoScope.User;

        return LgpoScope.Machine;
    }

    private static string? ResolveLgpoExePath(string bundleRoot)
    {
        var candidate = Path.Combine(bundleRoot, "tools", "LGPO.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveAdmxTemplateRootPath(string applyRoot)
    {
        if (!Directory.Exists(applyRoot))
            return null;

        var directCandidates = new[]
        {
            Path.Combine(applyRoot, "ADMX Templates"),
            Path.Combine(applyRoot, "ADMX"),
            Path.Combine(applyRoot, "PolicyDefinitions")
        };

        foreach (var candidate in directCandidates)
        {
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.admx", SearchOption.AllDirectories).Any())
                return candidate;
        }

        try
        {
            var discovered = Directory.EnumerateFiles(applyRoot, "*.admx", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(discovered))
                return null;

            return Path.GetDirectoryName(discovered);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<bool> RunVerifyAsync()
    {
        StatusText = "Running verification scan (Evaluate-STIG + SCC)...";
        CurrentFailureCard = null;
        ResetComplianceMetrics();

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
            CurrentFailureCard = CreateVerifyElevationRequiredCard("Verification preflight blocked because STIGForge is not running with administrator privileges.");
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            VerifyFindingsCount = 0;
            CurrentFailureCard = CreateVerifyEvaluatePathInvalidCard();
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

        var effectiveSccArguments = string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedSccCommandPath))
        {
            if (!TryBuildSccHeadlessArguments(SccArguments, OutputFolderPath, out effectiveSccArguments, out var sccValidationError))
            {
                StatusText = "SCC arguments are invalid: " + sccValidationError;
                VerifyFindingsCount = 0;
                CurrentFailureCard = CreateVerifyScapArgumentsInvalidCard(sccValidationError);
                return false;
            }

            if (!string.Equals(effectiveSccArguments, SccArguments, StringComparison.Ordinal))
                SccArguments = effectiveSccArguments;
        }

        try
        {
            var request = new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = true,
                    ToolRoot = resolvedEvaluateToolPath,
                    Arguments = BuildEvaluateStigArguments(OutputFolderPath)
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = !string.IsNullOrWhiteSpace(resolvedSccCommandPath),
                    CommandPath = resolvedSccCommandPath ?? string.Empty,
                    Arguments = effectiveSccArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(SccWorkingDirectory) ? null : SccWorkingDirectory,
                    TimeoutSeconds = SccTimeoutSeconds
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, CancellationToken.None),
                CancellationToken.None);

            UpdateComplianceMetrics(result);

            var verifyOpenFindings = result.FailCount + result.ErrorCount;
            VerifyFindingsCount = verifyOpenFindings;
            FixedCount = BaselineFindingsCount - VerifyFindingsCount;
            if (FixedCount < 0) FixedCount = 0;

            if (HasScapDidNothingDiagnostic(result))
            {
              StatusText = "Verification did not complete: SCC ran without usable arguments/output.";
              CurrentFailureCard = CreateVerifyScapNoOutputCard();
              await WriteMissionJsonAsync(result, CancellationToken.None, CurrentFailureCard, "Verify");
              return false;
            }

            if (verifyOpenFindings == 0)
            {
              StatusText = BuildZeroFindingsMessage("Verification scan", result);
              var isFailure = IsZeroFindingsFailure(result);
              WorkflowFailureCard? missionFailureCard = null;
              if (isFailure)
              {
                missionFailureCard = BuildVerifyFailureCard(result);
                CurrentFailureCard = missionFailureCard;
              }
              else
              {
                CurrentFailureCard = null;
              }

              await WriteMissionJsonAsync(result, CancellationToken.None, missionFailureCard, "Verify");

              return !isFailure;
            }

            StatusText = $"Verification complete: {VerifyFindingsCount} remaining ({FixedCount} fixed)";
            CurrentFailureCard = null;
            await WriteMissionJsonAsync(result, CancellationToken.None, null, "Verify");
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Verification failed: {ex.Message}";
            VerifyFindingsCount = 0;
            CurrentFailureCard = CreateVerifyUnknownFailureCard($"Verification failed unexpectedly: {ex.Message}");
            return false;
        }
    }

    private void ResetComplianceMetrics()
    {
        TotalRuleCount = 0;
        CompliancePassCount = 0;
        ComplianceFailCount = 0;
        ComplianceOtherCount = 0;
        CatIVulnerabilityCount = 0;
        CatIIVulnerabilityCount = 0;
        CatIIIVulnerabilityCount = 0;
        TotalCatVulnerabilityCount = 0;
        CompliancePercent = 0;
    }

    private void UpdateComplianceMetrics(VerificationWorkflowResult result)
    {
        if (result == null)
            return;

        TotalRuleCount = result.TotalRuleCount;
        CompliancePassCount = result.PassCount;
        ComplianceFailCount = result.FailCount;
        ComplianceOtherCount = result.NotApplicableCount + result.NotReviewedCount + result.ErrorCount;
        CatIVulnerabilityCount = result.CatICount;
        CatIIVulnerabilityCount = result.CatIICount;
        CatIIIVulnerabilityCount = result.CatIIICount;
        TotalCatVulnerabilityCount = CatIVulnerabilityCount + CatIIVulnerabilityCount + CatIIIVulnerabilityCount;
        var denominator = result.PassCount + result.FailCount + result.ErrorCount;
        CompliancePercent = denominator > 0
            ? (double)result.PassCount / denominator * 100
            : 0;
    }

    private async Task WriteMissionJsonAsync(
        VerificationWorkflowResult result,
        CancellationToken ct,
        WorkflowFailureCard? failureCard = null,
        string stage = "Verify")
    {
        if (string.IsNullOrWhiteSpace(OutputFolderPath))
            throw new InvalidOperationException("Output folder is required to write mission.json");

        Directory.CreateDirectory(OutputFolderPath);

        var missionPath = Path.Combine(OutputFolderPath, "mission.json");
        var mission = new LocalWorkflowMission
        {
            Diagnostics = BuildMissionDiagnostics(result, failureCard, stage),
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

    private static IReadOnlyList<string> BuildMissionDiagnostics(
        VerificationWorkflowResult result,
        WorkflowFailureCard? failureCard,
        string stage)
    {
        var diagnostics = (result.Diagnostics ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        if (failureCard is not null)
        {
            diagnostics.Add($"RootCause={failureCard.RootCauseCode}; Stage={stage}");
        }

        var toolRuns = result.ToolRuns ?? Array.Empty<VerificationToolRunResult>();
        foreach (var run in toolRuns)
        {
            var tool = string.IsNullOrWhiteSpace(run.Tool) ? "unknown" : run.Tool;
            var line = $"ToolRun {tool}: Executed={run.Executed}; ExitCode={run.ExitCode}";

            if (!string.IsNullOrWhiteSpace(run.Error))
                line += $"; Error={run.Error}";

            diagnostics.Add(line);
        }

        return diagnostics;
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

    [RelayCommand(CanExecute = nameof(CanTestSccHeadless))]
    private async Task TestSccHeadlessAsync()
    {
        IsBusy = true;
        CurrentFailureCard = null;

        try
        {
            if (_verifyService == null)
            {
                StatusText = "Verification service not configured";
                return;
            }

            if (!EnsureAdminPreflight(out var adminMessage))
            {
                StatusText = adminMessage;
                CurrentFailureCard = CreateVerifyElevationRequiredCard("SCC headless validation requires administrator privileges.");
                return;
            }

            var resolvedSccCommandPath = ResolveSccCommandPath(SccToolPath);
            if (string.IsNullOrWhiteSpace(resolvedSccCommandPath))
            {
                StatusText = LooksLikeUnsupportedSccGuiPath(SccToolPath)
                    ? "SCC GUI executable (scc.exe) is not supported for automation. Use cscc.exe or cscc-remote.exe."
                    : "SCC tool path is not configured or no cscc.exe/cscc-remote.exe executable was found";
                return;
            }

            if (!string.Equals(resolvedSccCommandPath, SccToolPath, StringComparison.OrdinalIgnoreCase))
                SccToolPath = resolvedSccCommandPath;

            if (string.IsNullOrWhiteSpace(OutputFolderPath))
            {
                StatusText = "Output folder is required for SCC headless validation";
                return;
            }

            Directory.CreateDirectory(OutputFolderPath);

            if (!TryBuildSccHeadlessArguments(SccArguments, OutputFolderPath, out var effectiveSccArguments, out var sccValidationError))
            {
                StatusText = "SCC headless validation failed: " + sccValidationError;
                CurrentFailureCard = CreateVerifyScapArgumentsInvalidCard(sccValidationError);
                return;
            }

            if (!string.Equals(effectiveSccArguments, SccArguments, StringComparison.Ordinal))
                SccArguments = effectiveSccArguments;

            StatusText = "Running SCC headless validation...";

            var request = new VerificationWorkflowRequest
            {
                OutputRoot = OutputFolderPath,
                ConsolidatedToolLabel = "SCC Headless Test",
                EvaluateStig = new EvaluateStigWorkflowOptions
                {
                    Enabled = false
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = true,
                    CommandPath = resolvedSccCommandPath,
                    Arguments = effectiveSccArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(SccWorkingDirectory) ? null : SccWorkingDirectory,
                    TimeoutSeconds = SccTimeoutSeconds,
                    ToolLabel = "SCC"
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, CancellationToken.None),
                CancellationToken.None);

            var scapRun = result.ToolRuns?.FirstOrDefault(run =>
                string.Equals(run.Tool, "SCC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(run.Tool, "SCAP", StringComparison.OrdinalIgnoreCase));

            if (scapRun is null || !scapRun.Executed)
            {
                StatusText = "SCC headless validation failed: SCC did not execute.";
                return;
            }

            if (scapRun.ExitCode != 0)
            {
                StatusText = $"SCC headless validation failed: SCC exited with code {scapRun.ExitCode}.";
                return;
            }

            if (HasScapDidNothingDiagnostic(result))
            {
                StatusText = "SCC headless validation failed: SCC ran but produced no SCAP artifacts.";
                CurrentFailureCard = CreateVerifyScapNoOutputCard();
                return;
            }

            StatusText = "SCC headless validation passed: SCC executed and produced SCAP artifacts.";
            CurrentFailureCard = null;
        }
        catch (Exception ex)
        {
            StatusText = $"SCC headless validation failed: {ex.Message}";
            CurrentFailureCard = CreateVerifyUnknownFailureCard($"SCC headless validation failed unexpectedly: {ex.Message}");
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
        EvaluateAfPath = settings.EvaluateAfPath;
        EvaluateSelectStig = settings.EvaluateSelectStig;
        EvaluateAdditionalArgs = settings.EvaluateAdditionalArgs;
        SccToolPath = settings.SccToolPath;
        SccArguments = settings.SccArguments;
        SccWorkingDirectory = settings.SccWorkingDirectory;
        SccTimeoutSeconds = settings.SccTimeoutSeconds;
        OutputFolderPath = settings.OutputFolderPath;
        MachineTarget = settings.MachineTarget;
        RequireElevationForScan = settings.RequireElevationForScan;
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
            EvaluateAfPath = EvaluateAfPath,
            EvaluateSelectStig = EvaluateSelectStig,
            EvaluateAdditionalArgs = EvaluateAdditionalArgs,
            SccToolPath = SccToolPath,
            SccArguments = SccArguments,
            SccWorkingDirectory = SccWorkingDirectory,
            SccTimeoutSeconds = SccTimeoutSeconds,
            OutputFolderPath = OutputFolderPath,
            MachineTarget = MachineTarget,
            RequireElevationForScan = RequireElevationForScan,
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
