using System.Diagnostics;
using System.Text;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Telemetry;

namespace STIGForge.Apply.Steps;

internal sealed class ScriptStepHandler
{
  public async Task<ApplyStepOutcome> RunAsync(
    string scriptPath,
    string? args,
    string bundleRoot,
    string logsDir,
    string snapshotsDir,
    HardeningMode mode,
    string stepName,
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

    ApplyProcessHelpers.InjectTraceContext(psi);

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start apply script.");

    var outputTask = process.StandardOutput.ReadToEndAsync(ct);
    var errorTask = process.StandardError.ReadToEndAsync(ct);
    await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
    if (!process.WaitForExit(30_000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
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

internal static class ApplyProcessHelpers
{
  public static string BuildEncodedCommandArgs(string command)
  {
    var bytes = Encoding.Unicode.GetBytes(command);
    var encoded = Convert.ToBase64String(bytes);
    return "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
  }

  public static void InjectTraceContext(ProcessStartInfo psi)
  {
    var context = TraceContext.GetCurrentContext();
    if (context == null)
      return;

    psi.Environment["STIGFORGE_TRACE_ID"] = context.TraceId;
    psi.Environment["STIGFORGE_PARENT_SPAN_ID"] = context.SpanId;
    psi.Environment["STIGFORGE_TRACE_FLAGS"] = context.TraceFlags;
  }
}
