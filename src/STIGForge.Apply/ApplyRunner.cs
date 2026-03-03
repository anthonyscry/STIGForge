using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.Reboot;
using STIGForge.Apply.Snapshot;
using STIGForge.Apply.Steps;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Evidence;

namespace STIGForge.Apply;

public class ApplyRunner
{
  private const string PowerStigStepName = "powerstig_compile";
  private const string ScriptStepName = "apply_script";
  private const string DscStepName = "apply_dsc";
  private const string LgpoStepName = "apply_lgpo";
  private const string AdmxStepName = "apply_admx_templates";
  private const string GpoImportStepName = "apply_gpo_import";

  private readonly ILogger<ApplyRunner> _logger;
  private readonly SnapshotService _snapshotService;
  private readonly RollbackScriptGenerator _rollbackScriptGenerator;
  private readonly LcmService _lcmService;
  private readonly RebootCoordinator _rebootCoordinator;
  private readonly IAuditTrailService? _audit;

  private readonly PowerStigStepHandler _powerStigStepHandler;
  private readonly ScriptStepHandler _scriptStepHandler;
  private readonly DscStepHandler _dscStepHandler;
  private readonly PolicyStepHandler _policyStepHandler;
  private readonly StepEvidenceWriter _stepEvidenceWriter;

  public ApplyRunner(
    ILogger<ApplyRunner> logger,
    SnapshotService snapshotService,
    RollbackScriptGenerator rollbackScriptGenerator,
    LcmService lcmService,
    RebootCoordinator rebootCoordinator,
    IAuditTrailService? audit = null,
    EvidenceCollector? evidenceCollector = null,
    Lgpo.LgpoRunner? lgpoRunner = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
    _rollbackScriptGenerator = rollbackScriptGenerator ?? throw new ArgumentNullException(nameof(rollbackScriptGenerator));
    _lcmService = lcmService ?? throw new ArgumentNullException(nameof(lcmService));
    _rebootCoordinator = rebootCoordinator ?? throw new ArgumentNullException(nameof(rebootCoordinator));
    _audit = audit;

    _powerStigStepHandler = new PowerStigStepHandler();
    _scriptStepHandler = new ScriptStepHandler();
    _dscStepHandler = new DscStepHandler();
    _policyStepHandler = new PolicyStepHandler(_logger, lgpoRunner);
    _stepEvidenceWriter = new StepEvidenceWriter(_logger, evidenceCollector);
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
    if (request.DryRun)
    {
      mode = HardeningMode.AuditOnly;
      _logger.LogInformation("Dry-run mode active - forcing HardeningMode.AuditOnly");
    }

    var dryRunCollector = request.DryRun ? new DryRun.DryRunCollector() : null;
    var runId = string.IsNullOrWhiteSpace(request.RunId) ? Guid.NewGuid().ToString() : request.RunId!;
    var priorRunId = request.PriorRunId;
    var priorStepSha256 = StepEvidenceWriter.LoadPriorRunStepSha256(root, priorRunId);
    var plannedSteps = BuildPlannedStepNames(request);

    RebootContext? resumeContext;
    try
    {
      resumeContext = await _rebootCoordinator.ResumeAfterReboot(root, ct).ConfigureAwait(false);
    }
    catch (RebootException ex)
    {
      throw new InvalidOperationException(
        "Resume context is invalid or exhausted. Automatic continuation is blocked until operator decision. " +
        "Review Apply/.resume_marker.json and recovery artifacts before retrying.",
        ex);
    }

    if (resumeContext != null)
    {
      ValidateResumeContext(resumeContext, plannedSteps, root);
      _logger.LogInformation(
        "Resuming apply after reboot from step {CurrentStepIndex} ({CompletedCount} steps completed)",
        resumeContext.CurrentStepIndex,
        resumeContext.CompletedSteps?.Count ?? 0);

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

    LcmState? originalLcm = null;
    if (!string.IsNullOrWhiteSpace(request.DscMofPath))
      originalLcm = await CaptureAndConfigureLcmAsync(mode, ct).ConfigureAwait(false);

    if (!request.SkipSnapshot)
      snapshot = await CreateSnapshotAsync(snapshotsDir, request.LgpoExePath, ct).ConfigureAwait(false);

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

        PowerStigTarget? pstigTarget = null;
        if (request.OsTarget.HasValue && request.OsTarget.Value != Core.Models.OsTarget.Unknown)
        {
          pstigTarget = PowerStigTechnologyMap.Resolve(
            request.OsTarget.Value,
            request.RoleTemplate ?? Core.Models.RoleTemplate.Workstation);
          if (pstigTarget != null)
          {
            _logger.LogInformation(
              "Resolved PowerSTIG target: {Resource} OsVersion={OsVersion} OsRole={OsRole}",
              pstigTarget.CompositeResourceName,
              pstigTarget.OsVersion,
              pstigTarget.OsRole ?? "(none)");
          }
        }

        var outcome = await _powerStigStepHandler.RunCompileAsync(
          request.PowerStigModulePath!,
          request.PowerStigDataFile,
          pstigOutputPath,
          root,
          logsDir,
          snapshotsDir,
          mode,
          request.PowerStigVerbose,
          PowerStigStepName,
          ct,
          pstigTarget).ConfigureAwait(false);

        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);

        if (outcome.ExitCode != 0)
        {
          _logger.LogError("PowerSTIG compile failed (exit code {ExitCode}). Skipping dependent DSC apply step.", outcome.ExitCode);
          if (!string.IsNullOrWhiteSpace(outcome.StdErrPath) && File.Exists(outcome.StdErrPath))
          {
            try
            {
              _logger.LogError("PowerSTIG stderr:\n{StdErr}", File.ReadAllText(outcome.StdErrPath));
            }
            catch
            {
            }
          }
        }
      }

