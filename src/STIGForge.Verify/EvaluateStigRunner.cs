using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class EvaluateStigRunner
{
  public VerifyRunResult Run(string toolRoot, string arguments, string? workingDirectory)
  {
    var scriptPath = Path.Combine(toolRoot, "Evaluate-STIG.ps1");
    if (!File.Exists(scriptPath))
      throw new FileNotFoundException("Evaluate-STIG.ps1 not found", scriptPath);

    var args = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
    if (!string.IsNullOrWhiteSpace(arguments))
      args += " " + arguments;

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = args,
      WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? toolRoot : workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start Evaluate-STIG process.");

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    return new VerifyRunResult
    {
      ExitCode = process.ExitCode,
      Output = output,
      Error = error,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now
    };
  }
}
