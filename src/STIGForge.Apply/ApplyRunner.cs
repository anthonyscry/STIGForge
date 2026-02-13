using System.Diagnostics;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using STIGForge.Core.Models;
using STIGForge.Apply.Snapshot;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using Microsoft.Extensions.Logging;
 
 namespace STIGForge.Apply;
 
public sealed class ApplyRunner
{
    private const string AdmxImportStepName = "admx_import";
    private const string PowerStigStepName = "powerstig_compile";
    private const string ScriptStepName = "apply_script";
    private const string LgpoApplyStepName = "lgpo_apply";
    private const string DscStepName = "apply_dsc";

    private readonly ILogger<ApplyRunner> _logger;
    private readonly SnapshotService _snapshotService;
    private readonly RollbackScriptGenerator _rollbackScriptGenerator;
    private readonly LcmService _lcmService;
    private readonly RebootCoordinator _rebootCoordinator;
    private readonly IAuditTrailService? _audit;

    public ApplyRunner(
      ILogger<ApplyRunner> logger,
      SnapshotService snapshotService,
      RollbackScriptGenerator rollbackScriptGenerator,
      LcmService lcmService,
      RebootCoordinator rebootCoordinator,
      IAuditTrailService? audit = null)
    {
       _logger = logger ?? throw new ArgumentNullException(nameof(logger));
       _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
       _rollbackScriptGenerator = rollbackScriptGenerator ?? throw new ArgumentNullException(nameof(rollbackScriptGenerator));
       _lcmService = lcmService ?? throw new ArgumentNullException(nameof(lcmService));
       _rebootCoordinator = rebootCoordinator ?? throw new ArgumentNullException(nameof(rebootCoordinator));
       _audit = audit;
    }

