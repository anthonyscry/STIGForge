using System.Diagnostics;
using System.IO;
using System.Text;
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

    // Output location is controlled via WorkingDirectory on the process, not a CLI argument.
    private string BuildEvaluateStigArguments()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(EvaluateAfPath))
            parts.Add("-AFPath " + QuoteCommandLineArgument(EvaluateAfPath.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateSelectStig))
            parts.Add("-SelectSTIG " + QuoteCommandLineArgument(EvaluateSelectStig.Trim()));

        if (!string.IsNullOrWhiteSpace(EvaluateAdditionalArgs))
            parts.Add(EvaluateAdditionalArgs.Trim());

        parts.Add("-Output CKL");

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
            CurrentFailureCard = CreateElevationRequiredCard("Baseline scan preflight blocked because STIGForge is not running with administrator privileges.");
            ScanComplianceText = string.Empty;
            return false;
        }

        var resolvedEvaluateToolPath = ResolveEvaluateStigToolRoot(EvaluateStigToolPath);
        if (string.IsNullOrWhiteSpace(resolvedEvaluateToolPath))
        {
            StatusText = "Evaluate-STIG tool path is not configured or invalid";
            BaselineFindingsCount = 0;
            CurrentFailureCard = CreateEvaluatePathInvalidCard();
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
                StatusText = BuildZeroFindingsMessage("Baseline scan", result);
                var isFailure = IsZeroFindingsFailure(result);
                CurrentFailureCard = isFailure ? BuildScanFailureCard(result) : null;
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

            if (HasScapDidNothingDiagnostic(result))
            {
                StatusText = "Verification did not complete: SCC ran without usable arguments/output.";
                CurrentFailureCard = CreateVerifyScapNoOutputCard();
                await WriteMissionJsonAsync(result, _cts.Token, CurrentFailureCard, "Verify");
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
            CurrentFailureCard = CreateVerifyUnknownFailureCard($"Verification failed unexpectedly: {ex.Message}");
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
}
