using STIGForge.Apply;
using STIGForge.Apply.PowerStig;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
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
    return await _builder.BuildAsync(request, ct);
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

    var psd1Path = request.PowerStigDataFile;
    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath) && string.IsNullOrWhiteSpace(psd1Path))
    {
      var generated = Path.Combine(root, "Apply", "PowerStigData", "stigdata.psd1");
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
      DscMofPath = request.DscMofPath,
      DscVerbose = request.DscVerbose,
      PowerStigModulePath = request.PowerStigModulePath,
      PowerStigDataFile = psd1Path,
      PowerStigOutputPath = request.PowerStigOutputPath,
      PowerStigVerbose = request.PowerStigVerbose,
      PowerStigDataGeneratedPath = psd1Path
    }, ct);

    WritePhaseMarker(Path.Combine(root, "Apply", "apply.complete"), applyResult.LogPath);

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
          Arguments = request.EvaluateStigArgs ?? string.Empty,
          WorkingDirectory = request.EvaluateStigRoot
        }
      }, ct);

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
      var scapOutput = Path.Combine(verifyRoot, "SCAP");
      var toolName = string.IsNullOrWhiteSpace(request.ScapToolLabel) ? "SCAP" : request.ScapToolLabel!;
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
      }, ct);

      var scapRun = scapWorkflow.ToolRuns.FirstOrDefault(r => string.Equals(r.Tool, toolName, StringComparison.OrdinalIgnoreCase) || r.Tool.IndexOf("SCAP", StringComparison.OrdinalIgnoreCase) >= 0);
      if (scapRun == null || !scapRun.Executed)
        throw new InvalidOperationException(toolName + " execution did not run successfully.");

      coverageInputs.Add(new VerificationCoverageInput
      {
        ToolLabel = toolName,
        ReportPath = scapWorkflow.ConsolidatedJsonPath
      });
    }

    if (coverageInputs.Count > 0)
      _artifactAggregation.WriteCoverageArtifacts(Path.Combine(root, "Reports"), coverageInputs);

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
      catch { /* audit failure should not block orchestration */ }
    }
  }

  private static IReadOnlyList<STIGForge.Core.Models.ControlRecord> LoadBundleControls(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath)) return Array.Empty<STIGForge.Core.Models.ControlRecord>();

    var packDir = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(packDir)) return Array.Empty<STIGForge.Core.Models.ControlRecord>();

    var json = File.ReadAllText(packDir);
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

    return string.Join(" ", args);
  }

  private static string Quote(string value)
  {
    return "\"" + value + "\"";
  }

  private static void WritePhaseMarker(string path, string logPath)
  {
    File.WriteAllText(path, "Completed: " + BuildTime.Now.ToString("o") + Environment.NewLine + logPath);
  }
}
