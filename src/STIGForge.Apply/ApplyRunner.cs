using System.Diagnostics;
using System.Text;
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

public class ApplyRunner
{
    private const string PowerStigStepName = "powerstig_compile";
    private const string ScriptStepName = "apply_script";
    private const string DscStepName = "apply_dsc";
    private const string LgpoStepName = "apply_lgpo";
    private const string AdmxStepName = "apply_admx_templates";

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

   public virtual async Task<ApplyResult> RunAsync(ApplyRequest request, CancellationToken ct)
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
          snapshot = await _snapshotService.CreateSnapshot(snapshotsDir, ct, request.LgpoExePath).ConfigureAwait(false);
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
        var pstigOutputPath = string.IsNullOrWhiteSpace(request.PowerStigOutputPath)
          ? Path.Combine(applyRoot, "Dsc")
          : request.PowerStigOutputPath!;

        // Resolve PowerSTIG target from OsTarget when available
        Dsc.PowerStigTarget? pstigTarget = null;
        if (request.OsTarget.HasValue && request.OsTarget.Value != Core.Models.OsTarget.Unknown)
        {
          pstigTarget = Dsc.PowerStigTechnologyMap.Resolve(
            request.OsTarget.Value,
            request.RoleTemplate ?? Core.Models.RoleTemplate.Workstation);
          if (pstigTarget != null)
            _logger.LogInformation("Resolved PowerSTIG target: {Resource} OsVersion={OsVersion} OsRole={OsRole}",
              pstigTarget.CompositeResourceName, pstigTarget.OsVersion, pstigTarget.OsRole ?? "(none)");
        }

        var outcome = await RunPowerStigCompileAsync(
           request.PowerStigModulePath!,
           request.PowerStigDataFile,
           pstigOutputPath,
           root,
           logsDir,
           snapshotsDir,
           mode,
           request.PowerStigVerbose,
           ct,
           pstigTarget).ConfigureAwait(false);

        outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);

        if (outcome.ExitCode != 0)
        {
          _logger.LogError("PowerSTIG compile failed (exit code {ExitCode}). Skipping dependent DSC apply step.", outcome.ExitCode);
          if (!string.IsNullOrWhiteSpace(outcome.StdErrPath) && File.Exists(outcome.StdErrPath))
          {
            try { _logger.LogError("PowerSTIG stderr:\n{StdErr}", File.ReadAllText(outcome.StdErrPath)); } catch { }
          }
        }
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

   // Skip DSC apply if PowerSTIG compilation failed (no MOFs to apply)
   var compileStepFailed = steps.Any(s => s.StepName == PowerStigStepName && s.ExitCode != 0);

   if (!string.IsNullOrWhiteSpace(request.DscMofPath) && !compileStepFailed)
      {
        // Verify MOF files actually exist before attempting apply
        var hasMofs = Directory.Exists(request.DscMofPath)
          && Directory.EnumerateFiles(request.DscMofPath, "*.mof", SearchOption.TopDirectoryOnly).Any();

        if (!hasMofs)
        {
          _logger.LogWarning("Skipping DSC apply: no .mof files found in {DscMofPath}", request.DscMofPath);
        }
        else if (completedSteps.Contains(DscStepName))
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

    // ADMX template step
    if (!string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath))
    {
      if (completedSteps.Contains(AdmxStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", AdmxStepName);
      }
      else
      {
        var outcome = RunAdmxImport(request, root, logsDir);
        outcome = WriteStepEvidence(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
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
  /// <summary>
  /// Builds PowerShell arguments using -EncodedCommand to avoid all quoting/escaping
  /// issues with complex scripts containing curly braces, dollar signs, and nested quotes.
  /// </summary>
  private static string BuildEncodedCommandArgs(string command)
  {
    var bytes = Encoding.Unicode.GetBytes(command);
    var encoded = Convert.ToBase64String(bytes);
    return "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
  }

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

    InjectTraceContext(psi);

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

    // Start-DscConfiguration invokes the LCM which needs DSC resource modules on PSModulePath.
    // Copy modules from bundled + bundle Apply dir into the system PSModulePath so the LCM
    // can find them. We use Copy-Item rather than just PSModulePath because the LCM runs
    // in a separate process (WMI host) that doesn't inherit our environment variables.
    var copyModulesBlock = new System.Text.StringBuilder();
    var bundledModulesDir = Path.Combine(AppContext.BaseDirectory, "tools", "PSModules");
    var bundleApplyDir = Path.Combine(bundleRoot, "Apply");
    var moduleSourceDirs = new List<string>();
    if (Directory.Exists(bundledModulesDir)) moduleSourceDirs.Add(bundledModulesDir);
    if (Directory.Exists(bundleApplyDir)) moduleSourceDirs.Add(bundleApplyDir);

    foreach (var srcDir in moduleSourceDirs)
    {
      foreach (var modDir in Directory.GetDirectories(srcDir))
      {
        var modName = Path.GetFileName(modDir);
        // Only copy known DSC resource modules, skip non-module dirs like Dsc, Logs, Snapshots
        if (!File.Exists(Path.Combine(modDir, modName + ".psd1")) &&
            !Directory.EnumerateFiles(modDir, "*.psd1", SearchOption.AllDirectories).Any())
          continue;
        copyModulesBlock.AppendLine(
          $"if (-not (Test-Path \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\{modName}\")) {{ " +
          $"Copy-Item -Path '{modDir.Replace("'", "''")}' -Destination \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\{modName}\" -Recurse -Force }}");
      }
    }

    var command = copyModulesBlock.ToString() +
      "Start-DscConfiguration -Path \"" + mofPath + "\" -Wait -Force" + whatIf + v;
    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = BuildEncodedCommandArgs(command),
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    // Also set PSModulePath for the PowerShell host process itself
    var extraModulePaths = new List<string>();
    if (Directory.Exists(bundledModulesDir)) extraModulePaths.Add(bundledModulesDir);
    if (Directory.Exists(bundleApplyDir)) extraModulePaths.Add(bundleApplyDir);
    if (extraModulePaths.Count > 0)
    {
      var existing = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
      psi.Environment["PSModulePath"] = string.Join(";", extraModulePaths) + ";" + existing;
    }

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();

    InjectTraceContext(psi);

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start DSC apply.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(600000))
    {
      process.Kill();
      throw new TimeoutException("DSC apply did not complete within 10 minutes.");
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
    CancellationToken ct,
    Dsc.PowerStigTarget? target = null)
  {
    if (!Directory.Exists(modulePath) && !File.Exists(modulePath))
      throw new FileNotFoundException("PowerSTIG module path not found: " + modulePath, modulePath);

    Directory.CreateDirectory(outputPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".err.log");

    var v = verbose ? " -Verbose" : string.Empty;

    // Collect additional module paths for PSModulePath injection.
    // Import-DscResource is a PARSE-TIME directive — it reads PSModulePath from
    // the process environment BEFORE any script code executes. Setting $env:PSModulePath
    // inside the command string is too late. We must set it on ProcessStartInfo.Environment.
    var extraModulePaths = new List<string>();

    // Bundled modules: <AppDir>/tools/PSModules
    var bundledModulesDir = Path.Combine(AppContext.BaseDirectory, "tools", "PSModules");
    if (Directory.Exists(bundledModulesDir))
      extraModulePaths.Add(bundledModulesDir);

    // User-provided module parent directory.
    // PSModulePath entries must be directories that *contain* module folders.
    // For a .psd1 at Apply/PowerSTIG/4.29.0/PowerSTIG.psd1 we need Apply/ (3 levels up).
    // For a directory at Apply/PowerSTIG we need Apply/ (1 level up).
    string? moduleParent;
    if (File.Exists(modulePath))
    {
      // .psd1 file: Version/Module.psd1 → ModuleName/ → Apply/
      var versionDir = Path.GetDirectoryName(modulePath);
      var moduleDir = versionDir != null ? Path.GetDirectoryName(versionDir) : null;
      moduleParent = moduleDir != null ? Path.GetDirectoryName(moduleDir) : null;
    }
    else
    {
      // Directory (e.g. Apply/PowerSTIG) → Apply/
      moduleParent = Path.GetDirectoryName(modulePath);
    }
    if (!string.IsNullOrWhiteSpace(moduleParent))
      extraModulePaths.Add(moduleParent!);

    // Also add the Apply directory from the bundle root — this is where
    // EnsurePowerStigDependenciesStaged copies sibling dependency modules.
    var bundleApplyDir = Path.Combine(bundleRoot, "Apply");
    if (Directory.Exists(bundleApplyDir) && !extraModulePaths.Contains(bundleApplyDir, StringComparer.OrdinalIgnoreCase))
      extraModulePaths.Add(bundleApplyDir);

    // Prefer bundled PowerSTIG module if available
    var bundledPowerStig = Path.Combine(bundledModulesDir, "PowerSTIG");
    var effectiveModulePath = Directory.Exists(bundledPowerStig) ? bundledPowerStig : modulePath;

    // Detect bundled PowerSTIG version from versioned subdirectory (e.g., PowerSTIG/4.29.0/)
    // to pin Import-DscResource -ModuleVersion and avoid "multiple versions found" errors.
    string? powerStigVersion = null;
    if (Directory.Exists(effectiveModulePath))
    {
      foreach (var subDir in Directory.GetDirectories(effectiveModulePath))
      {
        var dirName = Path.GetFileName(subDir);
        if (Version.TryParse(dirName, out _))
        {
          powerStigVersion = dirName;
          // Version detected — will be passed to Import-DscResource -ModuleVersion
          break;
        }
      }
    }

    string command;
    if (target != null)
    {
      // OS-targeted compilation using PowerSTIG composite DSC resources
      var configScript = Dsc.PowerStigTechnologyMap.BuildDscConfigurationScript(target, outputPath, dataFile, powerStigVersion);
      command = "Import-Module \"" + effectiveModulePath + "\"; " + configScript + v + ";";
    }
    else
    {
      // Legacy fallback: generic PowerSTIG compilation without OS targeting
      var dataArg = string.IsNullOrWhiteSpace(dataFile) ? string.Empty : " -StigDataFile \"" + dataFile + "\"";
      command =
        "Import-Module \"" + effectiveModulePath + "\"; " +
        "$ErrorActionPreference='Stop'; " +
        "New-StigDscConfiguration" + dataArg + " -OutputPath \"" + outputPath + "\"" + v + ";";
    }

    // Write the command to a script file for debugging and log it
    var scriptPath = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".ps1");
    File.WriteAllText(scriptPath, command);

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = BuildEncodedCommandArgs(command),
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    // Prepend bundled + user module paths to PSModulePath so Import-DscResource
    // (parse-time) and Import-Module (runtime) both find PowerSTIG and dependencies.
    if (extraModulePaths.Count > 0)
    {
      var existingPsModulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
      psi.Environment["PSModulePath"] = string.Join(";", extraModulePaths) + ";" + existingPsModulePath;
    }

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();

    InjectTraceContext(psi);

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start PowerSTIG compile.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(300000))
    {
      process.Kill();
      throw new TimeoutException("PowerSTIG compilation did not complete within 5 minutes.");
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
    if (!string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath)) planned.Add(AdmxStepName);
    if (!string.IsNullOrWhiteSpace(request.LgpoPolFilePath)) planned.Add(LgpoStepName);
    return planned;
  }

  private ApplyStepOutcome RunAdmxImport(ApplyRequest request, string bundleRoot, string logsDir)
  {
    var started = DateTimeOffset.Now;
    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_admx_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_admx_" + stepId + ".err.log");

    var outBuilder = new StringBuilder(2048);
    var errBuilder = new StringBuilder(1024);
    var exitCode = 0;

    try
    {
      var sourceRoot = request.AdmxTemplateRootPath!;
      if (!Directory.Exists(sourceRoot))
        throw new DirectoryNotFoundException("ADMX template root not found: " + sourceRoot);

      var targetRoot = ResolvePolicyDefinitionsTarget(request, bundleRoot);
      if (string.IsNullOrWhiteSpace(targetRoot))
        throw new InvalidOperationException("Unable to resolve PolicyDefinitions target path for ADMX import.");

      Directory.CreateDirectory(targetRoot);

      var copiedAdmx = 0;
      var copiedAdml = 0;
      var skipped = new List<string>();

      foreach (var admxPath in Directory.EnumerateFiles(sourceRoot, "*.admx", SearchOption.AllDirectories)
                   .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
      {
        if (!IsTemplateApplicableToCurrentOs(admxPath))
        {
          skipped.Add(admxPath);
          continue;
        }

        CopyTemplateFile(sourceRoot, targetRoot, admxPath);
        copiedAdmx++;

        var baseName = Path.GetFileNameWithoutExtension(admxPath);
        foreach (var admlPath in Directory.EnumerateFiles(sourceRoot, baseName + ".adml", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
          CopyTemplateFile(sourceRoot, targetRoot, admlPath);
          copiedAdml++;
        }
      }

      outBuilder.AppendLine("ADMX import complete.");
      outBuilder.AppendLine("Source: " + sourceRoot);
      outBuilder.AppendLine("Target: " + targetRoot);
      outBuilder.AppendLine("Copied .admx: " + copiedAdmx);
      outBuilder.AppendLine("Copied .adml: " + copiedAdml);
      outBuilder.AppendLine("Skipped (non-applicable): " + skipped.Count);

      if (copiedAdmx == 0)
      {
        errBuilder.AppendLine("No applicable ADMX templates were found to import.");
        exitCode = 1;
      }
    }
    catch (Exception ex)
    {
      errBuilder.AppendLine(ex.Message);
      exitCode = 1;
    }

    File.WriteAllText(stdout, outBuilder.ToString());
    File.WriteAllText(stderr, errBuilder.ToString());

    return new ApplyStepOutcome
    {
      StepName = AdmxStepName,
      ExitCode = exitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private static string? ResolvePolicyDefinitionsTarget(ApplyRequest request, string bundleRoot)
  {
    if (!string.IsNullOrWhiteSpace(request.AdmxPolicyDefinitionsPath))
      return request.AdmxPolicyDefinitionsPath;

    if (OperatingSystem.IsWindows())
    {
      var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
      if (!string.IsNullOrWhiteSpace(windowsDir))
        return Path.Combine(windowsDir, "PolicyDefinitions");
    }

    return Path.Combine(bundleRoot, "Apply", "PolicyDefinitions");
  }

  private static bool IsTemplateApplicableToCurrentOs(string path)
  {
    var normalized = path.Replace('\\', '/').ToLowerInvariant();
    var os = DetectHostOsTag();

    var isWin11Tagged = normalized.Contains("windows11") || normalized.Contains("windows_11") || normalized.Contains("win11");
    var isWin10Tagged = normalized.Contains("windows10") || normalized.Contains("windows_10") || normalized.Contains("win10");
    var isServerTagged = normalized.Contains("server");

    var isServer2022Tagged = normalized.Contains("2022");
    var isServer2019Tagged = normalized.Contains("2019");
    var isServer2016Tagged = normalized.Contains("2016");

    var hasAnyOsTag = isWin11Tagged || isWin10Tagged || isServerTagged;
    if (!hasAnyOsTag)
      return true;

    return os switch
    {
      "win11" => isWin11Tagged,
      "win10" => isWin10Tagged,
      "server2022" => isServerTagged && (isServer2022Tagged || (!isServer2019Tagged && !isServer2016Tagged)),
      "server2019" => isServerTagged && (isServer2019Tagged || (!isServer2022Tagged && !isServer2016Tagged)),
      "server2016" => isServerTagged && (isServer2016Tagged || (!isServer2022Tagged && !isServer2019Tagged)),
      "server" => isServerTagged,
      _ => true
    };
  }

  private static string DetectHostOsTag()
  {
    if (!OperatingSystem.IsWindows())
      return "other";

    var productName = string.Empty;
    try
    {
      productName = Microsoft.Win32.Registry.GetValue(
          @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
          "ProductName",
          null) as string ?? string.Empty;
    }
    catch
    {
      productName = string.Empty;
    }

    if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0)
      return "win11";
    if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
      return "win10";
    if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0) return "server2022";
      if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0) return "server2019";
      if (productName.IndexOf("2016", StringComparison.OrdinalIgnoreCase) >= 0) return "server2016";
      return "server";
    }

    return "other";
  }

  private static void CopyTemplateFile(string sourceRoot, string targetRoot, string sourceFile)
  {
    var relative = Path.GetRelativePath(sourceRoot, sourceFile);
    var destination = Path.Combine(targetRoot, relative);
    var destinationDir = Path.GetDirectoryName(destination);
    if (!string.IsNullOrWhiteSpace(destinationDir))
      Directory.CreateDirectory(destinationDir);

    File.Copy(sourceFile, destination, true);
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
