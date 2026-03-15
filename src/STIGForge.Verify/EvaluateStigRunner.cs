using System.Diagnostics;
using STIGForge.Core.Abstractions;

namespace STIGForge.Verify;

public sealed class EvaluateStigRunner
{
  private const int DefaultTimeoutSeconds = 600;
  private readonly IProcessRunner _processRunner;

  public EvaluateStigRunner(IProcessRunner processRunner)
  {
    _processRunner = processRunner;
  }

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
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    var effectiveTimeoutSeconds = timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds;

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeoutSeconds));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

    ProcessResult result;
    try
    {
      result = await _processRunner.RunAsync(psi, linkedCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      throw new TimeoutException($"Process did not exit within {effectiveTimeoutSeconds} seconds.");
    }

    return new VerifyRunResult
    {
      ExitCode = result.ExitCode,
      Output = result.StandardOutput,
      Error = result.StandardError,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now
    };
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
