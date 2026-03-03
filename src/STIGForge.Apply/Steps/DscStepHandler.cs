using System.Diagnostics;
using System.Text;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Steps;

internal sealed class DscStepHandler
{
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
          $"Copy-Item -Path '{modDir.Replace("'", "''")}' -Destination \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\{modName}\" -Recurse -Force; " +
          $"Get-ChildItem \"$env:ProgramFiles\\WindowsPowerShell\\Modules\\{modName}\" -Recurse -File | Unblock-File;");
      }
    }

    var command = copyModulesBlock + "Start-DscConfiguration -Path \"" + mofPath + "\" -Wait -Force" + whatIf + v;
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
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start DSC apply.");

    var outputTask = process.StandardOutput.ReadToEndAsync(ct);
    var errorTask = process.StandardError.ReadToEndAsync(ct);
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(600_000))
    {
      process.Kill();
      throw new TimeoutException("DSC apply did not complete within 10 minutes.");
    }

    var stdoutText = await outputTask.ConfigureAwait(false);
    var stderrText = await errorTask.ConfigureAwait(false);
    File.WriteAllText(stdout, stdoutText);
    File.WriteAllText(stderr, stderrText);

    var exitCode = process.ExitCode;
    if (exitCode != 0 && stderrText.Contains("Applying Configuration", StringComparison.Ordinal))
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
