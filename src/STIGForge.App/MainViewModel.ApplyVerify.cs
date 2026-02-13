using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Constants;
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

      var script = Path.Combine(BundleRoot, BundlePaths.ApplyDirectory, "RunApply.ps1");
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
        SkipSnapshot = ApplySkipSnapshot,
        PowerStigModulePath = string.IsNullOrWhiteSpace(PowerStigModulePath) ? null : PowerStigModulePath.Trim(),
        PowerStigDataFile = string.IsNullOrWhiteSpace(PowerStigDataFile) ? null : PowerStigDataFile.Trim(),
        PowerStigOutputPath = string.IsNullOrWhiteSpace(PowerStigOutputPath) ? null : PowerStigOutputPath.Trim(),
        PowerStigVerbose = PowerStigVerbose,
        AdmxSourcePath = string.IsNullOrWhiteSpace(AdmxSourcePath) ? null : AdmxSourcePath.Trim(),
        LgpoExePath = string.IsNullOrWhiteSpace(LgpoExePath) ? null : LgpoExePath.Trim(),
        LgpoGpoBackupPath = string.IsNullOrWhiteSpace(LgpoGpoBackupPath) ? null : LgpoGpoBackupPath.Trim(),
        LgpoVerbose = LgpoVerbose
      }, CancellationToken.None);

      ApplyStatus = "Apply complete: " + result.LogPath;
      LastOutputPath = result.LogPath;
      ReportSummary = BuildReportSummary(BundleRoot);
      GuidedNextAction = "Apply complete. Next action: run Verify and review mission summary.";
      RefreshDashboard();
    }
    catch (Exception ex)
    {
      ApplyStatus = "Apply failed: " + ex.Message;
      if (!string.IsNullOrWhiteSpace(BundleRoot))
      {
        var guidance = BuildApplyRecoveryGuidance(BundleRoot);
        ApplyStatus += " " + guidance;
        GuidedNextAction = "Apply blocked. " + guidance;
      }
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
        await TryActivateToolkitAsync(userInitiated: false, _cts.Token);

      var dscMofPath = ResolveDscMofPathForGui();
      var hasDscMofs = !string.IsNullOrWhiteSpace(dscMofPath);

      if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath) && !hasDscMofs)
      {
        VerifyStatus = "No verify tools available. Provide Evaluate-STIG root, SCAP command path, or compile PowerSTIG MOFs first.";
        StatusText = VerifyStatus;
        return;
      }

      var verifyRoot = Path.Combine(BundleRoot, BundlePaths.VerifyDirectory);
      Directory.CreateDirectory(verifyRoot);
      var coverageInputs = new List<VerificationCoverageInput>();
      var aggregatedResults = 0;
      var toolFailures = new List<string>();

      var verifyTasks = new List<Task<VerificationToolExecutionOutcome>>();

      if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
      {
        var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
        verifyTasks.Add(ExecuteVerificationToolAsync(
          "Evaluate-STIG",
          new VerificationWorkflowRequest
          {
            OutputRoot = evalOutput,
            ConsolidatedToolLabel = "Evaluate-STIG",
            EvaluateStig = new EvaluateStigWorkflowOptions
            {
              Enabled = true,
              ToolRoot = EvaluateStigRoot,
              Arguments = EvaluateStigArgs ?? string.Empty
            }
          },
          CancellationToken.None));
      }

      if (!string.IsNullOrWhiteSpace(ScapCommandPath))
      {
        var scapOutput = Path.Combine(verifyRoot, PackTypes.Scap);
        var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? PackTypes.Scap : ScapLabel;
        verifyTasks.Add(ExecuteVerificationToolAsync(
          toolName,
          new VerificationWorkflowRequest
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
          },
          CancellationToken.None));
      }

      if (verifyTasks.Count > 0)
      {
        VerifyStatus = verifyTasks.Count > 1
          ? "Verify running: scanning with Evaluate-STIG and SCAP in parallel..."
          : "Verify running: scanning configured verification tool...";
        StatusText = VerifyStatus;

        var verifyResults = await Task.WhenAll(verifyTasks);
        foreach (var result in verifyResults)
        {
          if (result.Error != null)
          {
            var failure = result.ToolLabel + " failed: " + result.Error.Message;
            toolFailures.Add(failure);
            System.Diagnostics.Debug.WriteLine("[Verify-" + result.ToolLabel + "] " + failure);
            continue;
          }

          var workflow = result.Workflow!;
          foreach (var diag in workflow.Diagnostics)
            System.Diagnostics.Debug.WriteLine("[Verify-" + result.ToolLabel + "] " + diag);

          coverageInputs.Add(new VerificationCoverageInput
          {
            ToolLabel = result.ToolLabel,
            ReportPath = workflow.ConsolidatedJsonPath
          });

          aggregatedResults += workflow.ConsolidatedResultCount;
        }
      }

      if (hasDscMofs)
      {
        VerifyStatus = "Verify running: scanning compiled DSC MOFs...";
        StatusText = VerifyStatus;
        var dscOutput = Path.Combine(verifyRoot, "DSC-Scan");
        var dscWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
        {
          OutputRoot = dscOutput,
          ConsolidatedToolLabel = "PowerSTIG-DSC",
          DscScan = new DscScanWorkflowOptions
          {
            Enabled = true,
            MofPath = dscMofPath!,
            Verbose = PowerStigVerbose,
            ToolLabel = "PowerSTIG-DSC"
          }
        }, CancellationToken.None);

        coverageInputs.Add(new VerificationCoverageInput
        {
          ToolLabel = "PowerSTIG-DSC",
          ReportPath = dscWorkflow.ConsolidatedJsonPath
        });

        aggregatedResults += dscWorkflow.ConsolidatedResultCount;
      }

      if (coverageInputs.Count > 0)
      {
        VerifyStatus = "Verify running: aggregating coverage artifacts...";
        StatusText = VerifyStatus;
        _artifactAggregation.WriteCoverageArtifacts(Path.Combine(BundleRoot, BundlePaths.ReportsDirectory), coverageInputs);
      }

      if (toolFailures.Count == 0)
      {
        VerifyStatus = $"Verify complete. Parsed {aggregatedResults} results.";
      }
      else if (aggregatedResults > 0)
      {
        VerifyStatus = $"Verify complete with warnings. Parsed {aggregatedResults} results. Failed: {string.Join("; ", toolFailures)}";
      }
      else
      {
        VerifyStatus = "Verify failed: " + string.Join("; ", toolFailures);
      }

      StatusText = VerifyStatus;
      LastOutputPath = Path.Combine(BundleRoot, BundlePaths.VerifyDirectory);
      ReportSummary = BuildReportSummary(BundleRoot);
      VerifySummary = ReportSummary;
      LoadCoverageOverlap();
      RefreshDashboard();
    }
    catch (Exception ex)
    {
      VerifyStatus = "Verify failed: " + ex.Message;
      StatusText = VerifyStatus;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task RunSimpleMissionAsync()
  {
    if (IsBusy) return;

    if (SimpleBuildBeforeRun)
    {
      if (SelectedPack == null)
      {
        StatusText = "Simple Mode: choose a content pack first.";
        GuidedNextAction = "Question 2: select a content pack.";
        return;
      }

      if (SelectedProfile == null)
      {
        StatusText = "Simple Mode: choose a profile first.";
        GuidedNextAction = "Question 3: select or save a profile.";
        return;
      }

      await BuildBundleAsync();

      if (!StatusText.StartsWith("Build complete", StringComparison.OrdinalIgnoreCase))
      {
        GuidedNextAction = "Build did not complete. Fix pack/profile inputs, then run Question 6 again.";
        return;
      }

      if (string.IsNullOrWhiteSpace(BundleRoot) || !Directory.Exists(BundleRoot))
      {
        StatusText = "Simple Mode: build completed but bundle path is unavailable.";
        GuidedNextAction = "Question 6: use Build Bundle first or select an existing bundle.";
        return;
      }
    }
    else if (string.IsNullOrWhiteSpace(BundleRoot) || !Directory.Exists(BundleRoot))
    {
      StatusText = "Simple Mode: select an existing bundle path first.";
      GuidedNextAction = "Question 6: choose a valid bundle path when skipping build.";
      return;
    }

    if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath))
      await TryActivateToolkitAsync(userInitiated: false, _cts.Token);

    var simpleDscMofs = ResolveDscMofPathForGui();
    if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath) && string.IsNullOrWhiteSpace(simpleDscMofs))
    {
      StatusText = "Simple Mode: no verify tools available. Configure Evaluate-STIG, SCAP, or compile PowerSTIG MOFs first.";
      GuidedNextAction = "Question 4: activate tools from STIG_SCAP or set scanner paths manually.";
      return;
    }

    OrchRunApply = true;
    OrchRunVerify = true;
    OrchRunExport = true;
    GuidedNextAction = "Running simple mission: apply, verify, and export.";
    await Orchestrate();
  }

  private async Task<VerificationToolExecutionOutcome> ExecuteVerificationToolAsync(string toolLabel, VerificationWorkflowRequest request, CancellationToken ct)
  {
    try
    {
      var workflow = await _verificationWorkflow.RunAsync(request, ct);
      return VerificationToolExecutionOutcome.Success(toolLabel, workflow);
    }
    catch (Exception ex)
    {
      return VerificationToolExecutionOutcome.Failure(toolLabel, ex);
    }
  }

  private sealed class VerificationToolExecutionOutcome
  {
    public string ToolLabel { get; private set; } = string.Empty;
    public VerificationWorkflowResult? Workflow { get; private set; }
    public Exception? Error { get; private set; }

    public static VerificationToolExecutionOutcome Success(string toolLabel, VerificationWorkflowResult workflow)
    {
      return new VerificationToolExecutionOutcome
      {
        ToolLabel = toolLabel,
        Workflow = workflow
      };
    }

    public static VerificationToolExecutionOutcome Failure(string toolLabel, Exception error)
    {
      return new VerificationToolExecutionOutcome
      {
        ToolLabel = toolLabel,
        Error = error
      };
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
      _notifications.Success("Export complete.");
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
      GuidedNextAction = "Orchestration in progress...";
      var log = new StringBuilder();
      log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Active bundle: " + BundleRoot);
      log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Verify uses the bundle's existing manifest/content. Rebuild bundle after changing selected packs.");
      if (SelectedMissionPacks.Count > 1)
      {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] NOTE: " + SelectedMissionPacks.Count + " packs are selected in Import, but orchestration runs only the active bundle path above.");
      }
      var manifestPath = Path.Combine(BundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
      if (File.Exists(manifestPath))
      {
        try
        {
          using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
          var runPack = doc.RootElement.TryGetProperty("Run", out var runEl)
            && runEl.TryGetProperty("PackName", out var runPackEl)
            ? (runPackEl.GetString() ?? string.Empty).Trim()
            : string.Empty;
          var manifestPack = doc.RootElement.TryGetProperty("Pack", out var packEl)
            && packEl.TryGetProperty("Name", out var nameEl)
            ? (nameEl.GetString() ?? string.Empty).Trim()
            : string.Empty;
          var effectivePack = string.IsNullOrWhiteSpace(runPack) ? manifestPack : runPack;
          if (!string.IsNullOrWhiteSpace(effectivePack))
            log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] Bundle manifest pack: " + effectivePack);
        }
        catch
        {
        }
      }
      log.AppendLine();

      // Step 1: Apply
      var blockingFailures = new List<string>();

      if (OrchRunApply)
      {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === APPLY ===");
        OrchStatus = "Running Apply...";
        OrchLog = log.ToString();

        var script = Path.Combine(BundleRoot, BundlePaths.ApplyDirectory, "RunApply.ps1");
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

            if (!string.IsNullOrWhiteSpace(PowerStigModulePath))
              log.AppendLine("  INFO: PowerSTIG compile enabled.");

            var result = await _applyRunner.RunAsync(new STIGForge.Apply.ApplyRequest
            {
              BundleRoot = BundleRoot,
              ScriptPath = script,
              ScriptArgs = "-BundleRoot \"" + BundleRoot + "\"",
              SkipSnapshot = ApplySkipSnapshot,
              ModeOverride = ParseMode(ProfileMode),
              PowerStigModulePath = string.IsNullOrWhiteSpace(PowerStigModulePath) ? null : PowerStigModulePath.Trim(),
              PowerStigDataFile = string.IsNullOrWhiteSpace(PowerStigDataFile) ? null : PowerStigDataFile.Trim(),
              PowerStigOutputPath = string.IsNullOrWhiteSpace(PowerStigOutputPath) ? null : PowerStigOutputPath.Trim(),
              PowerStigVerbose = PowerStigVerbose,
              AdmxSourcePath = string.IsNullOrWhiteSpace(AdmxSourcePath) ? null : AdmxSourcePath.Trim(),
              LgpoExePath = string.IsNullOrWhiteSpace(LgpoExePath) ? null : LgpoExePath.Trim(),
              LgpoGpoBackupPath = string.IsNullOrWhiteSpace(LgpoGpoBackupPath) ? null : LgpoGpoBackupPath.Trim(),
              LgpoVerbose = LgpoVerbose
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
      if (OrchRunVerify && string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath))
        await TryActivateToolkitAsync(userInitiated: false, _cts.Token);

      if (OrchRunVerify)
      {
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === VERIFY ===");
        if (blockingFailures.Count > 0)
          log.AppendLine("  WARN: Apply had failures, but proceeding with verification (scan-only is safe).");
        OrchStatus = "Running Verify...";
        OrchLog = log.ToString();
        {

        var verifyRoot = Path.Combine(BundleRoot, BundlePaths.VerifyDirectory);
        Directory.CreateDirectory(verifyRoot);

        var orchVerifyTasks = new List<Task<VerificationToolExecutionOutcome>>();

        if (!string.IsNullOrWhiteSpace(EvaluateStigRoot))
        {
          log.AppendLine("  Queue: Evaluate-STIG");
          var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
          orchVerifyTasks.Add(ExecuteVerificationToolAsync(
            "Evaluate-STIG",
            new VerificationWorkflowRequest
            {
              OutputRoot = evalOutput,
              ConsolidatedToolLabel = "Evaluate-STIG",
              EvaluateStig = new EvaluateStigWorkflowOptions
              {
                Enabled = true,
                ToolRoot = EvaluateStigRoot,
                Arguments = EvaluateStigArgs ?? string.Empty
              }
            },
            CancellationToken.None));
        }

        if (!string.IsNullOrWhiteSpace(ScapCommandPath))
        {
          var toolName = string.IsNullOrWhiteSpace(ScapLabel) ? PackTypes.Scap : ScapLabel;
          log.AppendLine("  Queue: " + toolName);
          var scapOutput = Path.Combine(verifyRoot, PackTypes.Scap);
          orchVerifyTasks.Add(ExecuteVerificationToolAsync(
            toolName,
            new VerificationWorkflowRequest
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
            },
            CancellationToken.None));
        }

        if (orchVerifyTasks.Count > 0)
        {
          log.AppendLine("  Running queued verification tools in parallel...");
          OrchLog = log.ToString();

          var orchResults = await Task.WhenAll(orchVerifyTasks);
          foreach (var result in orchResults)
          {
            if (result.Error != null)
            {
              log.AppendLine("  WARN: " + result.ToolLabel + " failed: " + result.Error.Message);
              continue;
            }

            var workflow = result.Workflow!;
            coverageInputs.Add(new VerificationCoverageInput
            {
              ToolLabel = result.ToolLabel,
              ReportPath = workflow.ConsolidatedJsonPath
            });
            log.AppendLine("  OK: " + result.ToolLabel + " complete. " + workflow.ConsolidatedResultCount + " results.");

            if (workflow.Diagnostics.Count > 0)
            {
              log.AppendLine("  Diagnostics:");
              foreach (var diag in workflow.Diagnostics)
                log.AppendLine("    " + diag);
            }
          }
        }

        var orchDscMofPath = ResolveDscMofPathForGui();
        if (!string.IsNullOrWhiteSpace(orchDscMofPath))
        {
          try
          {
            log.AppendLine("  Running DSC compliance scan...");
            OrchLog = log.ToString();
            var dscOutput = Path.Combine(verifyRoot, "DSC-Scan");
            var dscWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
            {
              OutputRoot = dscOutput,
              ConsolidatedToolLabel = "PowerSTIG-DSC",
              DscScan = new DscScanWorkflowOptions
              {
                Enabled = true,
                MofPath = orchDscMofPath!,
                Verbose = PowerStigVerbose,
                ToolLabel = "PowerSTIG-DSC"
              }
            }, CancellationToken.None);
            coverageInputs.Add(new VerificationCoverageInput
            {
              ToolLabel = "PowerSTIG-DSC",
              ReportPath = dscWorkflow.ConsolidatedJsonPath
            });
            log.AppendLine("  OK: DSC scan complete. " + dscWorkflow.ConsolidatedResultCount + " results.");
            
            if (dscWorkflow.Diagnostics.Count > 0)
            {
              log.AppendLine("  Diagnostics:");
              foreach (var diag in dscWorkflow.Diagnostics)
                log.AppendLine("    " + diag);
            }
          }
          catch (Exception ex)
          {
            log.AppendLine("  WARN: DSC scan failed: " + ex.Message);
          }
        }

        if (string.IsNullOrWhiteSpace(EvaluateStigRoot) && string.IsNullOrWhiteSpace(ScapCommandPath) && string.IsNullOrWhiteSpace(orchDscMofPath))
        {
          log.AppendLine("  SKIP: No verify tools configured and no DSC MOF files found.");
        }

        if (coverageInputs.Count > 0)
        {
          try
          {
            var artifacts = _artifactAggregation.WriteCoverageArtifacts(Path.Combine(BundleRoot, BundlePaths.ReportsDirectory), coverageInputs);
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
        log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] === EXPORT eMASS ===");
        if (blockingFailures.Count > 0)
          log.AppendLine("  WARN: Apply had failures. Export will proceed but may contain incomplete data.");
        OrchStatus = "Exporting eMASS...";
        OrchLog = log.ToString();
        {

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

      if (blockingFailures.Count > 0)
      {
        var guidance = BuildApplyRecoveryGuidance(BundleRoot);
        GuidedNextAction = "Blocking findings detected. " + guidance;
        ReportSummary = "Mission blocked: " + string.Join(" | ", blockingFailures) + Environment.NewLine + GuidedNextAction;
      }
      else
      {
        ReportSummary = BuildReportSummary(BundleRoot);
        GuidedNextAction = "No blocking mission findings. Next action: proceed with export and release evidence collection.";
      }

      // Refresh dashboard
      LoadCoverageOverlap();
      RefreshDashboard();
    }
    catch (Exception ex)
    {
      OrchStatus = "Orchestration failed: " + ex.Message;
      GuidedNextAction = OrchStatus;
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

  /// <summary>
  /// Auto-detect compiled DSC MOF files for compliance scanning.
  /// Checks PowerStigOutputPath, then default {BundleRoot}/Apply/Dsc.
  /// Returns directory path if MOFs exist, null otherwise.
  /// </summary>
  private string? ResolveDscMofPathForGui()
  {
    if (!string.IsNullOrWhiteSpace(PowerStigOutputPath))
    {
      var trimmed = PowerStigOutputPath.Trim();
      if (Directory.Exists(trimmed) && Directory.GetFiles(trimmed, "*.mof", SearchOption.AllDirectories).Length > 0)
        return trimmed;
    }

    if (!string.IsNullOrWhiteSpace(BundleRoot))
    {
      var defaultDscDir = Path.Combine(BundleRoot, BundlePaths.ApplyDirectory, "Dsc");
      if (Directory.Exists(defaultDscDir) && Directory.GetFiles(defaultDscDir, "*.mof", SearchOption.AllDirectories).Length > 0)
        return defaultDscDir;
    }

    return null;
  }

  private static string BuildApplyRecoveryGuidance(string bundleRoot)
  {
    var applyLog = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "apply_run.json");
    var snapshotsDir = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "Snapshots");
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
