using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using STIGForge.Apply.Lgpo;

namespace STIGForge.Apply.Steps;

internal sealed class PolicyStepHandler
{
  private readonly ILogger<ApplyRunner> _logger;
  private readonly LgpoRunner? _lgpoRunner;

  public PolicyStepHandler(ILogger<ApplyRunner> logger, LgpoRunner? lgpoRunner)
  {
    _logger = logger;
    _lgpoRunner = lgpoRunner;
  }

  public bool CanRunLgpo => _lgpoRunner != null;

  public ApplyStepOutcome RunAdmxImport(ApplyRequest request, string bundleRoot, string logsDir, string stepName)
  {
    var started = DateTimeOffset.Now;
    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_admx_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_admx_" + stepId + ".err.log");

    var outBuilder = new StringBuilder(2048);
    var errBuilder = new StringBuilder(1024);
    var exitCode = 0;

    try
    {
      var sourceRoot = request.AdmxTemplateRootPath!;
      if (!Directory.Exists(sourceRoot))
        throw new DirectoryNotFoundException("ADMX template root not found: " + sourceRoot);

      var targetRoot = ResolvePolicyDefinitionsTarget(request, bundleRoot);
      if (string.IsNullOrWhiteSpace(targetRoot))
        throw new InvalidOperationException("Unable to resolve PolicyDefinitions target path for ADMX import.");

      Directory.CreateDirectory(targetRoot);

      var copiedAdmx = 0;
      var copiedAdml = 0;
      var skipped = new List<string>();

      foreach (var admxPath in Directory.EnumerateFiles(sourceRoot, "*.admx", SearchOption.AllDirectories)
                   .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
      {
        if (!IsTemplateApplicableToCurrentOs(admxPath))
        {
          skipped.Add(admxPath);
          continue;
        }

        CopyTemplateFile(sourceRoot, targetRoot, admxPath);
        copiedAdmx++;

        var baseName = Path.GetFileNameWithoutExtension(admxPath);
        foreach (var admlPath in Directory.EnumerateFiles(sourceRoot, baseName + ".adml", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
          CopyTemplateFile(sourceRoot, targetRoot, admlPath);
          copiedAdml++;
        }
      }

      outBuilder.AppendLine("ADMX import complete.");
      outBuilder.AppendLine("Source: " + sourceRoot);
      outBuilder.AppendLine("Target: " + targetRoot);
      outBuilder.AppendLine("Copied .admx: " + copiedAdmx);
      outBuilder.AppendLine("Copied .adml: " + copiedAdml);
      outBuilder.AppendLine("Skipped (non-applicable): " + skipped.Count);

      if (copiedAdmx == 0)
      {
        errBuilder.AppendLine("No applicable ADMX templates were found to import.");
        exitCode = 1;
      }
    }
    catch (Exception ex)
    {
      errBuilder.AppendLine(ex.Message);
      exitCode = 1;
    }

    File.WriteAllText(stdout, outBuilder.ToString());
    File.WriteAllText(stderr, errBuilder.ToString());

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = exitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  public async Task<ApplyStepOutcome> RunLgpoAsync(
    ApplyRequest request,
    string logsDir,
    string stepName,
    CancellationToken ct)
  {
    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_lgpo_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_lgpo_" + stepId + ".err.log");

    var started = DateTimeOffset.Now;
    var lgpoRequest = new LgpoApplyRequest
    {
      PolFilePath = request.LgpoPolFilePath!,
      Scope = request.LgpoScope ?? LgpoScope.Machine,
      LgpoExePath = request.LgpoExePath
    };

    var result = await _lgpoRunner!.ApplyPolicyAsync(lgpoRequest, ct).ConfigureAwait(false);
    await File.WriteAllTextAsync(stdout, result.StdOut, ct).ConfigureAwait(false);
    await File.WriteAllTextAsync(stderr, result.StdErr, ct).ConfigureAwait(false);

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = result.ExitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  public ApplyStepOutcome RunGpoImport(ApplyRequest request, string logsDir, string stepName)
  {
    var started = DateTimeOffset.Now;
    var stepId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
    var stdout = Path.Combine(logsDir, "apply_gpo_import_" + stepId + ".out.log");
    var stderr = Path.Combine(logsDir, "apply_gpo_import_" + stepId + ".err.log");

    var outBuilder = new StringBuilder(2048);
    var errBuilder = new StringBuilder(1024);
    var exitCode = 0;

    try
    {
      var backupRoot = request.DomainGpoBackupPath!;
      var gpoFolders = Directory.EnumerateDirectories(backupRoot).ToList();
      if (gpoFolders.Count == 0)
        outBuilder.AppendLine("No GPO backup subfolders found in " + backupRoot);

      foreach (var gpoFolder in gpoFolders)
      {
        var folderName = Path.GetFileName(gpoFolder);
        outBuilder.AppendLine("Importing GPO: " + folderName);

        var qFolder = ApplyProcessHelpers.ToPowerShellSingleQuoted(folderName);
        var qRoot = ApplyProcessHelpers.ToPowerShellSingleQuoted(backupRoot);
        var script = "Import-Module GroupPolicy; " +
          $"Import-GPO -BackupGpoName {qFolder} -Path {qRoot} -TargetName {qFolder} -CreateIfNeeded";

        var psi = new ProcessStartInfo
        {
          FileName = "powershell.exe",
          Arguments = ApplyProcessHelpers.BuildEncodedCommandArgs(script),
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var psOut = process.StandardOutput.ReadToEnd();
        var psErr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        outBuilder.AppendLine(psOut);
        if (!string.IsNullOrWhiteSpace(psErr))
          errBuilder.AppendLine(psErr);

        if (process.ExitCode != 0)
        {
          exitCode = process.ExitCode;
          _logger.LogWarning("Import-GPO for {GpoName} exited with code {ExitCode}", folderName, process.ExitCode);
          continue;
        }

        _logger.LogInformation("Imported GPO: {GpoName}", folderName);
      }
    }
    catch (Exception ex)
    {
      errBuilder.AppendLine(ex.ToString());
      exitCode = -1;
      _logger.LogError(ex, "GPO import step failed");
    }

    File.WriteAllText(stdout, outBuilder.ToString());
    File.WriteAllText(stderr, errBuilder.ToString());

    return new ApplyStepOutcome
    {
      StepName = stepName,
      ExitCode = exitCode,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now,
      StdOutPath = stdout,
      StdErrPath = stderr
    };
  }

  private static string? ResolvePolicyDefinitionsTarget(ApplyRequest request, string bundleRoot)
  {
    if (!string.IsNullOrWhiteSpace(request.AdmxPolicyDefinitionsPath))
      return request.AdmxPolicyDefinitionsPath;

    if (OperatingSystem.IsWindows())
    {
      var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
      if (!string.IsNullOrWhiteSpace(windowsDir))
        return Path.Combine(windowsDir, "PolicyDefinitions");
    }

    return Path.Combine(bundleRoot, "Apply", "PolicyDefinitions");
  }

  private static bool IsTemplateApplicableToCurrentOs(string path)
  {
    var normalized = path.Replace('\\', '/').ToLowerInvariant();
    var os = DetectHostOsTag();

    var isWin11Tagged = normalized.Contains("windows11") || normalized.Contains("windows_11") || normalized.Contains("win11");
    var isWin10Tagged = normalized.Contains("windows10") || normalized.Contains("windows_10") || normalized.Contains("win10");
    var isServerTagged = normalized.Contains("server");

    var isServer2022Tagged = normalized.Contains("2022");
    var isServer2019Tagged = normalized.Contains("2019");
    var isServer2016Tagged = normalized.Contains("2016");

    var hasAnyOsTag = isWin11Tagged || isWin10Tagged || isServerTagged;
    if (!hasAnyOsTag)
      return true;

    return os switch
    {
      "win11" => isWin11Tagged,
      "win10" => isWin10Tagged,
      "server2022" => isServerTagged && (isServer2022Tagged || (!isServer2019Tagged && !isServer2016Tagged)),
      "server2019" => isServerTagged && (isServer2019Tagged || (!isServer2022Tagged && !isServer2016Tagged)),
      "server2016" => isServerTagged && (isServer2016Tagged || (!isServer2022Tagged && !isServer2019Tagged)),
      "server" => isServerTagged,
      _ => true
    };
  }

  private static string DetectHostOsTag()
  {
    if (!OperatingSystem.IsWindows())
      return "other";

    var productName = string.Empty;
    try
    {
      productName = Microsoft.Win32.Registry.GetValue(
          @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
          "ProductName",
          null) as string ?? string.Empty;
    }
    catch (Exception)
    {
      productName = string.Empty;
    }

    if (productName.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) >= 0)
      return "win11";
    if (productName.IndexOf("Windows 10", StringComparison.OrdinalIgnoreCase) >= 0)
      return "win10";
    if (productName.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0)
    {
      if (productName.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0)
        return "server2022";
      if (productName.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0)
        return "server2019";
      if (productName.IndexOf("2016", StringComparison.OrdinalIgnoreCase) >= 0)
        return "server2016";
      return "server";
    }

    return "other";
  }

  private static void CopyTemplateFile(string sourceRoot, string targetRoot, string sourceFile)
  {
    var relative = Path.GetRelativePath(sourceRoot, sourceFile);
    var destination = Path.Combine(targetRoot, relative);
    var destinationDir = Path.GetDirectoryName(destination);
    if (!string.IsNullOrWhiteSpace(destinationDir))
      Directory.CreateDirectory(destinationDir);

    File.Copy(sourceFile, destination, true);
  }
}
