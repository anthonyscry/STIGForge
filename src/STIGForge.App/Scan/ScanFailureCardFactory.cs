using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.App;

internal static class ScanFailureCardFactory
{
    internal static VerificationToolRunResult? FindEvaluateRun(VerificationWorkflowResult result)
    {
        return result.ToolRuns?.FirstOrDefault(run =>
            string.Equals(run.Tool, "Evaluate-STIG", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasNoCklDiagnostic(VerificationWorkflowResult result)
    {
        var diagnostics = result.Diagnostics ?? Array.Empty<string>();
        return diagnostics.Any(diagnostic =>
            diagnostic.Contains("No CKL results", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasScapDidNothingDiagnostic(VerificationWorkflowResult result)
    {
        var diagnostics = result.Diagnostics ?? Array.Empty<string>();
        return diagnostics.Any(diagnostic =>
            diagnostic.Contains("SCAP arguments were empty", StringComparison.OrdinalIgnoreCase)
            || diagnostic.Contains("produced no SCAP artifacts", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsZeroFindingsFailure(VerificationWorkflowResult result)
    {
        if (!HasAnyResults(result))
            return true;

        var evaluateRun = FindEvaluateRun(result);
        if (evaluateRun is { Executed: true } && evaluateRun.ExitCode != 0)
            return true;

        return HasNoCklDiagnostic(result);
    }

    internal static bool HasAnyResults(VerificationWorkflowResult result)
    {
        if (result == null)
            return false;

        if (result.TotalRuleCount > 0)
            return true;

        return result.ConsolidatedResultCount > 0;
    }

    internal static string BuildZeroFindingsMessage(string operationName, VerificationWorkflowResult result)
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

    internal static WorkflowFailureCard CreateFailureCard(
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

    internal static WorkflowFailureCard BuildScanFailureCard(VerificationWorkflowResult result)
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

    internal static WorkflowFailureCard BuildVerifyFailureCard(VerificationWorkflowResult result)
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

    internal static WorkflowFailureCard CreateEvaluatePathInvalidCard()
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.EvaluatePathInvalid,
            "Evaluate-STIG path is invalid",
            "The configured Evaluate-STIG location does not contain a usable Evaluate-STIG.ps1 script.",
            "Open Settings, correct the Evaluate-STIG path, save, and rerun Scan.",
            showOpenSettingsAction: true,
            showRetryScanAction: true);
    }

    internal static WorkflowFailureCard CreateVerifyEvaluatePathInvalidCard()
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.EvaluatePathInvalid,
            "Evaluate-STIG path is invalid",
            "The configured Evaluate-STIG location does not contain a usable Evaluate-STIG.ps1 script.",
            "Open Settings, correct the Evaluate-STIG path, save, and rerun Verify.",
            showOpenSettingsAction: true,
            showRetryVerifyAction: true);
    }

    internal static WorkflowFailureCard CreateElevationRequiredCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.ElevationRequired,
            "Administrator privileges required",
            whatHappened,
            "Relaunch STIGForge as administrator and rerun Scan.",
            showRetryScanAction: true);
    }

    internal static WorkflowFailureCard CreateVerifyElevationRequiredCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.ElevationRequired,
            "Administrator privileges required",
            whatHappened,
            "Relaunch STIGForge as administrator and rerun Verify.",
            showRetryVerifyAction: true);
    }

    internal static WorkflowFailureCard CreateVerifyUnknownFailureCard(string whatHappened)
    {
        return CreateFailureCard(
            WorkflowRootCauseCode.UnknownFailure,
            "Verification could not be completed",
            whatHappened,
            "Review diagnostics in mission output and rerun Verify.",
            showRetryVerifyAction: true,
            showOpenOutputFolderAction: true);
    }

    internal static WorkflowFailureCard CreateVerifyScapNoOutputCard()
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

    internal static WorkflowFailureCard CreateVerifyScapArgumentsInvalidCard(string details)
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
}
