using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Steps;

internal sealed class DscStepHandler
{
  private readonly IProcessRunner _processRunner;

  public DscStepHandler(IProcessRunner processRunner)
  {
    _processRunner = processRunner;
  }

  public async Task<ApplyStepOutcome> RunAsync(
    string mofPath,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    bool verbose,
    string stepName,
    CancellationToken ct)
  {
    if (!Directory.Exists(mofPath))
      throw new DirectoryNotFoundException("DSC path not found: " + mofPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_dsc_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_dsc_" + stepId + ".err.log");

    var whatIf = mode == HardeningMode.AuditOnly ? " -WhatIf" : string.Empty;
    var v = verbose ? " -Verbose" : string.Empty;

    var copyModulesBlock = new StringBuilder();
    var bundledModulesDir = Path.Combine(AppContext.BaseDirectory, "tools", "PSModules");
    var bundleApplyDir = Path.Combine(bundleRoot, "Apply");

    if (Directory.Exists(bundledModulesDir))
    {
      foreach (var modDir in Directory.GetDirectories(bundledModulesDir))
      {
        var modName = Path.GetFileName(modDir);
        copyModulesBlock.AppendLine(
          "Copy-Item -Path " + ApplyProcessHelpers.ToPowerShellSingleQuoted(modDir) + " -Destination \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\" + modName + "\" -Recurse -Force; " +
          "Get-ChildItem \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\" + modName + "\" -Recurse -File | Unblock-File;");
      }
    }

    var command = copyModulesBlock + "Start-DscConfiguration -Path " + ApplyProcessHelpers.ToPowerShellSingleQuoted(mofPath) + " -Wait -Force" + whatIf + v;
    var scriptPath = Path.Combine(logsDir, "apply_dsc_" + stepId + ".ps1");
    File.WriteAllText(scriptPath, command);

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = ApplyProcessHelpers.BuildEncodedCommandArgs(command),
      WorkingDirectory = bundleRoot,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var extraModulePaths = new List<string>();
    if (Directory.Exists(bundledModulesDir))
      extraModulePaths.Add(bundledModulesDir);
    if (Directory.Exists(bundleApplyDir))
      extraModulePaths.Add(bundleApplyDir);
    if (extraModulePaths.Count > 0)
    {
      var existing = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
      psi.Environment["PSModulePath"] = string.Join(";", extraModulePaths) + ";" + existing;
    }

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();

    ApplyProcessHelpers.InjectTraceContext(psi);

    var started = DateTimeOffset.Now;

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

    ProcessResult result;
    try
    {
      result = await _processRunner.RunAsync(psi, linkedCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException("DSC apply did not complete within 10 minutes.");
    }

    File.WriteAllText(stdout, result.StandardOutput);
    File.WriteAllText(stderr, result.StandardError);

    var exitCode = result.ExitCode;
    // Only suppress non-zero exit when stderr contains ONLY the benign "Applying configuration" progress message
    // and no actual error indicators. Previously this used a loose substring match that masked real failures.
    if (exitCode != 0
        && result.StandardError.Contains("Applying configuration", StringComparison.OrdinalIgnoreCase)
        && !result.StandardError.Contains("error", StringComparison.OrdinalIgnoreCase)
        && !result.StandardError.Contains("exception", StringComparison.OrdinalIgnoreCase)
        && !result.StandardError.Contains("failed", StringComparison.OrdinalIgnoreCase))
      exitCode = 0;

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = exitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }
}
