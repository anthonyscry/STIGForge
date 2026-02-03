 using System.Diagnostics;
 using System.Text.Json;
 using STIGForge.Core.Models;
 using STIGForge.Apply.Snapshot;
 using STIGForge.Apply.Dsc;
 using Microsoft.Extensions.Logging;
 
 namespace STIGForge.Apply;
 
public sealed class ApplyRunner
{
   private readonly ILogger<ApplyRunner> _logger;
   private readonly SnapshotService _snapshotService;
   private readonly RollbackScriptGenerator _rollbackScriptGenerator;
   private readonly LcmService _lcmService;

   public ApplyRunner(
     ILogger<ApplyRunner> logger,
     SnapshotService snapshotService,
     RollbackScriptGenerator rollbackScriptGenerator,
     LcmService lcmService)
   {
      _logger = logger ?? throw new ArgumentNullException(nameof(logger));
      _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
      _rollbackScriptGenerator = rollbackScriptGenerator ?? throw new ArgumentNullException(nameof(rollbackScriptGenerator));
      _lcmService = lcmService ?? throw new ArgumentNullException(nameof(lcmService));
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
          originalLcm = await _lcmService.GetLcmState(ct);

          // Configure LCM for apply
          var lcmConfig = new LcmConfig
          {
            ConfigurationMode = mode == HardeningMode.AuditOnly ? "ApplyOnly" : "ApplyAndMonitor",
            RebootNodeIfNeeded = true,
            ConfigurationModeFrequencyMins = 15,
            AllowModuleOverwrite = true
          };
          _logger.LogInformation("Configuring LCM for DSC application (Mode: {ConfigurationMode})...", lcmConfig.ConfigurationMode);
          await _lcmService.ConfigureLcm(lcmConfig, ct);
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
          snapshot = await _snapshotService.CreateSnapshot(snapshotsDir, ct);
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
        ct);
      steps.Add(outcome);
    }

    if (!string.IsNullOrWhiteSpace(request.ScriptPath))
    {
      var outcome = await RunScriptAsync(request.ScriptPath!, request.ScriptArgs, root, logsDir, snapshotsDir, mode, ct);
      steps.Add(outcome);
    }

   if (!string.IsNullOrWhiteSpace(request.DscMofPath))
     {
       var outcome = await RunDscAsync(request.DscMofPath!, root, logsDir, snapshotsDir, mode, request.DscVerbose, ct);
       steps.Add(outcome);

       // Reset LCM after DSC application (optional)
       if (originalLcm != null && request.ResetLcmAfterApply)
       {
         try
         {
          _logger.LogInformation("Resetting LCM to original state...");
            await _lcmService.ResetLcm(originalLcm, ct);
          _logger.LogInformation("LCM reset successfully.");
         }
         catch (LcmException ex)
         {
          _logger.LogWarning(ex, "Failed to reset LCM to original state. This is non-critical and apply continues.");
         }
       }
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

     return new ApplyResult
     {
       BundleRoot = root,
       Mode = mode,
       LogPath = logPath,
       Steps = steps,
       SnapshotId = snapshot?.SnapshotId ?? string.Empty,
       RollbackScriptPath = snapshot?.RollbackScriptPath ?? string.Empty
     };
   }

   private static HardeningMode? TryReadModeFromManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath)) return null;

    using var stream = File.OpenRead(manifestPath);
    using var doc = JsonDocument.Parse(stream);
    if (!doc.RootElement.TryGetProperty("Profile", out var profile)) return null;

    if (!profile.TryGetProperty("HardeningMode", out var mode)) return null;
    var value = mode.GetString();
    if (string.IsNullOrWhiteSpace(value)) return null;

    if (Enum.TryParse<HardeningMode>(value, true, out var parsed))
      return parsed;

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
    await Task.WhenAll(outputTask, errorTask);
    process.WaitForExit();

    File.WriteAllText(stdout, outputTask.Result);
    File.WriteAllText(stderr, errorTask.Result);

    return new ApplyStepOutcome
    {
      StepName = "apply_script",
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
    await Task.WhenAll(outputTask, errorTask);
    process.WaitForExit();

    File.WriteAllText(stdout, outputTask.Result);
    File.WriteAllText(stderr, errorTask.Result);

    return new ApplyStepOutcome
    {
      StepName = "apply_dsc",
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
    if (!Directory.Exists(modulePath))
      throw new DirectoryNotFoundException("PowerSTIG module path not found: " + modulePath);

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
    await Task.WhenAll(outputTask, errorTask);
    process.WaitForExit();

    File.WriteAllText(stdout, outputTask.Result);
    File.WriteAllText(stderr, errorTask.Result);

    return new ApplyStepOutcome
    {
      StepName = "powerstig_compile",
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }
 
}

