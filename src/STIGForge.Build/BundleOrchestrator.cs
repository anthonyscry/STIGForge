using System.Text;
using System.Text.Json;
using STIGForge.Apply;
using STIGForge.Apply.PowerStig;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.Telemetry;
using STIGForge.Verify;

namespace STIGForge.Build;

public sealed class BundleOrchestrator
{
  private readonly BundleBuilder _builder;
  private readonly ApplyRunner _apply;
  private readonly IVerificationWorkflowService _verificationWorkflow;
  private readonly VerificationArtifactAggregationService _artifactAggregation;
  private readonly MissionTracingService _tracing;
  private readonly IAuditTrailService? _audit;
  private readonly IMissionRunRepository? _missionRunRepository;

  public BundleOrchestrator(
      BundleBuilder builder,
      ApplyRunner apply,
      IVerificationWorkflowService verificationWorkflow,
      VerificationArtifactAggregationService artifactAggregation,
      MissionTracingService tracing,
      IAuditTrailService? audit = null,
      IMissionRunRepository? missionRunRepository = null)
  {
    _builder = builder;
    _apply = apply;
    _verificationWorkflow = verificationWorkflow;
    _artifactAggregation = artifactAggregation;
    _tracing = tracing ?? throw new ArgumentNullException(nameof(tracing));
    _audit = audit;
    _missionRunRepository = missionRunRepository;
  }

  public async Task<BundleBuildResult> BuildBundleAsync(BundleBuildRequest request, CancellationToken ct)
  {
    return await _builder.BuildAsync(request, ct).ConfigureAwait(false);
  }

  public async Task OrchestrateAsync(OrchestrateRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var root = request.BundleRoot.Trim();
    if (!Directory.Exists(root))
      throw new DirectoryNotFoundException("Bundle root not found: " + root);

    var verifyRoot = string.IsNullOrWhiteSpace(request.VerifyOutputRoot)
      ? Path.Combine(root, "Verify")
      : request.VerifyOutputRoot!;

    Directory.CreateDirectory(verifyRoot);
    Directory.CreateDirectory(Path.Combine(root, "Reports"));

    if (request.SkipSnapshot)
    {
      ValidateBreakGlass(request.BreakGlassAcknowledged, request.BreakGlassReason, "orchestrate --skip-snapshot");
      await RecordBreakGlassAsync(root, "skip-snapshot", request.BreakGlassReason, ct).ConfigureAwait(false);
    }

    // Load overlay decisions to filter NotApplicable controls
    var overlayDecisions = LoadOverlayDecisions(root);
    var notApplicableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var decision in overlayDecisions)
    {
      if (decision.outcome?.statusOverride == "NotApplicable")
      {
        notApplicableKeys.Add(decision.key);
      }
    }

    // Start a mission run and emit timeline events if a repository is wired
    var runId = Guid.NewGuid().ToString("19D");
    var run = new MissionRun
    {
      RunId = runId,
      Label = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
      BundleRoot = root,
      Status = MissionRunStatus.Running,
      CreatedAt = DateTimeOffset.UtcNow
    };

    if (_missionRunRepository != null)
      await _missionRunRepository.CreateRunAsync(run, ct).ConfigureAwait(false);

    var seq = 0;

    // Start root mission span for end-to-end tracing
    using var missionActivity = _tracing.StartMissionSpan(root, runId);

