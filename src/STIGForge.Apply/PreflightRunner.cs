using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply;

/// <summary>
/// C# wrapper that invokes Preflight.ps1 and interprets the exit code and JSON output.
/// PowerShell preflight is the single source of truth; this class just invokes and checks.
/// </summary>
public sealed class PreflightRunner
{
  private readonly ILogger<PreflightRunner> _logger;
  private readonly IProcessRunner _processRunner;
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

  public PreflightRunner(ILogger<PreflightRunner> logger, IProcessRunner processRunner)
  {
    _logger = logger;
    _processRunner = processRunner;
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

    // Use EncodedCommand with single-quoted args to prevent injection (consistent with rest of codebase)
    var qScript = Steps.ApplyProcessHelpers.ToPowerShellSingleQuoted(scriptPath);
    var qBundle = Steps.ApplyProcessHelpers.ToPowerShellSingleQuoted(request.BundleRoot);
    var qModules = Steps.ApplyProcessHelpers.ToPowerShellSingleQuoted(modulesPath);
    var command = $"& {qScript} -BundleRoot {qBundle} -ModulesPath {qModules}";

    if (!string.IsNullOrWhiteSpace(request.PowerStigModulePath))
      command += $" -PowerStigModulePath {Steps.ApplyProcessHelpers.ToPowerShellSingleQuoted(request.PowerStigModulePath!)}";

    if (request.CheckLgpoConflict)
      command += " -CheckLgpoConflict";

    if (!string.IsNullOrWhiteSpace(request.BundleManifestPath))
      command += $" -BundleManifestPath {Steps.ApplyProcessHelpers.ToPowerShellSingleQuoted(request.BundleManifestPath!)}";

    var args = Steps.ApplyProcessHelpers.BuildEncodedCommandArgs(command);

    _logger.LogInformation("Running preflight: powershell.exe {Args}", args);

    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = "powershell.exe",
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

      ProcessResult result;
      try
      {
        result = await _processRunner.RunAsync(psi, linkedCts.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
      {
        return new PreflightResult
        {
          Ok = false,
          Issues = new[] { "Preflight script timed out after 60 seconds" },
          Timestamp = DateTimeOffset.UtcNow.ToString("o"),
          ExitCode = -2
        };
      }

      if (!string.IsNullOrWhiteSpace(result.StandardError))
        _logger.LogWarning("Preflight stderr: {Stderr}", result.StandardError.Trim());

      return ParseResult(result.StandardOutput, result.ExitCode);
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
        Issues = exitCode != 0 ? new[] { "Preflight produced no output" } : [],
        Timestamp = DateTimeOffset.UtcNow.ToString("o"),
        ExitCode = exitCode
      };
    }

    try
    {
      var options = JsonOptions.CaseInsensitive;
      var parsed = JsonSerializer.Deserialize<PreflightResult>(stdout.Trim(), options);
      if (parsed != null)
      {
        // Override ExitCode with actual process exit code if they differ
        parsed.ExitCode = exitCode;
        return parsed;
      }
    }
    catch (Exception)
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
