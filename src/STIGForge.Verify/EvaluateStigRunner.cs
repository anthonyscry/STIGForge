using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class EvaluateStigRunner
{
  private const int DefaultTimeoutSeconds = 30;

  public async Task<VerifyRunResult> RunAsync(string toolRoot, string arguments, string? workingDirectory, int timeoutSeconds = DefaultTimeoutSeconds, CancellationToken ct = default)
  {
    var scriptPath = ResolveScriptPath(toolRoot);
    var resolvedToolRoot = Path.GetDirectoryName(scriptPath) ?? toolRoot;

    var args = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
    if (!string.IsNullOrWhiteSpace(arguments))
      args += " " + arguments;

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = args,
      WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? resolvedToolRoot : workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start Evaluate-STIG process.");

    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();

    var effectiveTimeoutSeconds = timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds;
    using var timeoutCts = new CancellationTokenSource(checked(effectiveTimeoutSeconds * 1000));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

    try
    {
      await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      TryKill(process);
      throw new TimeoutException($"Process did not exit within {effectiveTimeoutSeconds} seconds.");
    }

    var output = await outputTask.ConfigureAwait(false);
    var error = await errorTask.ConfigureAwait(false);

    return new VerifyRunResult
    {
      ExitCode = process.ExitCode,
      Output = output,
      Error = error,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now
    };
  }

  private static void TryKill(Process process)
  {
    try
    {
      if (!process.HasExited)
        process.Kill();
    }
    catch
    {
    }
  }

  private static string ResolveScriptPath(string toolRoot)
  {
    if (string.IsNullOrWhiteSpace(toolRoot))
      throw new ArgumentException("Tool root is required.", nameof(toolRoot));

    if (File.Exists(toolRoot))
    {
      if (string.Equals(Path.GetFileName(toolRoot), "Evaluate-STIG.ps1", StringComparison.OrdinalIgnoreCase))
        return Path.GetFullPath(toolRoot);

      throw new FileNotFoundException("Evaluate-STIG.ps1 not found", toolRoot);
    }

    var directScriptPath = Path.Combine(toolRoot, "Evaluate-STIG.ps1");
    if (File.Exists(directScriptPath))
      return directScriptPath;

    if (!Directory.Exists(toolRoot))
      throw new FileNotFoundException("Evaluate-STIG.ps1 not found", directScriptPath);

    try
    {
      var match = Directory.EnumerateFiles(toolRoot, "Evaluate-STIG.ps1", SearchOption.AllDirectories)
        .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
        .ThenBy(path => path.Length)
        .FirstOrDefault();

      if (!string.IsNullOrWhiteSpace(match))
        return match;
    }
    catch (UnauthorizedAccessException)
    {
    }
    catch (IOException)
    {
    }

    throw new FileNotFoundException("Evaluate-STIG.ps1 not found", directScriptPath);
  }
}
