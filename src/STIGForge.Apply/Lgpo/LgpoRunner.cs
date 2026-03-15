using System.Diagnostics;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Lgpo;

/// <summary>
/// Wraps LGPO.exe for Local Group Policy Object import and export operations.
/// LGPO is the secondary apply backend after DSC, before script fallback.
/// </summary>
public sealed class LgpoRunner
{
  private readonly ILogger<LgpoRunner> _logger;
  private readonly IProcessRunner _processRunner;
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

  public LgpoRunner(ILogger<LgpoRunner> logger, IProcessRunner processRunner)
  {
    _logger = logger;
    _processRunner = processRunner;
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
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        return new LgpoApplyResult
        {
          Success = false,
          ExitCode = -2,
          StdOut = $"LGPO.exe timed out after {DefaultTimeout.TotalSeconds} seconds",
          StdErr = string.Empty,
          StartedAt = started,
          FinishedAt = DateTimeOffset.UtcNow
        };
      }

      return new LgpoApplyResult
      {
        Success = result.ExitCode == 0,
        ExitCode = result.ExitCode,
        StdOut = result.StandardOutput,
        StdErr = result.StandardError,
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
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
    var result = await _processRunner.RunAsync(psi, linkedCts.Token).ConfigureAwait(false);

    await File.WriteAllTextAsync(outputFile, result.StandardOutput, ct).ConfigureAwait(false);
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

    // Check tools/ relative to application base directory (bundled deployment)
    var baseDirPath = Path.Combine(AppContext.BaseDirectory, "tools", "LGPO.exe");
    if (File.Exists(baseDirPath))
      return baseDirPath;

    // Fallback: assume it's on PATH
    return "LGPO.exe";
  }
}