   public async Task<ApplyResult> RunAsync(ApplyRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var root = request.BundleRoot.Trim();
    if (!Directory.Exists(root))
      throw new DirectoryNotFoundException("Bundle root not found: " + root);

    var applyRoot = Path.Combine(root, BundlePaths.ApplyDirectory);
    var logsDir = Path.Combine(applyRoot, "Logs");
    var snapshotsDir = Path.Combine(applyRoot, "Snapshots");
    Directory.CreateDirectory(applyRoot);
    Directory.CreateDirectory(logsDir);
    Directory.CreateDirectory(snapshotsDir);

    var mode = request.ModeOverride ?? TryReadModeFromManifest(root) ?? HardeningMode.Safe;

      var plannedSteps = BuildPlannedStepNames(request);

      // Check for resume after reboot (must be FIRST operation)
      RebootContext? resumeContext;
      try
      {
        resumeContext = await _rebootCoordinator.ResumeAfterReboot(root, ct).ConfigureAwait(false);
      }
      catch (RebootException ex)
      {
        throw new InvalidOperationException(
          "Resume context is invalid or exhausted. Automatic continuation is blocked until operator decision. "
          + "Review Apply/.resume_marker.json and recovery artifacts before retrying.",
          ex);
      }

      if (resumeContext != null)
      {
         ValidateResumeContext(resumeContext, plannedSteps, root);

         _logger.LogInformation("Resuming apply after reboot from step {CurrentStepIndex} ({CompletedCount} steps completed)",
            resumeContext.CurrentStepIndex, resumeContext.CompletedSteps?.Count ?? 0);
         // Mark previously completed steps so they are skipped below
         if (resumeContext.CompletedSteps != null)
         {
           foreach (var completedStep in resumeContext.CompletedSteps)
             _logger.LogInformation("  Skipping already-completed step: {Step}", completedStep);
         }
      }

      var completedSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (resumeContext?.CompletedSteps != null)
      {
        foreach (var completedStep in resumeContext.CompletedSteps)
          completedSteps.Add(completedStep);
      }

      var steps = new List<ApplyStepOutcome>();
      SnapshotResult? snapshot = null;

      // LCM workflow — skip entirely for AuditOnly (read-only scan should never touch LCM)
      LcmState? originalLcm = null;
      if (!string.IsNullOrWhiteSpace(request.DscMofPath) && mode != HardeningMode.AuditOnly)
      {
        try
        {
          // Capture original LCM state
          _logger.LogInformation("Capturing original LCM state...");
          originalLcm = await _lcmService.GetLcmState(ct).ConfigureAwait(false);

          // Configure LCM for apply
          var lcmConfig = new LcmConfig
          {
            ConfigurationMode = "ApplyAndMonitor",
            RebootNodeIfNeeded = true,
            ConfigurationModeFrequencyMins = 15,
            AllowModuleOverwrite = true
          };
          _logger.LogInformation("Configuring LCM for DSC application (Mode: {ConfigurationMode})...", lcmConfig.ConfigurationMode);
          await _lcmService.ConfigureLcm(lcmConfig, ct).ConfigureAwait(false);
        }
        catch (LcmException ex)
        {
          _logger.LogError(ex, "LCM configuration failed. Apply aborted.");
          throw new InvalidOperationException("LCM configuration failed, apply aborted.", ex);
        }
      }

      if (!request.SkipSnapshot)
      {
        try
        {
          _logger.LogInformation("Creating pre-apply snapshot...");
          snapshot = await _snapshotService.CreateSnapshot(snapshotsDir, ct).ConfigureAwait(false);
          snapshot.RollbackScriptPath = _rollbackScriptGenerator.GenerateScript(snapshot);
          _logger.LogInformation("Snapshot {SnapshotId} created. Rollback script: {RollbackScript}",
            snapshot.SnapshotId, snapshot.RollbackScriptPath);
        }
        catch (SnapshotException ex)
        {
          _logger.LogError(ex, "Snapshot creation failed. Apply aborted.");
          throw new InvalidOperationException("Snapshot failed, apply aborted.", ex);
        }
      }

    if (!string.IsNullOrWhiteSpace(request.AdmxSourcePath))
    {
      if (completedSteps.Contains(AdmxImportStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", AdmxImportStepName);
      }
      else
      {
        var outcome = await RunAdmxImportAsync(
          request.AdmxSourcePath!,
          logsDir,
          ct).ConfigureAwait(false);
        steps.Add(outcome);
      }

      if (mode != HardeningMode.AuditOnly && await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      {
        _logger.LogInformation("Reboot required after ADMX import");
        var context = new RebootContext
        {
          BundleRoot = root,
          CurrentStepIndex = steps.Count,
          CompletedSteps = steps.Select(s => s.StepName).ToList(),
          RebootScheduledAt = DateTimeOffset.UtcNow
        };
        await _rebootCoordinator.ScheduleReboot(context, ct).ConfigureAwait(false);
        return new ApplyResult
        {
          BundleRoot = root,
          Mode = mode,
          LogPath = string.Empty,
          Steps = steps,
          SnapshotId = snapshot?.SnapshotId ?? string.Empty,
          RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty,
          IsMissionComplete = false,
          IntegrityVerified = false,
          RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty)
        };
      }
    }

    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath))
    {
      if (completedSteps.Contains(PowerStigStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", PowerStigStepName);
      }
      else
      {
        var outcome = await RunPowerStigCompileAsync(
           request.PowerStigModulePath!,
           request.PowerStigDataFile,
           string.IsNullOrWhiteSpace(request.PowerStigOutputPath)
             ? Path.Combine(applyRoot, "Dsc")
             : request.PowerStigOutputPath!,
           root,
           logsDir,
           snapshotsDir,
           mode,
           request.PowerStigVerbose,
           ct).ConfigureAwait(false);
        steps.Add(outcome);
      }

      if (mode != HardeningMode.AuditOnly && await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      {
         _logger.LogInformation("Reboot required after PowerSTIG compile");
         var context = new RebootContext
         {
            BundleRoot = root,
            CurrentStepIndex = steps.Count,
            CompletedSteps = steps.Select(s => s.StepName).ToList(),
            RebootScheduledAt = DateTimeOffset.UtcNow
         };
          await _rebootCoordinator.ScheduleReboot(context, ct).ConfigureAwait(false);
          return new ApplyResult
          {
             BundleRoot = root,
             Mode = mode,
             LogPath = string.Empty,
             Steps = steps,
             SnapshotId = snapshot?.SnapshotId ?? string.Empty,
             RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty,
             IsMissionComplete = false,
             IntegrityVerified = false,
             RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty)
          };
       }
    }

    if (!string.IsNullOrWhiteSpace(request.ScriptPath))
    {
      if (completedSteps.Contains(ScriptStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", ScriptStepName);
      }
      else
      {
        var outcome = await RunScriptAsync(request.ScriptPath!, request.ScriptArgs, root, logsDir, snapshotsDir, mode, ct).ConfigureAwait(false);
        steps.Add(outcome);
      }

      if (mode != HardeningMode.AuditOnly && await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      {
         _logger.LogInformation("Reboot required after script execution");
         var context = new RebootContext
         {
            BundleRoot = root,
            CurrentStepIndex = steps.Count,
            CompletedSteps = steps.Select(s => s.StepName).ToList(),
            RebootScheduledAt = DateTimeOffset.UtcNow
         };
          await _rebootCoordinator.ScheduleReboot(context, ct).ConfigureAwait(false);
          return new ApplyResult
          {
             BundleRoot = root,
             Mode = mode,
             LogPath = string.Empty,
             Steps = steps,
             SnapshotId = snapshot?.SnapshotId ?? string.Empty,
             RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty,
             IsMissionComplete = false,
             IntegrityVerified = false,
             RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty)
          };
        }
     }

    if (!string.IsNullOrWhiteSpace(request.LgpoGpoBackupPath))
    {
      if (completedSteps.Contains(LgpoApplyStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", LgpoApplyStepName);
      }
      else
      {
        var outcome = await RunLgpoApplyAsync(
          request.LgpoExePath,
          request.LgpoGpoBackupPath!,
          logsDir,
          request.LgpoVerbose,
          ct).ConfigureAwait(false);
        steps.Add(outcome);
      }

      if (mode != HardeningMode.AuditOnly && await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      {
        _logger.LogInformation("Reboot required after LGPO apply");
        var context = new RebootContext
        {
          BundleRoot = root,
          CurrentStepIndex = steps.Count,
          CompletedSteps = steps.Select(s => s.StepName).ToList(),
          RebootScheduledAt = DateTimeOffset.UtcNow
        };
        await _rebootCoordinator.ScheduleReboot(context, ct).ConfigureAwait(false);
        return new ApplyResult
        {
          BundleRoot = root,
          Mode = mode,
          LogPath = string.Empty,
          Steps = steps,
          SnapshotId = snapshot?.SnapshotId ?? string.Empty,
          RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty,
          IsMissionComplete = false,
          IntegrityVerified = false,
          RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty)
        };
      }
    }

   if (!string.IsNullOrWhiteSpace(request.DscMofPath) && mode != HardeningMode.AuditOnly)
      {
        if (completedSteps.Contains(DscStepName))
        {
          _logger.LogInformation("Skipping previously completed step: {StepName}", DscStepName);
        }
        else
        {
          var outcome = await RunDscAsync(request.DscMofPath!, root, logsDir, snapshotsDir, mode, request.DscVerbose, ct).ConfigureAwait(false);
          steps.Add(outcome);
        }

       if (originalLcm != null && request.ResetLcmAfterApply)
       {
         try
         {
          _logger.LogInformation("Resetting LCM to original state...");
            await _lcmService.ResetLcm(originalLcm, ct).ConfigureAwait(false);
          _logger.LogInformation("LCM reset successfully.");
         }
         catch (LcmException ex)
         {
          _logger.LogWarning(ex, "Failed to reset LCM to original state. This is non-critical and apply continues.");
         }
       }
     }
     else if (!string.IsNullOrWhiteSpace(request.DscMofPath) && mode == HardeningMode.AuditOnly)
     {
       _logger.LogInformation(
         "AuditOnly mode: skipping LCM configuration, DSC apply (Start-DscConfiguration), and reboot scheduling. " +
         "Use DscScanRunner with Test-DscConfiguration for read-only compliance checks against the compiled MOFs.");
     }

     var logPath = Path.Combine(applyRoot, "apply_run.json");
     var summary = new
     {
       bundleRoot = root,
       mode = mode.ToString(),
       startedAt = steps.Count > 0 ? steps.Min(s => s.StartedAt) : DateTimeOffset.Now,
       finishedAt = steps.Count > 0 ? steps.Max(s => s.FinishedAt) : DateTimeOffset.Now,
       steps = steps
     };

     var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
     File.WriteAllText(logPath, json);

      var blockingFailures = new List<string>();
      var integrityVerified = false;
      var recoveryArtifacts = GetRecoveryArtifactPaths(snapshot, snapshotsDir, logPath);

      var stepFailures = steps
        .Where(s => s.ExitCode != 0)
        .Select(s => $"Step '{s.StepName}' exited with code {s.ExitCode}.")
        .ToList();
      blockingFailures.AddRange(stepFailures);

      if (_audit == null)
      {
        blockingFailures.Add("Audit trail service unavailable - integrity evidence cannot be verified.");
      }
      else
      {
        try
        {
         await _audit.RecordAsync(new AuditEntry
         {
           Action = "apply",
           Target = root,
            Result = stepFailures.Count == 0 ? "success" : "failure",
            Detail = $"Mode={mode}, Steps={steps.Count}",
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Timestamp = DateTimeOffset.Now
          }, ct).ConfigureAwait(false);

          integrityVerified = await _audit.VerifyIntegrityAsync(ct).ConfigureAwait(false);
          if (!integrityVerified)
            blockingFailures.Add("Audit trail integrity verification failed.");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to record or verify audit evidence for apply operation");
          blockingFailures.Add("Unable to verify audit integrity evidence: " + ex.Message);
        }
      }

      if (blockingFailures.Count > 0)
      {
        throw new InvalidOperationException(BuildBlockingFailureMessage(blockingFailures, recoveryArtifacts));
      }

      return new ApplyResult
      {
       BundleRoot = root,
       Mode = mode,
        LogPath = logPath,
        Steps = steps,
        SnapshotId = snapshot?.SnapshotId ?? string.Empty,
        RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty,
        IsMissionComplete = true,
        IntegrityVerified = integrityVerified,
        BlockingFailures = blockingFailures,
        RecoveryArtifactPaths = recoveryArtifacts
      };
   }

  private static HardeningMode? TryReadModeFromManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
    if (!File.Exists(manifestPath)) return null;

    using var stream = File.OpenRead(manifestPath);
    using var doc = JsonDocument.Parse(stream);
    if (!doc.RootElement.TryGetProperty("Profile", out var profile)) return null;

    if (!profile.TryGetProperty("HardeningMode", out var mode)) return null;

    if (mode.ValueKind == JsonValueKind.String)
    {
      var value = mode.GetString();
      if (string.IsNullOrWhiteSpace(value)) return null;

      if (Enum.TryParse<HardeningMode>(value, true, out var parsedFromString))
        return parsedFromString;

      return null;
    }

    if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out var numeric))
    {
      if (Enum.IsDefined(typeof(HardeningMode), numeric))
        return (HardeningMode)numeric;
    }

