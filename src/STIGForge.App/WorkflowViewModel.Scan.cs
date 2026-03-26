using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

public partial class WorkflowViewModel
{
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

    // Output location is controlled via WorkingDirectory on the process, not a CLI argument.
    private string BuildEvaluateStigArguments()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(EvaluateAfPath))
            parts.Add("-AFPath " + SccArgumentParser.QuoteCommandLineArgument(EvaluateAfPath.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateSelectStig))
            parts.Add("-SelectSTIG " + SccArgumentParser.QuoteCommandLineArgument(EvaluateSelectStig.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateAdditionalArgs))
            parts.Add(EvaluateAdditionalArgs.Trim());

        parts.Add("-Output CKL");

        var target = MachineTarget?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "localhost", StringComparison.OrdinalIgnoreCase))
            parts.Add("-ComputerName " + SccArgumentParser.QuoteCommandLineArgument(target));

        return string.Join(" ", parts);
    }

    private async Task<bool> RunScanAsync()
    {
        StatusText = "Running Evaluate-STIG baseline scan...";
        CurrentFailureCard = null;
        VerifyComplianceText = string.Empty;
        InvalidateScanComplianceBaseline();

        if (_verifyService == null)
        {
            StatusText = "Verification service not configured";
            BaselineFindingsCount = 0;
            ScanComplianceText = string.Empty;
            return false;
        }

        if (ImportedItemsCount == 0)
        {
            StatusText = "No imported content detected. Run Import and confirm items in Imported Library.";
            BaselineFindingsCount = 0;
            ScanComplianceText = string.Empty;
            return false;
        }

        if (!EnsureAdminPreflight(out var adminMessage))
        {
            StatusText = adminMessage;
            BaselineFindingsCount = 0;
            CurrentFailureCard = ScanFailureCardFactory.CreateElevationRequiredCard("Baseline scan preflight blocked because STIGForge is not running with administrator privileges.");
            ScanComplianceText = string.Empty;
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            BaselineFindingsCount = 0;
            CurrentFailureCard = ScanFailureCardFactory.CreateEvaluatePathInvalidCard();
            ScanComplianceText = string.Empty;
            return false;
        }

        if (!string.Equals(resolvedEvaluateToolPath, EvaluateStigToolPath, StringComparison.OrdinalIgnoreCase))
            EvaluateStigToolPath = resolvedEvaluateToolPath;

        if (string.IsNullOrWhiteSpace(OutputFolderPath))
        {
            StatusText = "Output folder is required for scanning";
            BaselineFindingsCount = 0;
            ScanComplianceText = string.Empty;
            InvalidateScanComplianceBaseline();
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
                    Arguments = BuildEvaluateStigArguments(),
                    WorkingDirectory = OutputFolderPath
                },
                Scap = new ScapWorkflowOptions
                {
                    Enabled = false
                }
            };

            var result = await Task.Run(
                () => _verifyService.RunAsync(request, _cts.Token),
                _cts.Token);

            var baselineOpenFindings = result.FailCount + result.ErrorCount;
            BaselineFindingsCount = baselineOpenFindings;

            var scanDenom = result.PassCount + result.FailCount + result.ErrorCount;
            var scanPct = scanDenom > 0 ? (double)result.PassCount / scanDenom * 100 : 0;
            ScanComplianceText = $"Baseline: {scanPct:F1}% compliant ({result.PassCount}/{scanDenom})";

            if (baselineOpenFindings == 0)
            {
                StatusText = ScanFailureCardFactory.BuildZeroFindingsMessage("Baseline scan", result);
                var isFailure = ScanFailureCardFactory.IsZeroFindingsFailure(result);
                CurrentFailureCard = isFailure ? ScanFailureCardFactory.BuildScanFailureCard(result) : null;
                if (isFailure)
                {
                    InvalidateScanComplianceBaseline();
                }
                else
                {
                    SetScanComplianceBaseline(scanPct, scanDenom);
                }

                return !isFailure;
            }

            SetScanComplianceBaseline(scanPct, scanDenom);
            StatusText = $"Baseline scan complete: {BaselineFindingsCount} findings";
            CurrentFailureCard = null;
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            BaselineFindingsCount = 0;
            ScanComplianceText = string.Empty;
            return false;
        }
    }

    private async Task<bool> RunVerifyAsync()
    {
        StatusText = "Running verification scan (Evaluate-STIG + SCC)...";
        CurrentFailureCard = null;
        VerifyComplianceText = string.Empty;
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
            CurrentFailureCard = ScanFailureCardFactory.CreateVerifyElevationRequiredCard("Verification preflight blocked because STIGForge is not running with administrator privileges.");
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            VerifyFindingsCount = 0;
            CurrentFailureCard = ScanFailureCardFactory.CreateVerifyEvaluatePathInvalidCard();
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
            if (!SccArgumentParser.TryBuildSccHeadlessArguments(SccArguments, OutputFolderPath, SccOutputSwitches, out effectiveSccArguments, out var sccValidationError))
            {
                StatusText = "SCC arguments are invalid: " + sccValidationError;
                VerifyFindingsCount = 0;
                CurrentFailureCard = ScanFailureCardFactory.CreateVerifyScapArgumentsInvalidCard(sccValidationError);
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
                    Arguments = BuildEvaluateStigArguments(),
                    WorkingDirectory = OutputFolderPath
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
                () => _verifyService.RunAsync(request, _cts.Token),
                _cts.Token);

            UpdateComplianceMetrics(result);

            var verifyDenom = result.PassCount + result.FailCount + result.ErrorCount;
            var verifyPct = verifyDenom > 0 ? (double)result.PassCount / verifyDenom * 100 : 0;
            VerifyComplianceText = BuildVerifyComplianceText(verifyPct, result.PassCount, verifyDenom);

            var verifyOpenFindings = result.FailCount + result.ErrorCount;
            VerifyFindingsCount = verifyOpenFindings;
            FixedCount = BaselineFindingsCount - VerifyFindingsCount;
            if (FixedCount < 0)
                FixedCount = 0;

            if (ScanFailureCardFactory.HasScapDidNothingDiagnostic(result))
            {
                StatusText = "Verification did not complete: SCC ran without usable arguments/output.";
                CurrentFailureCard = ScanFailureCardFactory.CreateVerifyScapNoOutputCard();
                await WriteMissionJsonAsync(result, _cts.Token, CurrentFailureCard, "Verify");
                return false;
            }

            if (verifyOpenFindings == 0)
            {
                StatusText = ScanFailureCardFactory.BuildZeroFindingsMessage("Verification scan", result);
                var isFailure = ScanFailureCardFactory.IsZeroFindingsFailure(result);
                WorkflowFailureCard? missionFailureCard = null;
                if (isFailure)
                {
                    missionFailureCard = ScanFailureCardFactory.BuildVerifyFailureCard(result);
                    CurrentFailureCard = missionFailureCard;
                }
                else
                {
                    CurrentFailureCard = null;
                }

                await WriteMissionJsonAsync(result, _cts.Token, missionFailureCard, "Verify");
                return !isFailure;
            }

            StatusText = $"Verification complete: {VerifyFindingsCount} remaining ({FixedCount} fixed)";
            CurrentFailureCard = null;
            await WriteMissionJsonAsync(result, _cts.Token, null, "Verify");
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Verification failed: {ex.Message}";
            VerifyFindingsCount = 0;
            CurrentFailureCard = ScanFailureCardFactory.CreateVerifyUnknownFailureCard($"Verification failed unexpectedly: {ex.Message}");
            return false;
        }
    }

    private void SetScanComplianceBaseline(double scanPercent, int scanDenominator)
    {
        _scanBaselineComplianceIsValid = true;
        _scanBaselineCompliancePercent = scanPercent;
        _scanBaselineComplianceDenominator = scanDenominator;
    }

    private void InvalidateScanComplianceBaseline()
    {
        _scanBaselineComplianceIsValid = false;
        _scanBaselineCompliancePercent = 0;
        _scanBaselineComplianceDenominator = 0;
    }

    private string BuildVerifyComplianceText(double verifyPercent, int verifyPass, int verifyDenominator)
    {
        var complianceText = $"{verifyPercent:F1}% compliant ({verifyPass}/{verifyDenominator})";
        if (!_scanBaselineComplianceIsValid || _scanBaselineComplianceDenominator == 0)
            return complianceText;

        var delta = verifyPercent - _scanBaselineCompliancePercent;
        return $"{complianceText} | Delta vs baseline: {delta:+0.0;-0.0;0.0} pp";
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

        var json = JsonSerializer.Serialize(mission, JsonOptions.Indented);

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
            diagnostics.Add($"RootCause={failureCard.RootCauseCode}; Stage={stage}");

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

    [RelayCommand(CanExecute = nameof(CanRunScan))]
    private async Task RunScanStepAsync()
    {
        IsBusy = true;
        ScanState = StepState.Running;
        ScanError = string.Empty;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var success = await RunScanAsync();
            if (success)
            {
                ScanState = StepState.Complete;
                if (HardenState == StepState.Locked)
                    HardenState = StepState.Ready;
            }
            else
            {
                ScanState = StepState.Error;
                ScanError = StatusText;
            }
        }
        finally
        {
            stopwatch.Stop();
            LastScanDurationMs = stopwatch.ElapsedMilliseconds;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSkipScan))]
    private void SkipScanStep()
    {
        ScanState = StepState.Complete;
        ScanError = string.Empty;
        BaselineFindingsCount = 0;
        ScanComplianceText = string.Empty;
        VerifyComplianceText = string.Empty;
        InvalidateScanComplianceBaseline();
        StatusText = "Baseline scan skipped by operator — proceeding to harden";

        if (HardenState == StepState.Locked)
            HardenState = StepState.Ready;
    }

    [RelayCommand(CanExecute = nameof(CanRunVerify))]
    private async Task RunVerifyStepAsync()
    {
        IsBusy = true;
        VerifyState = StepState.Running;
        VerifyError = string.Empty;
        var stopwatch = Stopwatch.StartNew();
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
            stopwatch.Stop();
            LastVerifyDurationMs = stopwatch.ElapsedMilliseconds;
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
                CurrentFailureCard = ScanFailureCardFactory.CreateVerifyElevationRequiredCard("SCC headless validation requires administrator privileges.");
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

            if (!SccArgumentParser.TryBuildSccHeadlessArguments(SccArguments, OutputFolderPath, SccOutputSwitches, out var effectiveSccArguments, out var sccValidationError))
            {
                StatusText = "SCC headless validation failed: " + sccValidationError;
                CurrentFailureCard = ScanFailureCardFactory.CreateVerifyScapArgumentsInvalidCard(sccValidationError);
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
                () => _verifyService.RunAsync(request, _cts.Token),
                _cts.Token);

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

            if (ScanFailureCardFactory.HasScapDidNothingDiagnostic(result))
            {
                StatusText = "SCC headless validation failed: SCC ran but produced no SCAP artifacts.";
                CurrentFailureCard = ScanFailureCardFactory.CreateVerifyScapNoOutputCard();
                return;
            }

            StatusText = "SCC headless validation passed: SCC executed and produced SCAP artifacts.";
            CurrentFailureCard = null;
        }
        catch (Exception ex)
        {
            StatusText = $"SCC headless validation failed: {ex.Message}";
            CurrentFailureCard = ScanFailureCardFactory.CreateVerifyUnknownFailureCard($"SCC headless validation failed unexpectedly: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
