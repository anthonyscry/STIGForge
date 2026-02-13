using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class DscScanRunner
{
  private const int DefaultTimeoutMs = 120_000;

  public VerifyRunResult Run(string mofPath, bool verbose, string outputDir)
  {
    if (string.IsNullOrWhiteSpace(mofPath))
      throw new ArgumentException("MOF path is required for DSC scan.", nameof(mofPath));

    if (!Directory.Exists(mofPath))
      throw new DirectoryNotFoundException("MOF directory not found: " + mofPath);

    Directory.CreateDirectory(outputDir);

    var jsonOutputPath = Path.Combine(outputDir, "dsc-scan.dsc-test.json");

    var script = BuildScript(mofPath, jsonOutputPath, verbose);

    var psi = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
      WorkingDirectory = outputDir,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start PowerShell process for DSC scan.");

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(DefaultTimeoutMs))
    {
      process.Kill();
      throw new TimeoutException($"DSC scan did not complete within {DefaultTimeoutMs / 1000} seconds.");
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

  private static string BuildScript(string mofPath, string jsonOutputPath, bool verbose)
  {
    var verboseFlag = verbose ? " -Verbose" : string.Empty;
    var escapedMofPath = mofPath.Replace("'", "''");
    var escapedJsonPath = jsonOutputPath.Replace("'", "''");

    return $"$result = Test-DscConfiguration -Path '{escapedMofPath}' -Detailed{verboseFlag}; " +
           $"$result | ConvertTo-Json -Depth 10 | Out-File -FilePath '{escapedJsonPath}' -Encoding UTF8";
  }
}
