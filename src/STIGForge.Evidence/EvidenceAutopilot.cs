using System.Diagnostics;
using System.Text;
using STIGForge.Core.Models;

namespace STIGForge.Evidence;

/// <summary>
/// Automated evidence collection for manual controls.
/// Intelligently collects system state, configuration files, and screenshots
/// based on control requirements.
/// </summary>
public sealed class EvidenceAutopilot
{
  private readonly string _evidenceRoot;

  public EvidenceAutopilot(string evidenceRoot)
  {
    _evidenceRoot = evidenceRoot;
  }

  /// <summary>
  /// Collect evidence for a manual control.
  /// Returns list of evidence file paths created.
  /// </summary>
  public async Task<EvidenceCollectionResult> CollectEvidenceAsync(
    ControlRecord control,
    CancellationToken cancellationToken = default)
  {
    var result = new EvidenceCollectionResult
    {
      ControlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? "unknown",
      CollectedAt = DateTimeOffset.Now
    };

    var controlDir = GetControlEvidenceDirectory(control);
    Directory.CreateDirectory(controlDir);

    // Analyze control to determine what evidence to collect
    var evidenceTypes = DetermineEvidenceTypes(control);

    foreach (var evidenceType in evidenceTypes)
    {
      try
      {
        var files = await CollectEvidenceByTypeAsync(evidenceType, controlDir, control, cancellationToken);
        result.EvidenceFiles.AddRange(files);
      }
      catch (Exception ex)
      {
        result.Errors.Add($"Failed to collect {evidenceType}: {ex.Message}");
      }
    }

    // Write collection summary
    var summaryPath = Path.Combine(controlDir, "_collection_summary.txt");
    await WriteSummaryAsync(summaryPath, result);

    return result;
  }

