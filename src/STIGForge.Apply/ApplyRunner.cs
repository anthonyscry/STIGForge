using System.Diagnostics;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Apply.Snapshot;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Evidence;
using STIGForge.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

 namespace STIGForge.Apply;

public sealed class ApplyRunner
{
    private const string PowerStigStepName = "powerstig_compile";
    private const string ScriptStepName = "apply_script";
    private const string DscStepName = "apply_dsc";
    private const string LgpoStepName = "apply_lgpo";

    private readonly ILogger<ApplyRunner> _logger;
    private readonly SnapshotService _snapshotService;
    private readonly RollbackScriptGenerator _rollbackScriptGenerator;
    private readonly LcmService _lcmService;
    private readonly RebootCoordinator _rebootCoordinator;
    private readonly IAuditTrailService? _audit;
    private readonly EvidenceCollector? _evidenceCollector;
    private readonly Lgpo.LgpoRunner? _lgpoRunner;
    private readonly PreflightRunner? _preflightRunner;

    public ApplyRunner(
      ILogger<ApplyRunner> logger,
      SnapshotService snapshotService,
      RollbackScriptGenerator rollbackScriptGenerator,
      LcmService lcmService,
      RebootCoordinator rebootCoordinator,
      IAuditTrailService? audit = null,
      EvidenceCollector? evidenceCollector = null,
      Lgpo.LgpoRunner? lgpoRunner = null,
      PreflightRunner? preflightRunner = null)
    {
       _logger = logger ?? throw new ArgumentNullException(nameof(logger));
       _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
       _rollbackScriptGenerator = rollbackScriptGenerator ?? throw new ArgumentNullException(nameof(rollbackScriptGenerator));
       _lcmService = lcmService ?? throw new ArgumentNullException(nameof(lcmService));
       _rebootCoordinator = rebootCoordinator ?? throw new ArgumentNullException(nameof(rebootCoordinator));
       _audit = audit;
       _evidenceCollector = evidenceCollector;
       _lgpoRunner = lgpoRunner;
       _preflightRunner = preflightRunner;
    }