    try
    {
      var psd1Path = request.PowerStigDataFile;
      if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath) && string.IsNullOrWhiteSpace(psd1Path))
      {
        var generated = Path.Combine(root, "Apply", "PowerStigData", "stigdata.psd1");
        Directory.CreateDirectory(Path.GetDirectoryName(generated)!);

        // Load bundle controls and filter out NotApplicable from overlay decisions
        var allControls = LoadBundleControls(root);
        var filteredControls = allControls
          .Where(c => !IsControlNotApplicable(c, notApplicableKeys))
          .ToList();

        var overrides = LoadBundlePowerStigOverrides(root);

        // Generate PowerStig data from filtered controls
        var data = filteredControls.Count > 0 || overrides.Count > 0
          ? PowerStigDataGenerator.CreateFromControls(filteredControls, overrides)
          : PowerStigDataGenerator.CreateDefault(request.PowerStigModulePath!, request.BundleRoot);

        PowerStigDataWriter.Write(generated, data);
        psd1Path = generated;
      }

      // --- Apply phase ---
      using var applyActivity = _tracing.StartPhaseSpan(ActivitySourceNames.ApplyPhase, root);
      await AppendEventAsync(runId, ++seq, MissionPhase.Apply, "apply", MissionEventStatus.Started, null, ct).ConfigureAwait(false);

      ApplyResult applyResult;
      try
      {
        applyResult = await _apply.RunAsync(new ApplyRequest
        {
          BundleRoot = root,
          ScriptPath = ResolveApplyScript(root, request.ApplyScriptPath),
          ScriptArgs = BuildApplyArgs(root, request),
          SkipSnapshot = request.SkipSnapshot,
          DscMofPath = request.DscMofPath,
          DscVerbose = request.DscVerbose,
          PowerStigModulePath = request.PowerStigModulePath,
          PowerStigDataFile = psd1Path,
          PowerStigOutputPath = request.PowerStigOutputPath,
          PowerStigVerbose = request.PowerStigVerbose,
          PowerStigDataGeneratedPath = psd1Path,
          RunId = runId
        }, ct).ConfigureAwait(false);

        WritePhaseMarker(Path.Combine(root, "Apply", "apply.complete"), applyResult.LogPath);
        _tracing.SetStatusOk(applyActivity);
        _tracing.AddPhaseEvent(applyActivity, "apply_completed", $"Steps={applyResult.Steps.Count}");
        await AppendEventAsync(runId, ++seq, MissionPhase.Apply, "apply", MissionEventStatus.Finished,
          "Apply completed. Steps=" + applyResult.Steps.Count, ct).ConfigureAwait(false);
      }
      catch (Exception applyEx)
      {
        _tracing.SetStatusError(applyActivity, applyEx.Message);
        applyActivity?.SetTag("error.type", applyEx.GetType().FullName);
        await AppendEventAsync(runId, ++seq, MissionPhase.Apply, "apply", MissionEventStatus.Failed,
          applyEx.Message, ct).ConfigureAwait(false);
        await FinishRunAsync(runId, MissionRunStatus.Failed, applyEx.Message, ct).ConfigureAwait(false);
        throw;
      }

      var coverageInputs = new List<VerificationCoverageInput>();

      // --- Verify phase: Evaluate-STIG ---
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
      {
        using var verifyActivity = _tracing.StartPhaseSpan("verify_evaluate_stig", root);
        await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "evaluate_stig", MissionEventStatus.Started, null, ct).ConfigureAwait(false);
        try
        {
          var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
          var evalWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
          {
            OutputRoot = evalOutput,
            ConsolidatedToolLabel = "Evaluate-STIG",
            EvaluateStig = new EvaluateStigWorkflowOptions
            {
              Enabled = true,
              ToolRoot = request.EvaluateStigRoot!,
              Arguments = request.EvaluateStigArgs ?? string.Empty,
              WorkingDirectory = request.EvaluateStigRoot
            }
          }, ct).ConfigureAwait(false);

          var evalRun = evalWorkflow.ToolRuns.FirstOrDefault(r => r.Tool.IndexOf("Evaluate", StringComparison.OrdinalIgnoreCase) >= 0);
          if (evalRun == null || !evalRun.Executed)
            throw new InvalidOperationException("Evaluate-STIG execution did not run successfully.");

          coverageInputs.Add(new VerificationCoverageInput
          {
            ToolLabel = "Evaluate-STIG",
            ReportPath = evalWorkflow.ConsolidatedJsonPath
          });

          _tracing.SetStatusOk(verifyActivity);
          _tracing.AddPhaseEvent(verifyActivity, "evaluate_stig_completed", $"Results={evalWorkflow.ConsolidatedResultCount}");
          await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "evaluate_stig", MissionEventStatus.Finished,
            "Evaluate-STIG completed. Results=" + evalWorkflow.ConsolidatedResultCount, ct).ConfigureAwait(false);
        }
        catch (Exception verifyEx)
        {
          _tracing.SetStatusError(verifyActivity, verifyEx.Message);
          verifyActivity?.SetTag("error.type", verifyEx.GetType().FullName);
          await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "evaluate_stig", MissionEventStatus.Failed,
            verifyEx.Message, ct).ConfigureAwait(false);
          await FinishRunAsync(runId, MissionRunStatus.Failed, verifyEx.Message, ct).ConfigureAwait(false);
          throw;
        }
      }
      else
      {
        await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "evaluate_stig", MissionEventStatus.Skipped,
          "EvaluateStigRoot not configured", ct).ConfigureAwait(false);
      }

      // --- Verify phase: SCAP ---
      if (!string.IsNullOrWhiteSpace(request.ScapCommandPath))
      {
        var toolName = string.IsNullOrWhiteSpace(request.ScapToolLabel) ? "SCAP" : request.ScapToolLabel!;
        using var scapActivity = _tracing.StartPhaseSpan("verify_scap", root);
        await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "scap", MissionEventStatus.Started, null, ct).ConfigureAwait(false);
        try
        {
          var scapOutput = Path.Combine(verifyRoot, "SCAP");
          var scapWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
          {
            OutputRoot = scapOutput,
            ConsolidatedToolLabel = toolName,
            Scap = new ScapWorkflowOptions
            {
              Enabled = true,
              CommandPath = request.ScapCommandPath!,
              Arguments = request.ScapArgs ?? string.Empty,
              ToolLabel = toolName
            }
          }, ct).ConfigureAwait(false);

          var scapRun = scapWorkflow.ToolRuns.FirstOrDefault(r => string.Equals(r.Tool, toolName, StringComparison.OrdinalIgnoreCase) || r.Tool.IndexOf("SCAP", StringComparison.OrdinalIgnoreCase) >= 0);
          if (scapRun == null || !scapRun.Executed)
            throw new InvalidOperationException(toolName + " execution did not run successfully.");

          coverageInputs.Add(new VerificationCoverageInput
          {
            ToolLabel = toolName,
            ReportPath = scapWorkflow.ConsolidatedJsonPath
          });

          _tracing.SetStatusOk(scapActivity);
          _tracing.AddPhaseEvent(scapActivity, "scap_completed", $"Results={scapWorkflow.ConsolidatedResultCount}");
          await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "scap", MissionEventStatus.Finished,
            toolName + " completed. Results=" + scapWorkflow.ConsolidatedResultCount, ct).ConfigureAwait(false);
        }
        catch (Exception scapEx)
        {
          _tracing.SetStatusError(scapActivity, scapEx.Message);
          scapActivity?.SetTag("error.type", scapEx.GetType().FullName);
          await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "scap", MissionEventStatus.Failed,
            scapEx.Message, ct).ConfigureAwait(false);
          await FinishRunAsync(runId, MissionRunStatus.Failed, scapEx.Message, ct).ConfigureAwait(false);
          throw;
        }
      }
      else
      {
        await AppendEventAsync(runId, ++seq, MissionPhase.Verify, "scap", MissionEventStatus.Skipped,
          "ScapCommandPath not configured", ct).ConfigureAwait(false);
      }

      // --- Evidence phase ---
      if (coverageInputs.Count > 0)
      {
        using var evidenceActivity = _tracing.StartPhaseSpan(ActivitySourceNames.ProvePhase, root);
        await AppendEventAsync(runId, ++seq, MissionPhase.Evidence, "coverage_artifacts", MissionEventStatus.Started, null, ct).ConfigureAwait(false);
        _artifactAggregation.WriteCoverageArtifacts(Path.Combine(root, "Reports"), coverageInputs);
        _tracing.SetStatusOk(evidenceActivity);
        _tracing.AddPhaseEvent(evidenceActivity, "coverage_artifacts_completed", $"CoverageInputs={coverageInputs.Count}");
        await AppendEventAsync(runId, ++seq, MissionPhase.Evidence, "coverage_artifacts", MissionEventStatus.Finished,
          "CoverageInputs=" + coverageInputs.Count, ct).ConfigureAwait(false);
      }
      else
      {
        await AppendEventAsync(runId, ++seq, MissionPhase.Evidence, "coverage_artifacts", MissionEventStatus.Skipped,
          "No verification tools configured", ct).ConfigureAwait(false);
      }

      if (_audit != null)
      {
        try
        {
          await _audit.RecordAsync(new AuditEntry
          {
            Action = "orchestrate",
            Target = root,
            Result = "success",
            Detail = $"CoverageInputs={coverageInputs.Count}; NotApplicableFiltered={notApplicableKeys.Count}",
            User = Environment.UserName,
            Machine = Environment.MachineName,
            Timestamp = DateTimeOffset.Now
          }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          System.Diagnostics.Trace.TraceWarning("Audit write failed during orchestration: " + ex.Message);
        }
      }

      _tracing.SetStatusOk(missionActivity);
      await FinishRunAsync(runId, MissionRunStatus.Completed, null, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _tracing.SetStatusError(missionActivity, ex.Message);
      missionActivity?.SetTag("error.type", ex.GetType().FullName);
      // Failure events are appended at the point of failure; re-throw to caller.
      throw;
    }
  }

  /// <summary>
  /// Load overlay decisions from Reports/overlay_decisions.json.
  /// Returns empty list if file doesn't exist.
  /// </summary>
  private static List<OverlayDecisionDto> LoadOverlayDecisions(string bundleRoot)
  {
    var decisionsPath = Path.Combine(bundleRoot, "Reports", "overlay_decisions.json");
    if (!File.Exists(decisionsPath))
      return new List<OverlayDecisionDto>();

    try
    {
      var json = File.ReadAllText(decisionsPath, Encoding.UTF8);
      var decisions = JsonSerializer.Deserialize<List<OverlayDecisionDto>>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
      return decisions ?? new List<OverlayDecisionDto>();
    }
    catch
    {
      // If deserialization fails, return empty list
      return new List<OverlayDecisionDto>();
    }
  }

  /// <summary>
  /// Check if a control is marked as NotApplicable in overlay decisions.
  /// </summary>
  private static bool IsControlNotApplicable(ControlRecord control, HashSet<string> notApplicableKeys)
  {
    if (notApplicableKeys.Count == 0)
      return false;

    // Check RuleId key
    if (!string.IsNullOrWhiteSpace(control.ExternalIds.RuleId))
    {
      var ruleKey = "RULE:" + control.ExternalIds.RuleId.Trim();
      if (notApplicableKeys.Contains(ruleKey))
        return true;
    }

    // Check VulnId key
    if (!string.IsNullOrWhiteSpace(control.ExternalIds.VulnId))
    {
      var vulnKey = "VULN:" + control.ExternalIds.VulnId.Trim();
      if (notApplicableKeys.Contains(vulnKey))
        return true;
    }

    return false;
  }

  private async Task AppendEventAsync(string runId, int seq, MissionPhase phase, string stepName, MissionEventStatus status, string? message, CancellationToken ct)
  {
    if (_missionRunRepository == null) return;

    try
    {
      await _missionRunRepository.AppendEventAsync(new MissionTimelineEvent
      {
        EventId = Guid.NewGuid().ToString("19D"),
        RunId = runId,
        Seq = seq,
        Phase = phase,
        StepName = stepName,
        Status = status,
        OccurredAt = DateTimeOffset.UtcNow,
        Message = message
      }, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Timeline event append failed (non-blocking): " + ex.Message);
    }
  }

  private async Task FinishRunAsync(string runId, MissionRunStatus status, string? detail, CancellationToken ct)
  {
    if (_missionRunRepository == null) return;

    try
    {
      await _missionRunRepository.UpdateRunStatusAsync(runId, status, DateTimeOffset.UtcNow, detail, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("Mission run status update failed (non-blocking): " + ex.Message);
    }
  }

  private static IReadOnlyList<STIGForge.Core.Models.ControlRecord> LoadBundleControls(string bundleRoot)
  {
    var packControlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(packControlsPath)) return Array.Empty<STIGForge.Core.Models.ControlRecord>();

    var json = File.ReadAllText(packControlsPath);
    var controls = System.Text.Json.JsonSerializer.Deserialize<List<STIGForge.Core.Models.ControlRecord>>(json,
      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (controls == null) return Array.Empty<STIGForge.Core.Models.ControlRecord>();
    return controls;
  }

  private static IReadOnlyList<STIGForge.Core.Models.PowerStigOverride> LoadBundlePowerStigOverrides(string bundleRoot)
  {
    var overlaysPath = Path.Combine(bundleRoot, "Manifest", "overlays.json");
    if (!File.Exists(overlaysPath)) return Array.Empty<STIGForge.Core.Models.PowerStigOverride>();

    var json = File.ReadAllText(overlaysPath);
    var overlays = System.Text.Json.JsonSerializer.Deserialize<List<STIGForge.Core.Models.Overlay>>(json,
      new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (overlays == null) return Array.Empty<STIGForge.Core.Models.PowerStigOverride>();

    var list = new List<STIGForge.Core.Models.PowerStigOverride>();
    foreach (var o in overlays)
      list.AddRange(o.PowerStigOverrides ?? Array.Empty<STIGForge.Core.Models.PowerStigOverride>());

    return list;
  }

  private static string? ResolveApplyScript(string bundleRoot, string? overridePath)
  {
    if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
    var candidate = Path.Combine(bundleRoot, "Apply", "RunApply.ps1");
    return File.Exists(candidate) ? candidate : null;
  }

  private static string? BuildApplyArgs(string bundleRoot, OrchestrateRequest request)
  {
    var args = new List<string>
    {
      "-BundleRoot",
      Quote(bundleRoot)
    };

    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath))
    {
      args.Add("-ModulesPath");
      args.Add(Quote(Path.Combine(bundleRoot, "Apply", "Modules")));
    }

    var preflight = Path.Combine(bundleRoot, "Apply", "Preflight", "Preflight.ps1");
    if (File.Exists(preflight))
    {
      args.Add("-PreflightScript");
      args.Add(Quote(preflight));
    }

    if (!string.IsNullOrWhiteSpace(request.DscMofPath))
    {
      args.Add("-DscMofPath");
      args.Add(Quote(request.DscMofPath!));
    }

    if (request.DscVerbose)
      args.Add("-VerboseDsc");

    if (request.PowerStigVerbose)
      args.Add("-VerboseDsc");

    if (request.SkipSnapshot)
      args.Add("-SkipSnapshot");

    return string.Join(" ", args);
  }

  private static string Quote(string value)
  {
    return "\"" + value + "\"";
  }

  private static void ValidateBreakGlass(bool breakGlassAcknowledged, string? breakGlassReason, string action)
  {
    if (!breakGlassAcknowledged)
      throw new ArgumentException($"{action} is high risk and requires explicit break-glass acknowledgment.");

    new ManualAnswerService().ValidateBreakGlassReason(breakGlassReason);
  }

  private async Task RecordBreakGlassAsync(string target, string bypassName, string? reason, CancellationToken ct)
  {
    if (_audit == null)
      return;

    await _audit.RecordAsync(new AuditEntry
    {
      Action = "break-glass",
      Target = target,
      Result = "acknowledged",
      Detail = $"Action=orchestrate; Bypass={bypassName}; Reason={reason?.Trim()}",
      User = Environment.UserName,
      Machine = Environment.MachineName,
      Timestamp = DateTimeOffset.Now
    }, ct).ConfigureAwait(false);
  }

  private static void WritePhaseMarker(string path, string logPath)
  {
    File.WriteAllText(path, "Completed: " + BuildTime.Now.ToString("o") + Environment.NewLine + logPath);
  }

  /// <summary>
  /// DTO for overlay decision JSON deserialization.
  /// </summary>
  private sealed class OverlayDecisionDto
  {
    public string key { get; init; } = string.Empty;
    public string overlayId { get; init; } = string.Empty;
    public string overlayName { get; init; } = string.Empty;
    public int overlayOrder { get; init; }
    public int overrideOrder { get; init; }
    public OverlayDecisionOutcomeDto? outcome { get; init; }
  }

  /// <summary>
  /// DTO for overlay decision outcome JSON deserialization.
  /// </summary>
  private sealed class OverlayDecisionOutcomeDto
  {
    public string? statusOverride { get; init; }
    public string? naReason { get; init; }
    public string? notes { get; init; }
  }
}
