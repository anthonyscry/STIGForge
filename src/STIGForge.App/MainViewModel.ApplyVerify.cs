using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Verify;

namespace STIGForge.App;

public partial class MainViewModel
{
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
      if (ApplySkipSnapshot)
        await ValidateAndRecordBreakGlassAsync("apply-run", BundleRoot, "skip-snapshot", CancellationToken.None);

      var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = BundleRoot,
        ScriptPath = script,
        ScriptArgs = "-BundleRoot \"" + BundleRoot + "\"",
        SkipSnapshot = ApplySkipSnapshot
      }, CancellationToken.None);

      ApplyStatus = "Apply complete: " + result.LogPath;
      LastOutputPath = result.LogPath;
      ReportSummary = BuildReportSummary(BundleRoot);
      RefreshDashboard();
    }
    catch (Exception ex)
    {
      ApplyStatus = "Apply failed: " + ex.Message;
      if (!string.IsNullOrWhiteSpace(BundleRoot))
        ApplyStatus += " " + BuildApplyRecoveryGuidance(BundleRoot);
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
      var coverageInputs = new List<VerificationCoverageInput>();

      if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
      {
        var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
        var evalWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
        {
          OutputRoot = evalOutput,
          ConsolidatedToolLabel = "Evaluate-STIG",
          EvaluateStig = new EvaluateStigWorkflowOptions
          {
            Enabled = true,
            ToolRoot = EvaluateStigRoot,
            Arguments = EvaluateStigArgs ?? string.Empty,
            WorkingDirectory = EvaluateStigRoot
          }
        }, CancellationToken.None);

        coverageInputs.Add(new VerificationCoverageInput
        {
          ToolLabel = "Evaluate-STIG",
          ReportPath = evalWorkflow.ConsolidatedJsonPath
        });
      }

      if (!string.IsNullOrWhiteSpace(ScapCommandPath))
      {
        var scapOutput = Path.Combine(verifyRoot, "SCAP");
        var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? "SCAP" : ScapLabel;
        var scapWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
        {
          OutputRoot = scapOutput,
          ConsolidatedToolLabel = toolName,
          Scap = new ScapWorkflowOptions
          {
            Enabled = true,
            CommandPath = ScapCommandPath,
            Arguments = ScapArgs ?? string.Empty,
            ToolLabel = toolName
          }
        }, CancellationToken.None);

        coverageInputs.Add(new VerificationCoverageInput
        {
          ToolLabel = toolName,
          ReportPath = scapWorkflow.ConsolidatedJsonPath
        });
      }

      if (coverageInputs.Count > 0)
        _artifactAggregation.WriteCoverageArtifacts(Path.Combine(BundleRoot, "Reports"), coverageInputs);

      VerifyStatus = "Verify complete.";
      LastOutputPath = Path.Combine(BundleRoot, "Verify");
      ReportSummary = BuildReportSummary(BundleRoot);
      VerifySummary = ReportSummary;
      LoadCoverageOverlap();
      RefreshDashboard();
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

      if (result.ValidationResult == null)
      {
        ExportStatus = "Exported: " + result.OutputRoot;
      }
      else
      {
        var verdict = result.ValidationResult.IsValid ? "VALID" : "INVALID (BLOCKING)";
        ExportStatus = "Exported: " + result.OutputRoot
          + " | Validation: " + verdict
          + " (errors=" + result.ValidationResult.Errors.Count
          + ", warnings=" + result.ValidationResult.Warnings.Count + ")";
      }

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
  private async Task Orchestrate()
  {
    if (IsBusy) return;
    if (string.IsNullOrWhiteSpace(BundleRoot))
    {
      OrchStatus = "Select a bundle first.";
      return;
    }

    try
    {
      IsBusy = true;
      OrchLog = "";
      OrchStatus = "Starting orchestration...";
      var log = new StringBuilder();

      // Step 1: Apply
      var blockingFailures = new List<string>();

      if (OrchRunApply)
      {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === APPLY ===");
        OrchStatus = "Running Apply...";
        OrchLog = log.ToString();

        var script = Path.Combine(BundleRoot, "Apply", "RunApply.ps1");
        if (!File.Exists(script))
        {
          log.AppendLine("  SKIP: RunApply.ps1 not found in bundle.");
        }
        else
        {
          try
          {
            if (ApplySkipSnapshot)
              await ValidateAndRecordBreakGlassAsync("orchestrate", BundleRoot, "skip-snapshot", CancellationToken.None);

            var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
            {
              BundleRoot = BundleRoot,
              ScriptPath = script,
              ScriptArgs = "-BundleRoot \"" + BundleRoot + "\"",
              SkipSnapshot = ApplySkipSnapshot
            }, CancellationToken.None);

            log.AppendLine("  OK: Apply complete. Log: " + result.LogPath);
            if (!result.IsMissionComplete)
              log.AppendLine("  INFO: Apply paused for reboot/resume continuation.");
            LastOutputPath = result.LogPath;
          }
          catch (Exception ex)
          {
            log.AppendLine("  BLOCKING: Apply failed: " + ex.Message);
            blockingFailures.Add("Apply: " + ex.Message);
          }
        }
        OrchLog = log.ToString();
      }

      // Step 2: Verify
      var coverageInputs = new List<VerificationCoverageInput>();
      if (OrchRunVerify)
      {
        if (blockingFailures.Count > 0)
        {
          log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === VERIFY ===");
          log.AppendLine("  SKIP: Verification skipped due to earlier blocking failures.");
          OrchLog = log.ToString();
        }
        else
        {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === VERIFY ===");
        OrchStatus = "Running Verify...";
        OrchLog = log.ToString();

        var verifyRoot = Path.Combine(BundleRoot, "Verify");
        Directory.CreateDirectory(verifyRoot);

        if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
        {
          try
          {
            log.AppendLine("  Running Evaluate-STIG...");
            OrchLog = log.ToString();
            var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
            var evalWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
            {
              OutputRoot = evalOutput,
              ConsolidatedToolLabel = "Evaluate-STIG",
              EvaluateStig = new EvaluateStigWorkflowOptions
              {
                Enabled = true,
                ToolRoot = EvaluateStigRoot,
                Arguments = EvaluateStigArgs ?? string.Empty,
                WorkingDirectory = EvaluateStigRoot
              }
            }, CancellationToken.None);
            coverageInputs.Add(new VerificationCoverageInput
            {
              ToolLabel = "Evaluate-STIG",
              ReportPath = evalWorkflow.ConsolidatedJsonPath
            });
            log.AppendLine("  OK: Evaluate-STIG complete. " + evalWorkflow.ConsolidatedResultCount + " results.");
          }
          catch (Exception ex)
          {
            log.AppendLine("  WARN: Evaluate-STIG failed: " + ex.Message);
          }
        }

        if (!string.IsNullOrWhiteSpace(ScapCommandPath))
        {
          try
          {
            log.AppendLine("  Running SCAP...");
            OrchLog = log.ToString();
            var scapOutput = Path.Combine(verifyRoot, "SCAP");
            var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? "SCAP" : ScapLabel;
            var scapWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
            {
              OutputRoot = scapOutput,
              ConsolidatedToolLabel = toolName,
              Scap = new ScapWorkflowOptions
              {
                Enabled = true,
                CommandPath = ScapCommandPath,
                Arguments = ScapArgs ?? string.Empty,
                ToolLabel = toolName
              }
            }, CancellationToken.None);
            coverageInputs.Add(new VerificationCoverageInput
            {
              ToolLabel = toolName,
              ReportPath = scapWorkflow.ConsolidatedJsonPath
            });
            log.AppendLine("  OK: SCAP complete. " + scapWorkflow.ConsolidatedResultCount + " results.");
          }
          catch (Exception ex)
          {
            log.AppendLine("  WARN: SCAP failed: " + ex.Message);
          }
        }

        if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath))
        {
          log.AppendLine("  SKIP: No verify tools configured.");
        }

        if (coverageInputs.Count > 0)
        {
          try
          {
            var artifacts = _artifactAggregation.WriteCoverageArtifacts(Path.Combine(BundleRoot, "Reports"), coverageInputs);
            log.AppendLine("  OK: Coverage artifacts written: " + artifacts.CoverageOverlapCsvPath);
          }
          catch (Exception ex)
          {
            log.AppendLine("  WARN: Coverage artifact aggregation failed: " + ex.Message);
          }
        }

        OrchLog = log.ToString();
        }
      }

      // Step 3: Export eMASS
      if (OrchRunExport)
      {
        if (blockingFailures.Count > 0)
        {
          log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === EXPORT eMASS ===");
          log.AppendLine("  SKIP: eMASS export skipped due to earlier blocking failures.");
          OrchLog = log.ToString();
        }
        else
        {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === EXPORT eMASS ===");
        OrchStatus = "Exporting eMASS...";
        OrchLog = log.ToString();

        try
        {
          var result = await _emassExporter.ExportAsync(new STIGForge.Export.ExportRequest
          {
            BundleRoot = BundleRoot
          }, CancellationToken.None);

          log.AppendLine("  OK: eMASS exported to " + result.OutputRoot);
          if (result.ValidationResult != null)
          {
            log.AppendLine("  Validation: " + (result.ValidationResult.IsValid ? "VALID" : "INVALID")
              + " (errors=" + result.ValidationResult.Errors.Count
              + ", warnings=" + result.ValidationResult.Warnings.Count + ")");

            if (!result.IsReadyForSubmission)
            {
              var blockingMessage = "Export readiness blocked. Resolve package validation errors before mission completion.";
              log.AppendLine("  BLOCKING: " + blockingMessage);
              blockingFailures.Add(blockingMessage);
            }

            if (!string.IsNullOrWhiteSpace(result.ValidationReportPath))
              log.AppendLine("  Validation report: " + result.ValidationReportPath);
            if (!string.IsNullOrWhiteSpace(result.ValidationReportJsonPath))
              log.AppendLine("  Validation report (json): " + result.ValidationReportJsonPath);
          }

          LastOutputPath = result.OutputRoot;
        }
        catch (Exception ex)
        {
          log.AppendLine("  BLOCKING: Export failed: " + ex.Message);
          blockingFailures.Add("Export: " + ex.Message);
        }

        OrchLog = log.ToString();
        }
      }

      // Done
      log.AppendLine();
      log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === COMPLETE ===");
      if (blockingFailures.Count > 0)
      {
        log.AppendLine("  Mission completion blocked:");
        foreach (var failure in blockingFailures)
          log.AppendLine("  - " + failure);
        log.AppendLine("  Recovery guidance: " + BuildApplyRecoveryGuidance(BundleRoot));
      }
      OrchLog = log.ToString();
      OrchStatus = blockingFailures.Count == 0
        ? "Orchestration complete."
        : "Orchestration blocked - operator decision required. " + BuildApplyRecoveryGuidance(BundleRoot);

      // Refresh dashboard
      ReportSummary = BuildReportSummary(BundleRoot);
      LoadCoverageOverlap();
      RefreshDashboard();
    }
    catch (Exception ex)
    {
      OrchStatus = "Orchestration failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private string BuildReportSummary(string bundleRoot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot))
      return string.Empty;

    try
    {
      var summary = _bundleMissionSummary.LoadSummary(bundleRoot);
      var summaryLine = "Summary: total=" + summary.Verify.TotalCount
        + " closed=" + summary.Verify.ClosedCount
        + " open=" + summary.Verify.OpenCount
        + " reports=" + summary.Verify.ReportCount
        + " blocking=" + summary.Verify.BlockingFailureCount
        + " warnings=" + summary.Verify.RecoverableWarningCount
        + " skipped=" + summary.Verify.OptionalSkipCount;

      var severityLine = BuildMissionSeverityLine(summary);
      var guidanceLine = BuildMissionRecoveryGuidance(summary, bundleRoot);
      return summaryLine + Environment.NewLine + severityLine + Environment.NewLine + guidanceLine;
    }
    catch (Exception ex)
    {
      return "Summary unavailable: " + ex.Message;
    }
  }

  private static string BuildApplyRecoveryGuidance(string bundleRoot)
  {
    var applyLog = Path.Combine(bundleRoot, "Apply", "apply_run.json");
    var snapshotsDir = Path.Combine(bundleRoot, "Apply", "Snapshots");
    var rollbackGuidance = GetRollbackGuidance(bundleRoot);

    return "Required artifacts: " + applyLog
      + (Directory.Exists(snapshotsDir) ? ", " + snapshotsDir : string.Empty)
      + ". Next action: review blocking failures, fix prerequisites, and rerun apply/verify. "
      + rollbackGuidance;
  }

  private async Task ValidateAndRecordBreakGlassAsync(string action, string target, string bypassName, CancellationToken ct)
  {
    if (!BreakGlassAcknowledged)
      throw new InvalidOperationException($"{action} {bypassName} is high risk. Acknowledge break-glass before continuing.");

    try
    {
      _manualAnswerService.ValidateBreakGlassReason(BreakGlassReason);
    }
    catch (ArgumentException ex)
    {
      throw new InvalidOperationException($"{action} {bypassName} requires a specific break-glass reason.", ex);
    }

    if (_audit == null)
      return;

    await _audit.RecordAsync(new AuditEntry
    {
      Action = "break-glass",
      Target = string.IsNullOrWhiteSpace(target) ? action : target,
      Result = "acknowledged",
      Detail = $"Action={action}; Bypass={bypassName}; Reason={BreakGlassReason.Trim()}",
      User = Environment.UserName,
      Machine = Environment.MachineName,
      Timestamp = DateTimeOffset.Now
    }, ct).ConfigureAwait(false);
  }
}
