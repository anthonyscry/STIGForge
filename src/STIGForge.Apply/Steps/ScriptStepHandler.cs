using System.Diagnostics;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Telemetry;

namespace STIGForge.Apply.Steps;

internal sealed class ScriptStepHandler
{
  private readonly IProcessRunner _processRunner;

  public ScriptStepHandler(IProcessRunner processRunner)
  {
    _processRunner = processRunner;
  }

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

    var command = "& " + ApplyProcessHelpers.ToPowerShellSingleQuoted(scriptPath);
    if (!string.IsNullOrWhiteSpace(args))
    {
      // Split and individually quote each argument to prevent injection via metacharacters
      var argParts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      command += " " + string.Join(" ", argParts.Select(ApplyProcessHelpers.ToPowerShellSingleQuoted));
    }

    var arguments = ApplyProcessHelpers.BuildEncodedCommandArgs(command);

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

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

    ProcessResult result;
    try
    {
      result = await _processRunner.RunAsync(psi, linkedCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException("Process did not exit within 5 minutes.");
    }

    File.WriteAllText(stdout, result.StandardOutput);
    File.WriteAllText(stderr, result.StandardError);

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = result.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }
}

internal static class ApplyProcessHelpers
{
  public static string ToPowerShellSingleQuoted(string? value)
    => STIGForge.Core.PowerShellHelpers.SingleQuote(value);

  public static string BuildEncodedCommandArgs(string command)
    => STIGForge.Core.PowerShellHelpers.BuildEncodedCommandArgs(command);

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