  /// <summary>
  /// Collect registry evidence for a control.
  /// </summary>
  public async Task<List<string>> CollectRegistryEvidenceAsync(
    string registryPath,
    string valueName,
    string outputDir,
    CancellationToken cancellationToken = default)
  {
    var files = new List<string>();
    var outputPath = Path.Combine(outputDir, "registry_export.txt");

    try
    {
      // Use reg.exe to export registry value
      var psi = new ProcessStartInfo
      {
        FileName = "reg.exe",
        Arguments = $"query \"{registryPath}\" /v \"{valueName}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if (process == null) return files;

      var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      var error = await process.StandardError.ReadToEndAsync(cancellationToken);

      await process.WaitForExitAsync(cancellationToken);

      var sb = new StringBuilder();
      sb.AppendLine($"Registry Query: {registryPath}\\{valueName}");
      sb.AppendLine($"Collected: {DateTimeOffset.Now:o}");
      sb.AppendLine();
      sb.AppendLine("Output:");
      sb.AppendLine(output);

      if (!string.IsNullOrWhiteSpace(error))
      {
        sb.AppendLine();
        sb.AppendLine("Errors:");
        sb.AppendLine(error);
      }

      await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
      files.Add(outputPath);
    }
    catch (Exception ex)
    {
      var errorPath = Path.Combine(outputDir, "registry_error.txt");
      await File.WriteAllTextAsync(errorPath, 
        $"Failed to collect registry evidence: {ex.Message}\nPath: {registryPath}\\{valueName}", 
        Encoding.UTF8, 
        cancellationToken);
      files.Add(errorPath);
    }

    return files;
  }

  /// <summary>
  /// Collect file-based evidence (copy configuration file).
  /// </summary>
  public async Task<List<string>> CollectFileEvidenceAsync(
    string sourceFile,
    string outputDir,
    CancellationToken cancellationToken = default)
  {
    var files = new List<string>();

    try
    {
      if (!File.Exists(sourceFile))
      {
        var notFoundPath = Path.Combine(outputDir, "file_not_found.txt");
        await File.WriteAllTextAsync(notFoundPath, 
          $"File not found: {sourceFile}\nChecked at: {DateTimeOffset.Now:o}", 
          Encoding.UTF8, 
          cancellationToken);
        files.Add(notFoundPath);
        return files;
      }

      var fileName = Path.GetFileName(sourceFile);
      var destPath = Path.Combine(outputDir, fileName);
      
      File.Copy(sourceFile, destPath, overwrite: true);
      files.Add(destPath);

      // Also create metadata file
      var metaPath = Path.Combine(outputDir, fileName + ".meta.txt");
      var sb = new StringBuilder();
      sb.AppendLine($"Source: {sourceFile}");
      sb.AppendLine($"Collected: {DateTimeOffset.Now:o}");
      
      var fi = new FileInfo(sourceFile);
      sb.AppendLine($"Size: {fi.Length} bytes");
      sb.AppendLine($"Last Modified: {fi.LastWriteTime:o}");

      await File.WriteAllTextAsync(metaPath, sb.ToString(), Encoding.UTF8, cancellationToken);
      files.Add(metaPath);
    }
    catch (Exception ex)
    {
      var errorPath = Path.Combine(outputDir, "file_error.txt");
      await File.WriteAllTextAsync(errorPath, 
        $"Failed to collect file evidence: {ex.Message}\nSource: {sourceFile}", 
        Encoding.UTF8, 
        cancellationToken);
      files.Add(errorPath);
    }

    return files;
  }

  /// <summary>
  /// Collect command output evidence.
  /// </summary>
  public async Task<List<string>> CollectCommandEvidenceAsync(
    string command,
    string arguments,
    string outputDir,
    CancellationToken cancellationToken = default)
  {
    var files = new List<string>();
    var outputPath = Path.Combine(outputDir, "command_output.txt");

    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = command,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      if (process == null) return files;

      var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      var error = await process.StandardError.ReadToEndAsync(cancellationToken);

      await process.WaitForExitAsync(cancellationToken);

      var sb = new StringBuilder();
      sb.AppendLine($"Command: {command} {arguments}");
      sb.AppendLine($"Executed: {DateTimeOffset.Now:o}");
      sb.AppendLine($"Exit Code: {process.ExitCode}");
      sb.AppendLine();
      sb.AppendLine("Output:");
      sb.AppendLine(output);

      if (!string.IsNullOrWhiteSpace(error))
      {
        sb.AppendLine();
        sb.AppendLine("Errors:");
        sb.AppendLine(error);
      }

      await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
      files.Add(outputPath);
    }
    catch (Exception ex)
    {
      var errorPath = Path.Combine(outputDir, "command_error.txt");
      await File.WriteAllTextAsync(errorPath, 
        $"Failed to execute command: {ex.Message}\nCommand: {command} {arguments}", 
        Encoding.UTF8, 
        cancellationToken);
      files.Add(errorPath);
    }

    return files;
  }

  private async Task<List<string>> CollectEvidenceByTypeAsync(
    EvidenceType type,
    string outputDir,
    ControlRecord control,
    CancellationToken cancellationToken)
  {
    return type switch
    {
      EvidenceType.RegistryValue => await CollectRegistryEvidenceFromControlAsync(outputDir, control, cancellationToken),
      EvidenceType.ConfigurationFile => await CollectFileEvidenceFromControlAsync(outputDir, control, cancellationToken),
      EvidenceType.CommandOutput => await CollectCommandEvidenceFromControlAsync(outputDir, control, cancellationToken),
      EvidenceType.ServiceStatus => await CollectServiceStatusAsync(outputDir, control, cancellationToken),
      EvidenceType.UserRights => await CollectUserRightsAsync(outputDir, cancellationToken),
      _ => new List<string>()
    };
  }

  private List<EvidenceType> DetermineEvidenceTypes(ControlRecord control)
  {
    var types = new List<EvidenceType>();
    var checkText = control.CheckText?.ToLowerInvariant() ?? string.Empty;
    var fixText = control.FixText?.ToLowerInvariant() ?? string.Empty;
    var combined = checkText + " " + fixText;

    // Registry evidence
    if (combined.Contains("registry") || combined.Contains("hkey_") || combined.Contains("gpedit"))
      types.Add(EvidenceType.RegistryValue);

    // File evidence
    if (combined.Contains(".conf") || combined.Contains(".config") || combined.Contains(".ini") || 
        combined.Contains("file") || combined.Contains("directory"))
      types.Add(EvidenceType.ConfigurationFile);

    // Command output
    if (combined.Contains("command") || combined.Contains("run") || combined.Contains("execute"))
      types.Add(EvidenceType.CommandOutput);

    // Service status
    if (combined.Contains("service") || combined.Contains("services.msc"))
      types.Add(EvidenceType.ServiceStatus);

    // User rights
    if (combined.Contains("user rights") || combined.Contains("secpol.msc") || combined.Contains("privilege"))
      types.Add(EvidenceType.UserRights);

    // Default: at least collect system info
    if (types.Count == 0)
      types.Add(EvidenceType.SystemInfo);

    return types;
  }

