using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace STIGForge.Apply.Lgpo;

/// <summary>
/// Wraps LGPO.exe for Local Group Policy Object import and export operations.
/// LGPO is the secondary apply backend after DSC, before script fallback.
/// </summary>
public sealed class LgpoRunner
{
  private readonly ILogger<LgpoRunner> _logger;
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

  public LgpoRunner(ILogger<LgpoRunner> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Applies a .pol file via LGPO.exe for the specified scope (Machine or User).
  /// </summary>
  public async Task<LgpoApplyResult> ApplyPolicyAsync(LgpoApplyRequest request, CancellationToken ct)
  {
    var lgpoPath = ResolveLgpoPath(request.LgpoExePath);
    if (!File.Exists(lgpoPath))
      throw new FileNotFoundException($"LGPO.exe not found at {lgpoPath}", lgpoPath);

    if (!File.Exists(request.PolFilePath))
      throw new FileNotFoundException($"Policy file not found at {request.PolFilePath}", request.PolFilePath);

    var scopeArg = request.Scope == LgpoScope.Machine ? "/m" : "/u";
    var args = $"{scopeArg} \"{request.PolFilePath}\"";

    _logger.LogInformation("Applying LGPO policy: {LgpoPath} {Args}", lgpoPath, args);

    var started = DateTimeOffset.UtcNow;
    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = lgpoPath,
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
        return new LgpoApplyResult
        {
          Success = false,
          ExitCode = -2,
          StdOut = "LGPO.exe timed out after 60 seconds",
          StdErr = string.Empty,
          StartedAt = started,
          FinishedAt = DateTimeOffset.UtcNow
        };
      }

      var stdout = await stdoutTask.ConfigureAwait(false);
      var stderr = await stderrTask.ConfigureAwait(false);

      return new LgpoApplyResult
      {
        Success = process.ExitCode == 0,
        ExitCode = process.ExitCode,
        StdOut = stdout,
        StdErr = stderr,
        StartedAt = started,
        FinishedAt = DateTimeOffset.UtcNow
      };
    }
    catch (Exception ex) when (ex is not FileNotFoundException)
    {
      _logger.LogError(ex, "Failed to execute LGPO.exe");
      return new LgpoApplyResult
      {
        Success = false,
        ExitCode = -3,
        StdOut = string.Empty,
        StdErr = ex.Message,
        StartedAt = started,
        FinishedAt = DateTimeOffset.UtcNow
      };
    }
  }

  /// <summary>
  /// Exports current policy to a text file for audit/comparison.
  /// </summary>
  public async Task<string> ExportPolicyAsync(LgpoScope scope, string outputDir, CancellationToken ct)
  {
    var lgpoPath = ResolveLgpoPath(null);
    if (!File.Exists(lgpoPath))
      throw new FileNotFoundException($"LGPO.exe not found at {lgpoPath}", lgpoPath);

    Directory.CreateDirectory(outputDir);
    var outputFile = Path.Combine(outputDir, $"lgpo_export_{scope.ToString().ToLowerInvariant()}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.txt");

    var scopeArg = scope == LgpoScope.Machine ? "/parse /m" : "/parse /u";
    var args = $"{scopeArg}";

    _logger.LogInformation("Exporting LGPO policy: {LgpoPath} {Args}", lgpoPath, args);

    var psi = new ProcessStartInfo
    {
      FileName = lgpoPath,
      Arguments = args,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = new Process { StartInfo = psi };
    process.Start();

    var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
    process.WaitForExit((int)DefaultTimeout.TotalMilliseconds);

    File.WriteAllText(outputFile, stdout);
    _logger.LogInformation("Policy exported to {OutputFile}", outputFile);

    return outputFile;
  }

  private static string ResolveLgpoPath(string? overridePath)
  {
    if (!string.IsNullOrWhiteSpace(overridePath))
      return overridePath!;

    // Default: look for LGPO.exe in tools/ relative to current directory
    var toolsPath = Path.Combine(Environment.CurrentDirectory, "tools", "LGPO.exe");
    if (File.Exists(toolsPath))
      return toolsPath;

    // Fallback: assume it's on PATH
    return "LGPO.exe";
  }
}
