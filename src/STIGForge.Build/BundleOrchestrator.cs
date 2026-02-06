using STIGForge.Apply;
using STIGForge.Apply.PowerStig;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Build;

public sealed class BundleOrchestrator
{
  private readonly BundleBuilder _builder;
  private readonly ApplyRunner _apply;

  public BundleOrchestrator(BundleBuilder builder, ApplyRunner apply)
  {
    _builder = builder;
    _apply = apply;
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

    var coverageInputs = new List<string>();

    if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
    {
      var evalRunner = new EvaluateStigRunner();
      var evalResult = evalRunner.Run(
        request.EvaluateStigRoot!,
        request.EvaluateStigArgs ?? string.Empty,
        request.EvaluateStigRoot);

      var evalOutput = Path.Combine(verifyRoot, "Evaluate-STIG");
      Directory.CreateDirectory(evalOutput);
      var evalReport = VerifyReportWriter.BuildFromCkls(evalOutput, "Evaluate-STIG");
      evalReport.StartedAt = evalResult.StartedAt;
      evalReport.FinishedAt = evalResult.FinishedAt;

      VerifyReportWriter.WriteJson(Path.Combine(evalOutput, "consolidated-results.json"), evalReport);
      VerifyReportWriter.WriteCsv(Path.Combine(evalOutput, "consolidated-results.csv"), evalReport.Results);

      coverageInputs.Add("Evaluate-STIG|" + evalOutput);
    }

    if (!string.IsNullOrWhiteSpace(request.ScapCommandPath))
    {
      var scapRunner = new ScapRunner();
      var scapResult = scapRunner.Run(
        request.ScapCommandPath!,
        request.ScapArgs ?? string.Empty,
        null);

      var scapOutput = Path.Combine(verifyRoot, "SCAP");
      Directory.CreateDirectory(scapOutput);
      var toolName = string.IsNullOrWhiteSpace(request.ScapToolLabel) ? "SCAP" : request.ScapToolLabel!;
      var scapReport = VerifyReportWriter.BuildFromCkls(scapOutput, toolName);
      scapReport.StartedAt = scapResult.StartedAt;
      scapReport.FinishedAt = scapResult.FinishedAt;

      VerifyReportWriter.WriteJson(Path.Combine(scapOutput, "consolidated-results.json"), scapReport);
      VerifyReportWriter.WriteCsv(Path.Combine(scapOutput, "consolidated-results.csv"), scapReport.Results);

      coverageInputs.Add(toolName + "|" + scapOutput);
    }

    if (coverageInputs.Count > 0)
      WriteCoverageOverlap(root, coverageInputs);
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

  private static void WriteCoverageOverlap(string bundleRoot, IReadOnlyList<string> inputs)
  {
    var reportsRoot = Path.Combine(bundleRoot, "Reports");
    var allResults = new List<ControlResult>();

    foreach (var raw in inputs)
    {
      var parts = raw.Split('|');
      var label = parts.Length > 1 ? parts[0] : string.Empty;
      var path = parts.Length > 1 ? parts[1] : parts[0];

      var resolved = ResolveReportPath(path);
      var report = VerifyReportReader.LoadFromJson(resolved);
      if (!string.IsNullOrWhiteSpace(label))
        report.Tool = label;

      foreach (var r in report.Results)
      {
        if (string.IsNullOrWhiteSpace(r.Tool))
          r.Tool = report.Tool;
      }

      allResults.AddRange(report.Results);
    }

    var coverage = VerifyReportWriter.BuildCoverageSummary(allResults);
    VerifyReportWriter.WriteCoverageSummary(
      Path.Combine(reportsRoot, "coverage_by_tool.csv"),
      Path.Combine(reportsRoot, "coverage_by_tool.json"),
      coverage);

    var maps = VerifyReportWriter.BuildControlSourceMap(allResults);
    VerifyReportWriter.WriteControlSourceMap(
      Path.Combine(reportsRoot, "control_sources.csv"),
      maps);

    var overlaps = VerifyReportWriter.BuildOverlapSummary(allResults);
    VerifyReportWriter.WriteOverlapSummary(
      Path.Combine(reportsRoot, "coverage_overlap.csv"),
      Path.Combine(reportsRoot, "coverage_overlap.json"),
      overlaps);
  }

  private static string ResolveReportPath(string path)
  {
    if (File.Exists(path)) return path;
    if (Directory.Exists(path))
    {
      var candidate = Path.Combine(path, "consolidated-results.json");
      if (File.Exists(candidate)) return candidate;
    }

    throw new FileNotFoundException("Report not found: " + path);
  }

  private static void WritePhaseMarker(string path, string logPath)
  {
    File.WriteAllText(path, "Completed: " + BuildTime.Now.ToString("o") + Environment.NewLine + logPath);
  }
}