    return null;
  }

  private static async Task<ApplyStepOutcome> RunScriptAsync(
    string scriptPath,
    string? args,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    CancellationToken ct)
  {
    if (!File.Exists(scriptPath))
      throw new FileNotFoundException("Apply script not found", scriptPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_script_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_script_" + stepId + ".err.log");

    var arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
    if (!string.IsNullOrWhiteSpace(args))
      arguments += " " + args;
    if (mode == HardeningMode.AuditOnly)
      arguments += " -SkipLcm";

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = arguments,
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start apply script.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    File.WriteAllText(stdout, await outputTask.ConfigureAwait(false));
    File.WriteAllText(stderr, await errorTask.ConfigureAwait(false));

    return new ApplyStepOutcome
    {
      StepName = ScriptStepName,
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private Task<ApplyStepOutcome> RunAdmxImportAsync(
    string admxSourcePath,
    string logsDir,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (!Directory.Exists(admxSourcePath))
      throw new DirectoryNotFoundException("ADMX source path not found: " + admxSourcePath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var started = DateTimeOffset.Now;
    var stdout = Path.Combine(logsDir, "admx_import_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "admx_import_" + stepId + ".err.log");
    var output = new System.Text.StringBuilder();
    var errors = new System.Text.StringBuilder();
    var hadCopyFailure = false;

    var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    var policyDefinitionsDir = Path.Combine(windowsDir, "PolicyDefinitions");
    var policyDefinitionsEnUsDir = Path.Combine(policyDefinitionsDir, "en-US");
    Directory.CreateDirectory(policyDefinitionsDir);
    Directory.CreateDirectory(policyDefinitionsEnUsDir);

    var admxFiles = Directory.GetFiles(admxSourcePath, "*.admx", SearchOption.TopDirectoryOnly);
    var admlSourceDir = Path.Combine(admxSourcePath, "en-US");

    foreach (var admxFile in admxFiles)
    {
      ct.ThrowIfCancellationRequested();
      var admxFileName = Path.GetFileName(admxFile);
      var admxDestinationPath = Path.Combine(policyDefinitionsDir, admxFileName);
      try
      {
        File.Copy(admxFile, admxDestinationPath, overwrite: true);
        var copyMessage = "Copied ADMX: " + admxFile + " -> " + admxDestinationPath;
        _logger.LogInformation(copyMessage);
        output.AppendLine(copyMessage);
      }
      catch (Exception ex)
      {
        hadCopyFailure = true;
        var copyError = "Failed ADMX copy: " + admxFile + " -> " + admxDestinationPath + " | " + ex.Message;
        _logger.LogError(ex, "ADMX copy failed from {Source} to {Destination}", admxFile, admxDestinationPath);
        errors.AppendLine(copyError);
      }

      var admlSourcePath = Path.Combine(admlSourceDir, Path.ChangeExtension(admxFileName, ".adml"));
      if (!File.Exists(admlSourcePath))
      {
        continue;
      }

      var admlDestinationPath = Path.Combine(policyDefinitionsEnUsDir, Path.GetFileName(admlSourcePath));
      try
      {
        File.Copy(admlSourcePath, admlDestinationPath, overwrite: true);
        var copyMessage = "Copied ADML: " + admlSourcePath + " -> " + admlDestinationPath;
        _logger.LogInformation(copyMessage);
        output.AppendLine(copyMessage);
      }
      catch (Exception ex)
      {
        hadCopyFailure = true;
        var copyError = "Failed ADML copy: " + admlSourcePath + " -> " + admlDestinationPath + " | " + ex.Message;
        _logger.LogError(ex, "ADML copy failed from {Source} to {Destination}", admlSourcePath, admlDestinationPath);
        errors.AppendLine(copyError);
      }
    }

    File.WriteAllText(stdout, output.ToString());
    File.WriteAllText(stderr, errors.ToString());

    return Task.FromResult(new ApplyStepOutcome
    {
      StepName = AdmxImportStepName,
      ExitCode = hadCopyFailure ? 1 : 0,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    });
  }

  private static async Task<ApplyStepOutcome> RunLgpoApplyAsync(
    string? lgpoExePath,
    string gpoBackupPath,
    string logsDir,
    bool verbose,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();

    if (!Directory.Exists(gpoBackupPath))
      throw new DirectoryNotFoundException("LGPO GPO backup path not found: " + gpoBackupPath);

    var lgpoPath = ResolveLgpoExePath(lgpoExePath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "lgpo_apply_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "lgpo_apply_" + stepId + ".err.log");

    var arguments = "/g \"" + gpoBackupPath + "\"";
    if (verbose)
      arguments += " /v";

    var psi = new ProcessStartInfo
    {
      FileName = lgpoPath,
      Arguments = arguments,
      WorkingDirectory = gpoBackupPath,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start LGPO apply.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    File.WriteAllText(stdout, await outputTask.ConfigureAwait(false));
    File.WriteAllText(stderr, await errorTask.ConfigureAwait(false));

    return new ApplyStepOutcome
    {
      StepName = LgpoApplyStepName,
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private static async Task<ApplyStepOutcome> RunDscAsync(
    string mofPath,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    bool verbose,
    CancellationToken ct)
  {
    if (!Directory.Exists(mofPath))
      throw new DirectoryNotFoundException("DSC path not found: " + mofPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_dsc_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_dsc_" + stepId + ".err.log");

    var whatIf = mode == HardeningMode.AuditOnly ? " -WhatIf" : string.Empty;
    var v = verbose ? " -Verbose" : string.Empty;

    var command = "Start-DscConfiguration -Path \"" + mofPath + "\" -Wait -Force" + whatIf + v;
    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"",
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start DSC apply.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    File.WriteAllText(stdout, await outputTask.ConfigureAwait(false));
    File.WriteAllText(stderr, await errorTask.ConfigureAwait(false));

    return new ApplyStepOutcome
    {
      StepName = DscStepName,
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private static async Task<ApplyStepOutcome> RunPowerStigCompileAsync(
    string modulePath,
    string? dataFile,
    string outputPath,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    bool verbose,
    CancellationToken ct)
  {
    if (!Directory.Exists(modulePath) && !File.Exists(modulePath))
      throw new FileNotFoundException("PowerSTIG module path not found: " + modulePath, modulePath);

    Directory.CreateDirectory(outputPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".err.log");

    var v = verbose ? " -Verbose" : string.Empty;
    var dataArg = string.IsNullOrWhiteSpace(dataFile) ? string.Empty : " -StigDataFile \"" + dataFile + "\"";

    // Resolve the PowerSTIG module directory and its parent so PowerShell can
    // auto-resolve RequiredModules (AuditPolicyDsc, SecurityPolicyDsc, etc.).
    // Also include the bundle's Apply/Modules directory for user-staged deps.
    var moduleDir = Directory.Exists(modulePath) ? modulePath : Path.GetDirectoryName(modulePath) ?? modulePath;
    var moduleParent = Path.GetDirectoryName(moduleDir) ?? moduleDir;
    var bundleModules = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "Modules");
    Directory.CreateDirectory(bundleModules);

    var psMod = "$env:PSModulePath = '" + moduleParent.Replace("'", "''") + "' + ';' + '"
      + bundleModules.Replace("'", "''") + "' + ';' + $env:PSModulePath; ";

    // If modulePath is a .psd1 file, use it directly; otherwise treat it as a directory.
    var psdFilePath = modulePath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase)
      ? modulePath
      : Path.Combine(modulePath, "PowerStig.psd1");

    var installDeps =
      "try { " +
        "$psd = Import-PowerShellDataFile '" + psdFilePath.Replace("'", "''") + "'; " +
        "foreach ($dep in $psd.RequiredModules) { " +
          "$modName = if ($dep -is [string]) { $dep } else { $dep.ModuleName }; " +
          "$modDir = Join-Path '" + bundleModules.Replace("'", "''") + "' $modName; " +
          "if (-not (Test-Path $modDir)) { " +
            "Write-Host \"[STIGFORGE] Installing dependency: $modName\"; " +
            "Save-Module -Name $modName -Path '" + bundleModules.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue; " +
          "} " +
        "} " +
      "} catch { Write-Host \"[STIGFORGE] Dependency install note: $_\"; }; ";

    var command =
      psMod +
      installDeps +
      "Import-Module \"" + modulePath + "\"; " +
      "$ErrorActionPreference='Stop'; " +
      "New-StigDscConfiguration" + dataArg + " -OutputPath \"" + outputPath + "\"" + v + ";";

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"",
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();
    psi.Environment["PSModulePath"] = moduleParent + ";" + bundleModules + ";"
      + (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty);

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start PowerSTIG compile.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    File.WriteAllText(stdout, await outputTask.ConfigureAwait(false));
    File.WriteAllText(stderr, await errorTask.ConfigureAwait(false));

    return new ApplyStepOutcome
    {
      StepName = PowerStigStepName,
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private static IReadOnlyList<string> BuildPlannedStepNames(ApplyRequest request)
  {
    var planned = new List<string>();
    if (!string.IsNullOrWhiteSpace(request.AdmxSourcePath)) planned.Add(AdmxImportStepName);
    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath)) planned.Add(PowerStigStepName);
    if (!string.IsNullOrWhiteSpace(request.ScriptPath)) planned.Add(ScriptStepName);
    if (!string.IsNullOrWhiteSpace(request.LgpoGpoBackupPath)) planned.Add(LgpoApplyStepName);
    if (!string.IsNullOrWhiteSpace(request.DscMofPath)) planned.Add(DscStepName);
    return planned;
  }

  private static string ResolveLgpoExePath(string? requestedLgpoExePath)
  {
    var providedPath = requestedLgpoExePath;
    if (providedPath != null)
    {
      var trimmed = providedPath.Trim();
      if (trimmed.Length > 0)
      {
        if (!File.Exists(trimmed))
          throw new FileNotFoundException("LGPO executable not found: " + trimmed, trimmed);
        return trimmed;
      }
    }

    var resolved = TryFindExecutableInPath("LGPO.exe");
    if (resolved != null && resolved.Length > 0)
      return resolved;

    throw new FileNotFoundException("LGPO.exe not found in PATH.");
  }

  private static string? TryFindExecutableInPath(string fileName)
  {
    var pathVar = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(pathVar))
      return null;

    var normalizedPathVar = pathVar ?? string.Empty;

    foreach (var pathEntry in normalizedPathVar.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
    {
      var candidate = Path.Combine(pathEntry, fileName);
      if (File.Exists(candidate))
        return candidate;
    }

    return null;
  }

  private static void ValidateResumeContext(RebootContext context, IReadOnlyList<string> plannedSteps, string bundleRoot)
  {
    if (!string.Equals(context.BundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Resume context bundle path does not match current bundle. Operator decision required before continuation.");

    if (plannedSteps.Count == 0)
      throw new InvalidOperationException("Resume marker exists but no apply steps are configured. Operator decision required before continuation.");

    if (context.CurrentStepIndex >= plannedSteps.Count)
      throw new InvalidOperationException("Resume marker is exhausted (no remaining steps). Operator decision required before continuation.");

    foreach (var completedStep in context.CompletedSteps ?? new List<string>())
    {
      if (!plannedSteps.Contains(completedStep, StringComparer.OrdinalIgnoreCase))
      {
        throw new InvalidOperationException("Resume marker references unknown completed steps. Operator decision required before continuation.");
      }
    }
  }

  private static List<string> GetRecoveryArtifactPaths(SnapshotResult? snapshot, string snapshotsDir, string logPath)
  {
    var artifacts = new List<string>();
    var rollbackScriptPath = snapshot?.RollbackScriptPath;
    if (rollbackScriptPath is { Length: > 0 }) artifacts.Add(rollbackScriptPath);
    if (Directory.Exists(snapshotsDir)) artifacts.Add(snapshotsDir);
    if (!string.IsNullOrWhiteSpace(logPath)) artifacts.Add(logPath);
    return artifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  }

  private static string BuildBlockingFailureMessage(IReadOnlyList<string> blockingFailures, IReadOnlyList<string> recoveryArtifacts)
  {
    var sb = new System.Text.StringBuilder();
    sb.Append("Mission completion blocked due to integrity-critical failures: ");
    sb.Append(string.Join(" ", blockingFailures));
    if (recoveryArtifacts.Count > 0)
    {
      sb.Append(" Recovery artifacts: ");
      sb.Append(string.Join(", ", recoveryArtifacts));
      sb.Append(". Rollback remains operator-initiated.");
    }

    return sb.ToString();
  }
 
}