      var rebootResult = await TryScheduleRebootAsync(
        root,
        mode,
        steps,
        snapshot,
        snapshotsDir,
        priorRunId,
        runId,
        ct,
        "Reboot required after PowerSTIG compile").ConfigureAwait(false);
      if (rebootResult != null)
        return rebootResult;
    }

    if (!string.IsNullOrWhiteSpace(request.ScriptPath))
    {
      if (completedSteps.Contains(ScriptStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", ScriptStepName);
      }
      else
      {
        if (dryRunCollector != null)
        {
          dryRunCollector.Add(
            "Script",
            "Script execution with STIGFORGE_DRY_RUN=true",
            null,
            request.ScriptPath,
            null,
            "Script",
            request.ScriptPath);
        }

        var outcome = await _scriptStepHandler.RunAsync(
          request.ScriptPath!,
          request.ScriptArgs,
          root,
          logsDir,
          snapshotsDir,
          mode,
          ScriptStepName,
          ct).ConfigureAwait(false);
        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }

      var rebootResult = await TryScheduleRebootAsync(
        root,
        mode,
        steps,
        snapshot,
        snapshotsDir,
        priorRunId,
        runId,
        ct,
        "Reboot required after script execution").ConfigureAwait(false);
      if (rebootResult != null)
        return rebootResult;
    }

    var compileStepFailed = steps.Any(s => s.StepName == PowerStigStepName && s.ExitCode != 0);
    if (!string.IsNullOrWhiteSpace(request.DscMofPath) && !compileStepFailed)
    {
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
        var outcome = await _dscStepHandler.RunAsync(
          request.DscMofPath!,
          root,
          logsDir,
          snapshotsDir,
          mode,
          request.DscVerbose,
          DscStepName,
          ct).ConfigureAwait(false);
        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);

        if (dryRunCollector != null && File.Exists(outcome.StdOutPath))
        {
          var whatIfOutput = File.ReadAllText(outcome.StdOutPath);
          var dscChanges = DryRun.DscWhatIfParser.Parse(whatIfOutput);
          dryRunCollector.AddRange("DSC", dscChanges);
        }

        steps.Add(outcome);
      }

      if (originalLcm != null && request.ResetLcmAfterApply)
        await TryResetLcmAsync(originalLcm, ct).ConfigureAwait(false);
    }

    if (!string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath))
    {
      if (completedSteps.Contains(AdmxStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", AdmxStepName);
      }
      else
      {
        var outcome = _policyStepHandler.RunAdmxImport(request, root, logsDir, AdmxStepName);
        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }
    }

    if (!string.IsNullOrWhiteSpace(request.LgpoPolFilePath) && _policyStepHandler.CanRunLgpo)
    {
      if (completedSteps.Contains(LgpoStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", LgpoStepName);
      }
      else
      {
        if (dryRunCollector != null)
        {
          dryRunCollector.Add(
            "LGPO",
            "LGPO policy would be applied: " + request.LgpoPolFilePath,
            null,
            request.LgpoPolFilePath,
            null,
            "GroupPolicy",
            request.LgpoPolFilePath);
        }

        var outcome = await _policyStepHandler.RunLgpoAsync(request, logsDir, LgpoStepName, ct).ConfigureAwait(false);
        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }

      var rebootCount = resumeContext?.RebootCount ?? 0;
      var rebootResult = await TryScheduleRebootAsync(
        root,
        mode,
        steps,
        snapshot,
        snapshotsDir,
        priorRunId,
        runId,
        ct,
        "Reboot required after LGPO apply",
        rebootCount,
        rebootCount + 1,
        ConvergenceStatus.Diverged).ConfigureAwait(false);
      if (rebootResult != null)
        return rebootResult;
    }

    if (!string.IsNullOrWhiteSpace(request.DomainGpoBackupPath)
        && request.RoleTemplate == Core.Models.RoleTemplate.DomainController)
    {
      if (completedSteps.Contains(GpoImportStepName))
      {
        _logger.LogInformation("Skipping previously completed step: {StepName}", GpoImportStepName);
      }
      else
      {
        var outcome = _policyStepHandler.RunGpoImport(request, logsDir, GpoImportStepName);
        outcome = _stepEvidenceWriter.Write(outcome, root, runId, priorStepSha256);
        steps.Add(outcome);
      }
    }

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

    if (dryRunCollector != null)
      return BuildDryRunResult(request, root, mode, steps, logPath, dryRunCollector);

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
      throw new InvalidOperationException(BuildBlockingFailureMessage(blockingFailures, recoveryArtifacts));

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

  private async Task<LcmState?> CaptureAndConfigureLcmAsync(HardeningMode mode, CancellationToken ct)
  {
    try
    {
      _logger.LogInformation("Capturing original LCM state...");
      var originalLcm = await _lcmService.GetLcmState(ct).ConfigureAwait(false);

      var lcmConfig = new LcmConfig
      {
        ConfigurationMode = mode == HardeningMode.AuditOnly ? "ApplyOnly" : "ApplyAndMonitor",
        RebootNodeIfNeeded = true,
        ConfigurationModeFrequencyMins = 15,
        AllowModuleOverwrite = true
      };

      _logger.LogInformation(
        "Configuring LCM for DSC application (Mode: {ConfigurationMode})...",
        lcmConfig.ConfigurationMode);
      await _lcmService.ConfigureLcm(lcmConfig, ct).ConfigureAwait(false);
      return originalLcm;
    }
    catch (LcmException ex)
    {
      _logger.LogError(ex, "LCM configuration failed. Apply aborted.");
      throw new InvalidOperationException("LCM configuration failed, apply aborted.", ex);
    }
  }

  private async Task<SnapshotResult?> CreateSnapshotAsync(string snapshotsDir, string? lgpoExePath, CancellationToken ct)
  {
    try
    {
      _logger.LogInformation("Creating pre-apply snapshot...");
      var snapshot = await _snapshotService.CreateSnapshot(snapshotsDir, ct, lgpoExePath).ConfigureAwait(false);
      snapshot.RollbackScriptPath = _rollbackScriptGenerator.GenerateScript(snapshot);
      _logger.LogInformation(
        "Snapshot {SnapshotId} created. Rollback script: {RollbackScript}",
        snapshot.SnapshotId,
        snapshot.RollbackScriptPath);
      return snapshot;
    }
    catch (SnapshotException ex)
    {
      _logger.LogError(ex, "Snapshot creation failed. Apply aborted.");
      throw new InvalidOperationException("Snapshot failed, apply aborted.", ex);
    }
  }

  private async Task TryResetLcmAsync(LcmState originalLcm, CancellationToken ct)
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

  private async Task<ApplyResult?> TryScheduleRebootAsync(
    string root,
    HardeningMode mode,
    List<ApplyStepOutcome> steps,
    SnapshotResult? snapshot,
    string snapshotsDir,
    string? priorRunId,
    string runId,
    CancellationToken ct,
    string reason,
    int? contextRebootCount = null,
    int? resultRebootCount = null,
    ConvergenceStatus? convergenceStatus = null)
  {
    if (!await _rebootCoordinator.DetectRebootRequired(ct).ConfigureAwait(false))
      return null;

    _logger.LogInformation(reason);
    var context = new RebootContext
    {
      BundleRoot = root,
      CurrentStepIndex = steps.Count,
      CompletedSteps = steps.Select(s => s.StepName).ToList(),
      RebootScheduledAt = DateTimeOffset.UtcNow,
      RebootCount = contextRebootCount ?? 0
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
      RebootCount = resultRebootCount ?? 0,
      ConvergenceStatus = convergenceStatus ?? ConvergenceStatus.NotApplicable
    };
  }

  private ApplyResult BuildDryRunResult(
    ApplyRequest request,
    string root,
    HardeningMode mode,
    IReadOnlyList<ApplyStepOutcome> steps,
    string logPath,
    DryRun.DryRunCollector dryRunCollector)
  {
    var report = dryRunCollector.Build(root, mode.ToString());
    var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    var reportPath = Path.Combine(root, "Apply", "dry_run_report.json");
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, reportJson);
    _logger.LogInformation("Dry-run report written to {Path} with {Count} proposed changes", reportPath, report.TotalChanges);

    return new ApplyResult
    {
      BundleRoot = root,
      Mode = mode,
      LogPath = logPath,
      Steps = steps,
      SnapshotId = string.Empty,
      IsMissionComplete = false,
      DryRunReport = report,
      RunId = request.RunId
    };
  }

  private static HardeningMode? TryReadModeFromManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return null;

    using var stream = File.OpenRead(manifestPath);
    using var doc = JsonDocument.Parse(stream);
    if (!doc.RootElement.TryGetProperty("Profile", out var profile))
      return null;
    if (!profile.TryGetProperty("HardeningMode", out var mode))
      return null;

    if (mode.ValueKind == JsonValueKind.String)
    {
      var value = mode.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return null;

      return Enum.TryParse<HardeningMode>(value, true, out var parsedFromString) ? parsedFromString : null;
    }

    if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out var numeric) && Enum.IsDefined(typeof(HardeningMode), numeric))
      return (HardeningMode)numeric;

    return null;
  }

  private static IReadOnlyList<string> BuildPlannedStepNames(ApplyRequest request)
  {
    var planned = new List<string>();
    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath))
      planned.Add(PowerStigStepName);
    if (!string.IsNullOrWhiteSpace(request.ScriptPath))
      planned.Add(ScriptStepName);
    if (!string.IsNullOrWhiteSpace(request.DscMofPath))
      planned.Add(DscStepName);
    if (!string.IsNullOrWhiteSpace(request.AdmxTemplateRootPath))
      planned.Add(AdmxStepName);
    if (!string.IsNullOrWhiteSpace(request.LgpoPolFilePath))
      planned.Add(LgpoStepName);
    if (!string.IsNullOrWhiteSpace(request.DomainGpoBackupPath)
        && request.RoleTemplate == Core.Models.RoleTemplate.DomainController)
    {
      planned.Add(GpoImportStepName);
    }

    return planned;
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
    if (rollbackScriptPath is { Length: > 0 })
      artifacts.Add(rollbackScriptPath);
    if (Directory.Exists(snapshotsDir))
      artifacts.Add(snapshotsDir);
    if (!string.IsNullOrWhiteSpace(logPath))
      artifacts.Add(logPath);

    return artifacts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  }

  private static string BuildBlockingFailureMessage(IReadOnlyList<string> blockingFailures, IReadOnlyList<string> recoveryArtifacts)
  {
    var sb = new StringBuilder();
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
