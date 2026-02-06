using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;

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

      if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
      {
        var evalRunner = new STIGForge.Verify.EvaluateStigRunner();
        var evalResult = evalRunner.Run(EvaluateStigRoot, EvaluateStigArgs ?? string.Empty, EvaluateStigRoot);
        var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
        Directory.CreateDirectory(evalOutput);
        var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(evalOutput, "Evaluate-STIG");
        report.StartedAt = evalResult.StartedAt;
        report.FinishedAt = evalResult.FinishedAt;
        STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(evalOutput, "consolidated-results.json"), report);
        STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(evalOutput, "consolidated-results.csv"), report.Results);
      }

      if (!string.IsNullOrWhiteSpace(ScapCommandPath))
      {
        var scapRunner = new STIGForge.Verify.ScapRunner();
        var scapResult = scapRunner.Run(ScapCommandPath, ScapArgs ?? string.Empty, null);
        var scapOutput = Path.Combine(verifyRoot, "SCAP");
        Directory.CreateDirectory(scapOutput);
        var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? "SCAP" : ScapLabel;
        var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(scapOutput, toolName);
        report.StartedAt = scapResult.StartedAt;
        report.FinishedAt = scapResult.FinishedAt;
        STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(scapOutput, "consolidated-results.json"), report);
        STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(scapOutput, "consolidated-results.csv"), report.Results);
      }

      VerifyStatus = "Verify complete.";
      LastOutputPath = Path.Combine(BundleRoot, "Verify");
      ReportSummary = BuildReportSummary(BundleRoot);
      VerifySummary = BuildReportSummary(BundleRoot);
      LoadCoverageOverlap();
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

      ExportStatus = "Exported: " + result.OutputRoot;
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
            var evalRunner = new STIGForge.Verify.EvaluateStigRunner();
            var evalResult = evalRunner.Run(EvaluateStigRoot, EvaluateStigArgs ?? string.Empty, EvaluateStigRoot);
            var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
            Directory.CreateDirectory(evalOutput);
            var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(evalOutput, "Evaluate-STIG");
            report.StartedAt = evalResult.StartedAt;
            report.FinishedAt = evalResult.FinishedAt;
            STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(evalOutput, "consolidated-results.json"), report);
            STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(evalOutput, "consolidated-results.csv"), report.Results);
            log.AppendLine("  OK: Evaluate-STIG complete. " + report.Results.Count + " results.");
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
            var scapRunner = new STIGForge.Verify.ScapRunner();
            var scapResult = scapRunner.Run(ScapCommandPath, ScapArgs ?? string.Empty, null);
            var scapOutput = Path.Combine(verifyRoot, "SCAP");
            Directory.CreateDirectory(scapOutput);
            var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? "SCAP" : ScapLabel;
            var report = STIGForge.Verify.VerifyReportWriter.BuildFromCkls(scapOutput, toolName);
            report.StartedAt = scapResult.StartedAt;
            report.FinishedAt = scapResult.FinishedAt;
            STIGForge.Verify.VerifyReportWriter.WriteJson(Path.Combine(scapOutput, "consolidated-results.json"), report);
            STIGForge.Verify.VerifyReportWriter.WriteCsv(Path.Combine(scapOutput, "consolidated-results.csv"), report.Results);
            log.AppendLine("  OK: SCAP complete. " + report.Results.Count + " results.");
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

  private static string BuildReportSummary(string bundleRoot)
  {
    var reportPath = Path.Combine(bundleRoot, "Verify", "consolidated-results.json");
    if (!File.Exists(reportPath)) return "";

    var report = STIGForge.Verify.VerifyReportReader.LoadFromJson(reportPath);
    var total = report.Results.Count;
    var open = report.Results.Count(r => r.Status != null && r.Status.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0);
    var closed = total - open;
    return "Summary: total=" + total + " closed=" + closed + " open=" + open;
  }
}