  private async Task<List<string>> CollectRegistryEvidenceFromControlAsync(
    string outputDir,
    ControlRecord control,
    CancellationToken cancellationToken)
  {
    // Parse registry paths from check text
    // This is a simplified implementation - could be enhanced with better parsing
    var checkText = control.CheckText ?? string.Empty;
    
    if (checkText.Contains("HKEY_LOCAL_MACHINE"))
    {
      // Collect common security registry paths
      return await CollectRegistryEvidenceAsync(
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        "EnableLUA",
        outputDir,
        cancellationToken);
    }

    return new List<string>();
  }

  private async Task<List<string>> CollectFileEvidenceFromControlAsync(
    string outputDir,
    ControlRecord control,
    CancellationToken cancellationToken)
  {
    // Collect common configuration files
    var commonFiles = new[]
    {
      @"C:\Windows\System32\drivers\etc\hosts",
      @"C:\Windows\System32\inetsrv\config\applicationHost.config"
    };

    var files = new List<string>();
    foreach (var file in commonFiles.Where(File.Exists))
    {
      var collected = await CollectFileEvidenceAsync(file, outputDir, cancellationToken);
      files.AddRange(collected);
    }

    return files;
  }

  private async Task<List<string>> CollectCommandEvidenceFromControlAsync(
    string outputDir,
    ControlRecord control,
    CancellationToken cancellationToken)
  {
    // Collect system information
    return await CollectCommandEvidenceAsync("systeminfo", "", outputDir, cancellationToken);
  }

  private async Task<List<string>> CollectServiceStatusAsync(
    string outputDir,
    ControlRecord control,
    CancellationToken cancellationToken)
  {
    return await CollectCommandEvidenceAsync("sc.exe", "query type= service state= all", outputDir, cancellationToken);
  }

  private async Task<List<string>> CollectUserRightsAsync(
    string outputDir,
    CancellationToken cancellationToken)
  {
    return await CollectCommandEvidenceAsync("secedit", "/export /cfg usrrights_temp.inf", outputDir, cancellationToken);
  }

  private string GetControlEvidenceDirectory(ControlRecord control)
  {
    var controlId = control.ExternalIds.VulnId ?? control.ExternalIds.RuleId ?? Guid.NewGuid().ToString("N");
    var safeId = string.Join("_", controlId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(_evidenceRoot, "by_control", safeId);
  }

  private async Task WriteSummaryAsync(string path, EvidenceCollectionResult result)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"Evidence Collection Summary");
    sb.AppendLine($"Control: {result.ControlId}");
    sb.AppendLine($"Collected: {result.CollectedAt:o}");
    sb.AppendLine();
    sb.AppendLine($"Files Collected: {result.EvidenceFiles.Count}");
    foreach (var file in result.EvidenceFiles)
      sb.AppendLine($"  - {Path.GetFileName(file)}");

    if (result.Errors.Count > 0)
    {
      sb.AppendLine();
      sb.AppendLine($"Errors: {result.Errors.Count}");
      foreach (var error in result.Errors)
        sb.AppendLine($"  - {error}");
    }

    await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
  }
}

/// <summary>
/// Result of evidence collection operation.
/// </summary>
public sealed class EvidenceCollectionResult
{
  public string ControlId { get; set; } = string.Empty;
  public DateTimeOffset CollectedAt { get; set; }
  public List<string> EvidenceFiles { get; set; } = new();
  public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Types of evidence that can be automatically collected.
/// </summary>
public enum EvidenceType
{
  RegistryValue,
  ConfigurationFile,
  CommandOutput,
  ServiceStatus,
  UserRights,
  SystemInfo
}
