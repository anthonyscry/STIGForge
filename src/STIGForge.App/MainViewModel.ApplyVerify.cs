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
      var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
      {
        BundleRoot = BundleRoot,
        ScriptPath = script,
        ScriptArgs = "-BundleRoot \"" + BundleRoot + "\""
      }, CancellationToken.None);

      ApplyStatus = "Apply complete: " + result.LogPath;
      LastOutputPath = result.LogPath;
    }
    catch (Exception ex)
    {
      ApplyStatus = "Apply failed: " + ex.Message;
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
        var verdict = result.ValidationResult.IsValid ? "VALID" : "INVALID";
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
            var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
            {
              BundleRoot = BundleRoot,
              ScriptPath = script,
              ScriptArgs = "-BundleRoot \"" + BundleRoot + "\""
            }, CancellationToken.None);

            log.AppendLine("  OK: Apply complete. Log: " + result.LogPath);
            LastOutputPath = result.LogPath;
          }
          catch (Exception ex)
          {
            log.AppendLine("  WARN: Apply failed: " + ex.Message);
          }
        }
        OrchLog = log.ToString();
      }

      // Step 2: Verify
      var coverageInputs = new List<VerificationCoverageInput>();
      if (OrchRunVerify)
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

      // Step 3: Export eMASS
      if (OrchRunExport)
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

            if (!string.IsNullOrWhiteSpace(result.ValidationReportPath))
              log.AppendLine("  Validation report: " + result.ValidationReportPath);
            if (!string.IsNullOrWhiteSpace(result.ValidationReportJsonPath))
              log.AppendLine("  Validation report (json): " + result.ValidationReportJsonPath);
          }

          LastOutputPath = result.OutputRoot;
        }
        catch (Exception ex)
        {
          log.AppendLine("  WARN: Export failed: " + ex.Message);
        }

        OrchLog = log.ToString();
      }

      // Done
      log.AppendLine();
      log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === COMPLETE ===");
      OrchLog = log.ToString();
      OrchStatus = "Orchestration complete.";

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
      return "Summary: total=" + summary.Verify.TotalCount
        + " closed=" + summary.Verify.ClosedCount
        + " open=" + summary.Verify.OpenCount
        + " reports=" + summary.Verify.ReportCount;
    }
    catch (Exception ex)
    {
      return "Summary unavailable: " + ex.Message;
    }
  }
}
