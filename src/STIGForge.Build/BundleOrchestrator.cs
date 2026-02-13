using STIGForge.Apply;
using STIGForge.Apply.PowerStig;
using STIGForge.Core.Abstractions;
using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using PackTypes = STIGForge.Core.Constants.PackTypes;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Verify;

namespace STIGForge.Build;

public sealed class BundleOrchestrator
{
  private readonly BundleBuilder _builder;
  private readonly ApplyRunner _apply;
  private readonly IVerificationWorkflowService _verificationWorkflow;
  private readonly VerificationArtifactAggregationService _artifactAggregation;
  private readonly IAuditTrailService? _audit;

  public BundleOrchestrator(BundleBuilder builder, ApplyRunner apply, IVerificationWorkflowService verificationWorkflow, VerificationArtifactAggregationService artifactAggregation, IAuditTrailService? audit = null)
  {
    _builder = builder;
    _apply = apply;
    _verificationWorkflow = verificationWorkflow;
    _artifactAggregation = artifactAggregation;
    _audit = audit;
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
      ? Path.Combine(root, BundlePaths.VerifyDirectory)
      : request.VerifyOutputRoot!;

    Directory.CreateDirectory(verifyRoot);
    Directory.CreateDirectory(Path.Combine(root, BundlePaths.ReportsDirectory));

    if (request.SkipSnapshot)
    {
      ValidateBreakGlass(request.BreakGlassAcknowledged, request.BreakGlassReason, "orchestrate --skip-snapshot");
      await RecordBreakGlassAsync(root, "skip-snapshot", request.BreakGlassReason, ct).ConfigureAwait(false);
    }

    var psd1Path = request.PowerStigDataFile;
    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath) && string.IsNullOrWhiteSpace(psd1Path))
    {
      var generated = Path.Combine(root, BundlePaths.ApplyDirectory, "PowerStigData", "stigdata.psd1");
      Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
      var data = PowerStigDataGenerator.CreateDefault(request.PowerStigModulePath!, request.BundleRoot);
      var controls = LoadBundleControls(root);
      var overrides = LoadBundlePowerStigOverrides(root);
      if (controls.Count > 0 || overrides.Count > 0)
        data = PowerStigDataGenerator.CreateFromControls(controls, overrides);
      PowerStigDataWriter.Write(generated, data);
      psd1Path = generated;
    }

    var applyResult = await _apply.RunAsync(new ApplyRequest
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
      PowerStigDataGeneratedPath = psd1Path
    }, ct).ConfigureAwait(false);

    WritePhaseMarker(Path.Combine(root, BundlePaths.ApplyDirectory, "apply.complete"), applyResult.LogPath);

    var coverageInputs = new List<VerificationCoverageInput>();

    if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
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
          Arguments = request.EvaluateStigArgs ?? string.Empty
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
    }

    if (!string.IsNullOrWhiteSpace(request.ScapCommandPath))
    {
      var scapOutput = Path.Combine(verifyRoot, PackTypes.Scap);
      var toolName = string.IsNullOrWhiteSpace(request.ScapToolLabel) ? PackTypes.Scap : request.ScapToolLabel!;
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

      var scapRun = scapWorkflow.ToolRuns.FirstOrDefault(r => string.Equals(r.Tool, toolName, StringComparison.OrdinalIgnoreCase) || r.Tool.IndexOf(PackTypes.Scap, StringComparison.OrdinalIgnoreCase) >= 0);
      if (scapRun == null || !scapRun.Executed)
        throw new InvalidOperationException(toolName + " execution did not run successfully.");

      coverageInputs.Add(new VerificationCoverageInput
      {
        ToolLabel = toolName,
        ReportPath = scapWorkflow.ConsolidatedJsonPath
      });
    }

    var dscScanEnabled = request.DscScanEnabled;
    if (!dscScanEnabled && !string.IsNullOrWhiteSpace(ResolveDscMofPath(request)))
      dscScanEnabled = true;

    if (dscScanEnabled)
    {
      var dscMofPath = ResolveDscMofPath(request);
      if (!string.IsNullOrWhiteSpace(dscMofPath))
      {
        var dscOutput = Path.Combine(verifyRoot, "DSC-Scan");
        var dscToolLabel = string.IsNullOrWhiteSpace(request.DscScanToolLabel) ? "PowerSTIG-DSC" : request.DscScanToolLabel!;
        var dscWorkflow = await _verificationWorkflow.RunAsync(new VerificationWorkflowRequest
        {
          OutputRoot = dscOutput,
          ConsolidatedToolLabel = dscToolLabel,
          DscScan = new DscScanWorkflowOptions
          {
            Enabled = true,
            MofPath = dscMofPath!,
            Verbose = request.DscScanVerbose,
            ToolLabel = dscToolLabel
          }
        }, ct).ConfigureAwait(false);

        coverageInputs.Add(new VerificationCoverageInput
        {
          ToolLabel = dscToolLabel,
          ReportPath = dscWorkflow.ConsolidatedJsonPath
        });
      }
    }

    if (coverageInputs.Count > 0)
      _artifactAggregation.WriteCoverageArtifacts(Path.Combine(root, BundlePaths.ReportsDirectory), coverageInputs);

    if (_audit != null)
    {
      try
      {
        await _audit.RecordAsync(new AuditEntry
        {
          Action = "orchestrate",
          Target = root,
          Result = "success",
          Detail = $"CoverageInputs={coverageInputs.Count}",
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
  }

  private static IReadOnlyList<STIGForge.Core.Models.ControlRecord> LoadBundleControls(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
    if (!File.Exists(manifestPath)) return Array.Empty<STIGForge.Core.Models.ControlRecord>();

    return PackControlsReader.Load(bundleRoot);
  }

  private static IReadOnlyList<STIGForge.Core.Models.PowerStigOverride> LoadBundlePowerStigOverrides(string bundleRoot)
  {
    var overlaysPath = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, "overlays.json");
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
    var candidate = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "RunApply.ps1");
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
      args.Add(Quote(Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "Modules")));
    }

    var preflight = Path.Combine(bundleRoot, BundlePaths.ApplyDirectory, "Preflight", "Preflight.ps1");
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

  private static string? ResolveDscMofPath(OrchestrateRequest request)
  {
    if (!string.IsNullOrWhiteSpace(request.DscMofPath) && Directory.Exists(request.DscMofPath))
      return request.DscMofPath;

    if (!string.IsNullOrWhiteSpace(request.PowerStigOutputPath) && Directory.Exists(request.PowerStigOutputPath))
      return request.PowerStigOutputPath;

    var defaultDscDir = Path.Combine(request.BundleRoot, BundlePaths.ApplyDirectory, "Dsc");
    if (Directory.Exists(defaultDscDir) && Directory.GetFiles(defaultDscDir, "*.mof", SearchOption.AllDirectories).Length > 0)
      return defaultDscDir;

    return null;
  }

  private static void WritePhaseMarker(string path, string logPath)
  {
    File.WriteAllText(path, "Completed: " + BuildTime.Now.ToString("o") + Environment.NewLine + logPath);
  }
}
