using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class ScapRunner
{
  public VerifyRunResult Run(string commandPath, string arguments, string? workingDirectory)
  {
    if (string.IsNullOrWhiteSpace(commandPath))
      throw new ArgumentException("Command path is required.");

    if (!File.Exists(commandPath))
      throw new FileNotFoundException("SCAP command not found", commandPath);

    var psi = new ProcessStartInfo
    {
      FileName = commandPath,
      Arguments = arguments ?? string.Empty,
      WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start SCAP command.");

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
