using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class EvaluateStigRunner
{
  public VerifyRunResult Run(string toolRoot, string arguments, string? workingDirectory)
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
