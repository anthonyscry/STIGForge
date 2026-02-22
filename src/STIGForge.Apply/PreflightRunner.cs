using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace STIGForge.Apply;

/// <summary>
/// C# wrapper that invokes Preflight.ps1 and interprets the exit code and JSON output.
/// PowerShell preflight is the single source of truth; this class just invokes and checks.
/// </summary>
public sealed class PreflightRunner
{
  private readonly ILogger<PreflightRunner> _logger;
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

  public PreflightRunner(ILogger<PreflightRunner> logger)
  {
    _logger = logger;
  }

  public async Task<PreflightResult> RunPreflightAsync(PreflightRequest request, CancellationToken ct)
  {
    var scriptPath = Path.Combine(request.BundleRoot, "Apply", "Preflight", "Preflight.ps1");
    if (!File.Exists(scriptPath))
    {
      _logger.LogWarning("Preflight script not found at {Path}", scriptPath);
      return new PreflightResult
      {
        Ok = false,
        Issues = new[] { $"Preflight script not found at {scriptPath}" },
        Timestamp = DateTimeOffset.UtcNow.ToString("o"),
        ExitCode = -1
      };
    }

    var modulesPath = request.ModulesPath ?? Path.Combine(request.BundleRoot, "Apply", "Modules");

    var args = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -BundleRoot \"{request.BundleRoot}\" -ModulesPath \"{modulesPath}\"";

    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath))
      args += $" -PowerStigModulePath \"{request.PowerStigModulePath}\"";

    if (request.CheckLgpoConflict)
      args += " -CheckLgpoConflict";

    if (!string.IsNullOrWhiteSpace(request.BundleManifestPath))
      args += $" -BundleManifestPath \"{request.BundleManifestPath}\"";

    _logger.LogInformation("Running preflight: powershell.exe {Args}", args);

    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = "powershell.exe",
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = new Process { StartInfo = psi };
      process.Start();

      var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
      var stderrTask = process.StandardError.ReadToEndAsync(ct);

      var completed = process.WaitForExit((int)DefaultTimeout.TotalMilliseconds);
      if (!completed)
      {
        try { process.Kill(); } catch { /* best effort */ }
        return new PreflightResult
        {
          Ok = false,
          Issues = new[] { "Preflight script timed out after 60 seconds" },
          Timestamp = DateTimeOffset.UtcNow.ToString("o"),
          ExitCode = -2
        };
      }

      var stdout = await stdoutTask.ConfigureAwait(false);
      var stderr = await stderrTask.ConfigureAwait(false);

      if (!string.IsNullOrWhiteSpace(stderr))
        _logger.LogWarning("Preflight stderr: {Stderr}", stderr.Trim());

      return ParseResult(stdout, process.ExitCode);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to execute preflight script");
      return new PreflightResult
      {
        Ok = false,
        Issues = new[] { $"Failed to execute preflight: {ex.Message}" },
        Timestamp = DateTimeOffset.UtcNow.ToString("o"),
        ExitCode = -3
      };
    }
  }

  internal static PreflightResult ParseResult(string stdout, int exitCode)
  {
    if (string.IsNullOrWhiteSpace(stdout))
    {
      return new PreflightResult
      {
        Ok = exitCode == 0,
        Issues = exitCode != 0 ? new[] { "Preflight produced no output" } : Array.Empty<string>(),
        Timestamp = DateTimeOffset.UtcNow.ToString("o"),
        ExitCode = exitCode
      };
    }

    try
    {
      var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
      var parsed = JsonSerializer.Deserialize<PreflightResult>(stdout.Trim(), options);
      if (parsed != null)
      {
        // Override ExitCode with actual process exit code if they differ
        parsed.ExitCode = exitCode;
        return parsed;
      }
    }
    catch
    {
      // JSON parse failed — fall through to raw output handling
    }

    return new PreflightResult
    {
      Ok = exitCode == 0,
      Issues = new[] { $"Preflight output (exit {exitCode}): {stdout.Trim()}" },
      Timestamp = DateTimeOffset.UtcNow.ToString("o"),
      ExitCode = exitCode
    };
  }
}
