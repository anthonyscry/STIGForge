using System.Diagnostics;
using STIGForge.Apply.Dsc;
using STIGForge.Core.Models;

namespace STIGForge.Apply.Steps;

internal sealed class PowerStigStepHandler
{
  public async Task<ApplyStepOutcome> RunCompileAsync(
    string modulePath,
    string? dataFile,
    string outputPath,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    bool verbose,
    string stepName,
    CancellationToken ct,
    PowerStigTarget? target = null,
    string? orgSettingsPath = null)
  {
    if (!Directory.Exists(modulePath) && !File.Exists(modulePath))
      throw new FileNotFoundException("PowerSTIG module path not found: " + modulePath, modulePath);

    Directory.CreateDirectory(outputPath);

    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".err.log");
    var v = verbose ? " -Verbose" : string.Empty;

    var extraModulePaths = new List<string>();
    var bundledModulesDir = Path.Combine(AppContext.BaseDirectory, "tools", "PSModules");
    if (Directory.Exists(bundledModulesDir))
      extraModulePaths.Add(bundledModulesDir);

    string? moduleParent;
    if (File.Exists(modulePath))
    {
      var versionDir = Path.GetDirectoryName(modulePath);
      var moduleDir = versionDir != null ? Path.GetDirectoryName(versionDir) : null;
      moduleParent = moduleDir != null ? Path.GetDirectoryName(moduleDir) : null;
    }
    else
    {
      moduleParent = Path.GetDirectoryName(modulePath);
    }

    if (!string.IsNullOrWhiteSpace(moduleParent))
      extraModulePaths.Add(moduleParent!);

    var bundleApplyDir = Path.Combine(bundleRoot, "Apply");
    if (Directory.Exists(bundleApplyDir) && !extraModulePaths.Contains(bundleApplyDir, StringComparer.OrdinalIgnoreCase))
      extraModulePaths.Add(bundleApplyDir);

    var bundledPowerStig = Path.Combine(bundledModulesDir, "PowerSTIG");
    var effectiveModulePath = Directory.Exists(bundledPowerStig) ? bundledPowerStig : modulePath;

    string? powerStigVersion = null;
    if (Directory.Exists(effectiveModulePath))
    {
      foreach (var subDir in Directory.GetDirectories(effectiveModulePath))
      {
        var dirName = Path.GetFileName(subDir);
        if (!Version.TryParse(dirName, out _))
          continue;

        powerStigVersion = dirName;
        break;
      }
    }

    string command;
    if (target != null)
    {
      var configScript = PowerStigTechnologyMap.BuildDscConfigurationScript(target, outputPath, dataFile, powerStigVersion, orgSettingsPath);
      command = "Import-Module " + ApplyProcessHelpers.ToPowerShellSingleQuoted(effectiveModulePath) + "; " + configScript + v + ";";
    }
    else
    {
      var dataArg = string.IsNullOrWhiteSpace(dataFile) ? string.Empty : " -StigDataFile " + ApplyProcessHelpers.ToPowerShellSingleQuoted(dataFile);
      command =
        "Import-Module " + ApplyProcessHelpers.ToPowerShellSingleQuoted(effectiveModulePath) + "; " +
        "$ErrorActionPreference='Stop'; " +
        "New-StigDscConfiguration" + dataArg + " -OutputPath " + ApplyProcessHelpers.ToPowerShellSingleQuoted(outputPath) + v + ";";
    }

    var scriptPath = Path.Combine(logsDir, "powerstig_compile_" + stepId + ".ps1");
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

    if (extraModulePaths.Count > 0)
    {
      var existingPsModulePath = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
      psi.Environment["PSModulePath"] = string.Join(";", extraModulePaths) + ";" + existingPsModulePath;
    }

    psi.Environment["STIGFORGE_BUNDLE_ROOT"] = bundleRoot;
    psi.Environment["STIGFORGE_APPLY_LOG_DIR"] = logsDir;
    psi.Environment["STIGFORGE_SNAPSHOT_DIR"] = snapshotsDir;
    psi.Environment["STIGFORGE_HARDENING_MODE"] = mode.ToString();
    ApplyProcessHelpers.InjectTraceContext(psi);

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start PowerSTIG compile.");

    var outputTask = process.StandardOutput.ReadToEndAsync(ct);
    var errorTask = process.StandardError.ReadToEndAsync(ct);
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(300_000))
    {
      process.Kill();
      throw new TimeoutException("PowerSTIG compilation did not complete within 5 minutes.");
    }

    File.WriteAllText(stdout, await outputTask.ConfigureAwait(false));
    File.WriteAllText(stderr, await errorTask.ConfigureAwait(false));

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = process.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }
}