   public async Task<ApplyResult> RunAsync(ApplyRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var root = request.BundleRoot.Trim();
    if (!Directory.Exists(root))
      throw new DirectoryNotFoundException("Bundle root not found: " + root);

    var applyRoot = Path.Combine(root, "Apply");
    var logsDir = Path.Combine(applyRoot, "Logs");
    var snapshotsDir = Path.Combine(applyRoot, "Snapshots");
    Directory.CreateDirectory(applyRoot);
    Directory.CreateDirectory(logsDir);
    Directory.CreateDirectory(snapshotsDir);

    var mode = request.ModeOverride ?? TryReadModeFromManifest(root) ?? HardeningMode.Safe;

    // Resolve effective run ID - generate one if not provided by orchestrator
    var runId = string.IsNullOrWhiteSpace(request.RunId) ? Guid.NewGuid().ToString() : request.RunId!;
    var priorRunId = request.PriorRunId;

    // Load prior run step evidence for continuity deduplication
    var priorStepSha256 = LoadPriorRunStepSha256(root, priorRunId);

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

      // LCM workflow
      LcmState? originalLcm = null;
      if (!string.IsNullOrWhiteSpace(request.DscMofPath))
      {
        try
        {
          // Capture original LCM state
          _logger.LogInformation("Capturing original LCM state...");
          originalLcm = await _lcmService.GetLcmState(ct).ConfigureAwait(false);

          // Configure LCM for apply
          var lcmConfig = new LcmConfig
          {
            ConfigurationMode = mode == HardeningMode.AuditOnly ? "ApplyOnly" : "ApplyAndMonitor",
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

        outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }

      // Check for reboot after PowerSTIG compile
      if (await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
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
             RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty),
             RunId = runId,
             PriorRunId = priorRunId
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
        outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }

      // Check for reboot after script execution
      if (await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
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
             RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty),
             RunId = runId,
             PriorRunId = priorRunId
          };
       }
    }

   if (!string.IsNullOrWhiteSpace(request.DscMofPath))
      {
        if (completedSteps.Contains(DscStepName))
        {
          _logger.LogInformation("Skipping previously completed step: {StepName}", DscStepName);
        }
        else
        {
          var outcome = await RunDscAsync(request.DscMofPath!, root, logsDir, snapshotsDir, mode, request.DscVerbose, ct).ConfigureAwait(false);
          outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
          steps.Add(outcome);
        }

       // Reset LCM after DSC application (optional)
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

    // LGPO step (secondary backend after DSC)
    if (!string.IsNullOrWhiteSpace(request.LgpoPolFilePath) && _lgpoRunner != null)
    {
      if (completedSteps.Contains(LgpoStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", LgpoStepName);
      }
      else
      {
        var outcome = await RunLgpoAsync(request, root, logsDir, ct).ConfigureAwait(false);
        outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }

      // Check for reboot after LGPO execution
      if (await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      {
        _logger.LogInformation("Reboot required after LGPO apply");
        var rebootCount = resumeContext?.RebootCount ?? 0;
        var context = new RebootContext
        {
          BundleRoot = root,
          CurrentStepIndex = steps.Count,
          CompletedSteps = steps.Select(s => s.StepName).ToList(),
          RebootScheduledAt = DateTimeOffset.UtcNow,
          RebootCount = rebootCount
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
          RecoveryArtifactPaths = GetRecoveryArtifactPaths(snapshot, snapshotsDir, string.Empty),
          RunId = runId,
          PriorRunId = priorRunId,
          RebootCount = rebootCount + 1,
          ConvergenceStatus = ConvergenceStatus.Diverged
        };
      }
    }

    // Calculate convergence status
    var finalRebootCount = resumeContext?.RebootCount ?? 0;
    var pendingReboot = await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false);
    var hasStepFailures = steps.Any(s => s.ExitCode != 0);
    var convergenceStatus = ConvergenceStatus.NotApplicable;

    if (steps.Count > 0)
    {
      if (!pendingReboot && !hasStepFailures)
        convergenceStatus = ConvergenceStatus.Converged;
      else if (finalRebootCount >= RebootCoordinator.MaxReboots)
        convergenceStatus = ConvergenceStatus.Exceeded;
      else
        convergenceStatus = ConvergenceStatus.Diverged;
    }

     var logPath = Path.Combine(applyRoot, "apply_run.json");
     var summary = new
     {
       runId,
       priorRunId,
       bundleRoot = root,
       mode = mode.ToString(),
       startedAt = steps.Count > 0 ? steps.Min(s => s.StartedAt) : DateTimeOffset.Now,
       finishedAt = steps.Count > 0 ? steps.Max(s => s.FinishedAt) : DateTimeOffset.Now,
       steps = steps.Select(s => new
       {
         s.StepName,
         s.ExitCode,
         s.StartedAt,
         s.FinishedAt,
         s.StdOutPath,
         s.StdErrPath,
         s.EvidenceMetadataPath,
         s.ArtifactSha256,
         s.ContinuityMarker
       })
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
            Detail = $"Mode={mode}, Steps={steps.Count}, RunId={runId}",
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
        RecoveryArtifactPaths = recoveryArtifacts,
        RunId = runId,
        PriorRunId = priorRunId,
        RebootCount = finalRebootCount,
        ConvergenceStatus = convergenceStatus
      };
   }

  /// <summary>
  /// Writes per-step evidence metadata with run-scoped provenance and SHA-256.
  /// Assigns a continuity marker by comparing the step's artifact SHA-256 against the prior run.
  /// Returns the updated outcome (immutable update via record copy pattern on mutable object).
  /// </summary>
  private ApplyStepOutcome WriteStepEvidence(ApplyStepOutcome outcome, string bundleRoot, string runId, IReadOnlyDictionary<string, string> priorStepSha256)
  {
    if (_evidenceCollector == null)
      return outcome;

    try
    {
      // Determine which artifact path to use for the SHA-256 (prefer stdout log)
      var artifactPath = !string.IsNullOrWhiteSpace(outcome.StdOutPath) && File.Exists(outcome.StdOutPath)
        ? outcome.StdOutPath
        : null;

      string? sha256 = null;
      if (artifactPath != null)
        sha256 = ComputeSha256(artifactPath);

      // Determine continuity marker based on prior run comparison
      string? continuityMarker = null;
      string? supersedesEvidenceId = null;
      if (sha256 != null && priorStepSha256.TryGetValue(outcome.StepName, out var priorSha))
      {
        continuityMarker = string.Equals(sha256, priorSha, StringComparison.OrdinalIgnoreCase)
          ? "retained"
          : "superseded";
      }

      var result = _evidenceCollector.WriteEvidence(new EvidenceWriteRequest
      {
        BundleRoot = bundleRoot,
        Title = "Apply step: " + outcome.StepName,
        Type = EvidenceArtifactType.File,
        Source = "ApplyRunner",
        SourceFilePath = artifactPath,
        ContentText = artifactPath == null ? $"Step {outcome.StepName} completed with exit code {outcome.ExitCode}" : null,
        FileExtension = artifactPath == null ? ".txt" : null,
        RunId = runId,
        StepName = outcome.StepName,
        SupersedesEvidenceId = supersedesEvidenceId
      });

      outcome.EvidenceMetadataPath = result.MetadataPath;
      outcome.ArtifactSha256 = sha256 ?? result.Sha256;
      outcome.ContinuityMarker = continuityMarker;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to write step evidence for {StepName} (non-blocking)", outcome.StepName);
    }

    return outcome;
  }

  /// <summary>
  /// Loads the SHA-256 hashes for each apply step from a prior run's apply_run.json.
  /// Returns empty dictionary if no prior run or file not found.
  /// </summary>
  private static IReadOnlyDictionary<string, string> LoadPriorRunStepSha256(string bundleRoot, string? priorRunId)
  {
    if (string.IsNullOrWhiteSpace(priorRunId))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Prior run's apply_run.json is at the well-known path under the same bundle root
    var logPath = Path.Combine(bundleRoot, "Apply", "apply_run.json");
    if (!File.Exists(logPath))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    try
    {
      using var stream = File.OpenRead(logPath);
      using var doc = JsonDocument.Parse(stream);

      // Match only if the prior run ID in the file matches the requested priorRunId
      if (!doc.RootElement.TryGetProperty("runId", out var storedRunId))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      if (!string.Equals(storedRunId.GetString(), priorRunId, StringComparison.OrdinalIgnoreCase))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      if (!doc.RootElement.TryGetProperty("steps", out var stepsEl))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var step in stepsEl.EnumerateArray())
      {
        if (step.TryGetProperty("StepName", out var stepName)
          && step.TryGetProperty("ArtifactSha256", out var sha)
          && sha.ValueKind == JsonValueKind.String
          && !string.IsNullOrWhiteSpace(sha.GetString()))
        {
          result[stepName.GetString()!] = sha.GetString()!;
        }
      }
      return result;
    }
    catch
    {
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
  }

  private static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    using var sha = System.Security.Cryptography.SHA256.Create();
    var hash = sha.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
  }

  /// <summary>
  /// Injects W3C trace context into PowerShell process environment variables.
  /// PowerShell scripts can access via $env:STIGFORGE_TRACE_ID, $env:STIGFORGE_PARENT_SPAN_ID.
  /// </summary>
  private static void InjectTraceContext(ProcessStartInfo psi)
  {
    var context = TraceContext.GetCurrentContext();
    if (context != null)
    {
      psi.Environment["STIGFORGE_TRACE_ID"] = context.TraceId;
      psi.Environment["STIGFORGE_PARENT_SPAN_ID"] = context.SpanId;
      psi.Environment["STIGFORGE_TRACE_FLAGS"] = context.TraceFlags;
    }
  }

  private static HardeningMode? TryReadModeFromManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
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

    var command =
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
    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath)) planned.Add(PowerStigStepName);
    if (!string.IsNullOrWhiteSpace(request.ScriptPath)) planned.Add(ScriptStepName);
    if (!string.IsNullOrWhiteSpace(request.DscMofPath)) planned.Add(DscStepName);
    if (!string.IsNullOrWhiteSpace(request.LgpoPolFilePath)) planned.Add(LgpoStepName);
    return planned;
  }

  private async Task<ApplyStepOutcome> RunLgpoAsync(
    ApplyRequest request,
    string bundleRoot,
    string logsDir,
    CancellationToken ct)
  {
    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_lgpo_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_lgpo_" + stepId + ".err.log");

    var started = DateTimeOffset.Now;

    var lgpoRequest = new Lgpo.LgpoApplyRequest
    {
      PolFilePath = request.LgpoPolFilePath!,
      Scope = request.LgpoScope ?? Lgpo.LgpoScope.Machine,
      LgpoExePath = request.LgpoExePath
    };

    var result = await _lgpoRunner!.ApplyPolicyAsync(lgpoRequest, ct).ConfigureAwait(false);

    File.WriteAllText(stdout, result.StdOut);
    File.WriteAllText(stderr, result.StdErr);

    return new ApplyStepOutcome
    {
      StepName = LgpoStepName,
      ExitCode = result.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
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
