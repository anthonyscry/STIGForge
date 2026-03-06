using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class RollbackService
{
  private readonly IRollbackRepository _repo;
  private readonly IProcessRunner? _processRunner;
  private readonly IClock _clock;

  private static readonly IReadOnlyList<RegistryCaptureTarget> DefaultRegistryTargets =
    new RegistryCaptureTarget[]
    {
      new(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenSlideshow", "DWord"),
      new(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", "DWord"),
      new(@"HKLM:\SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile", "EnableFirewall", "DWord"),
      new(@"HKLM:\SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel", "DWord"),
      new(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs", "DWord")
    };

  private static readonly IReadOnlyList<string> DefaultServiceNames =
    new[] { "RemoteRegistry", "SSDPSRV", "lltdsvc" };

  private static readonly IReadOnlyList<string> DefaultBundleRelativeFiles =
    new[]
    {
      @"Manifest\manifest.json",
      @"Manifest\pack_controls.json",
      @"Manifest\overlays.json",
      @"Manual\answers.json",
      @"Apply\RunApply.ps1",
      @"Apply\apply_run.json"
    };

  public RollbackService(
    IRollbackRepository repo,
    IProcessRunner? processRunner = null,
    IClock? clock = null)
  {
    _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    _processRunner = processRunner;
    _clock = clock ?? new SystemClock();
  }

  public async Task<RollbackSnapshot> CapturePreHardeningStateAsync(string bundleRoot, string description, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot)) throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));
    if (!Directory.Exists(bundleRoot)) throw new DirectoryNotFoundException("Bundle root not found: " + bundleRoot);

    var snapshot = new RollbackSnapshot
    {
      SnapshotId = Guid.NewGuid().ToString("N"),
      BundleRoot = bundleRoot,
      Description = description ?? string.Empty,
      CreatedAt = _clock.Now,
      RegistryKeys = await CaptureRegistryStatesAsync(ct).ConfigureAwait(false),
      FilePaths = CaptureFileStates(bundleRoot),
      ServiceStates = await CaptureServiceStatesAsync(ct).ConfigureAwait(false),
      GpoSettings = await CaptureGpoSettingsAsync(ct).ConfigureAwait(false)
    };

    snapshot.RollbackScriptPath = GenerateRollbackScript(snapshot);
    await _repo.SaveAsync(snapshot, ct).ConfigureAwait(false);
    return snapshot;
  }

  public async Task<RollbackApplyResult> ExecuteRollbackAsync(string snapshotId, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(snapshotId)) throw new ArgumentException("Snapshot ID is required.", nameof(snapshotId));

    var snapshot = await _repo.GetAsync(snapshotId, ct).ConfigureAwait(false);
    if (snapshot == null)
      throw new InvalidOperationException("Rollback snapshot not found: " + snapshotId);

    var scriptPath = snapshot.RollbackScriptPath;
    if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
    {
      scriptPath = GenerateRollbackScript(snapshot);
      snapshot.RollbackScriptPath = scriptPath;
      await _repo.SaveAsync(snapshot, ct).ConfigureAwait(false);
    }

    var started = _clock.Now;
    var processResult = await RunPowerShellFileAsync(scriptPath, ct).ConfigureAwait(false);
    var finished = _clock.Now;

    return new RollbackApplyResult
    {
      SnapshotId = snapshot.SnapshotId,
      Success = processResult.ExitCode == 0,
      ExitCode = processResult.ExitCode,
      Output = processResult.StandardOutput,
      Error = processResult.StandardError,
      StartedAt = started,
      FinishedAt = finished
    };
  }

  public Task<IReadOnlyList<RollbackSnapshot>> ListSnapshotsAsync(string bundleRoot, int limit, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot)) throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));
    if (limit < 1)
      limit = 100;

    return _repo.ListByBundleAsync(bundleRoot, limit, ct);
  }

  public string GenerateRollbackScript(RollbackSnapshot snapshot, string? outputPath = null)
  {
    if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
    if (string.IsNullOrWhiteSpace(snapshot.BundleRoot)) throw new ArgumentException("BundleRoot is required.", nameof(snapshot));

    var scriptPath = string.IsNullOrWhiteSpace(outputPath)
      ? Path.Combine(snapshot.BundleRoot, "Apply", "RollbackSnapshots", snapshot.SnapshotId, "rollback-apply.ps1")
      : outputPath;

    var parent = Path.GetDirectoryName(scriptPath);
    if (!string.IsNullOrWhiteSpace(parent))
      Directory.CreateDirectory(parent);

    var sb = new StringBuilder(4096);
    sb.AppendLine("# Rollback script generated by STIGForge");
    sb.AppendLine("# Snapshot ID: " + snapshot.SnapshotId);
    sb.AppendLine("# Bundle: " + snapshot.BundleRoot);
    sb.AppendLine("# Created: " + snapshot.CreatedAt.ToString("o"));
    if (!string.IsNullOrWhiteSpace(snapshot.Description))
      sb.AppendLine("# Description: " + snapshot.Description);
    sb.AppendLine();
    sb.AppendLine("$ErrorActionPreference = 'Stop'");
    sb.AppendLine();

    sb.AppendLine("Write-Host 'Restoring registry values...' -ForegroundColor Cyan");
    foreach (var registry in snapshot.RegistryKeys.OrderBy(k => k.Path, StringComparer.OrdinalIgnoreCase))
    {
      var pathLiteral = EscapePowerShellLiteral(registry.Path);
      var valueNameLiteral = EscapePowerShellLiteral(registry.ValueName);

      if (registry.Exists)
      {
        var valueType = NormalizeRegistryValueType(registry.ValueType);
        var valueLiteral = EscapePowerShellLiteral(registry.Value ?? string.Empty);
        sb.AppendLine("if (-not (Test-Path '" + pathLiteral + "')) { New-Item -Path '" + pathLiteral + "' -Force | Out-Null }");
        sb.AppendLine("Set-ItemProperty -Path '" + pathLiteral + "' -Name '" + valueNameLiteral + "' -Value '" + valueLiteral + "' -Type " + valueType + " -Force");
      }
      else
      {
        sb.AppendLine("if (Test-Path '" + pathLiteral + "') { Remove-ItemProperty -Path '" + pathLiteral + "' -Name '" + valueNameLiteral + "' -ErrorAction SilentlyContinue }");
      }
    }

    sb.AppendLine();
    sb.AppendLine("Write-Host 'Restoring service states...' -ForegroundColor Cyan");
    foreach (var service in snapshot.ServiceStates.OrderBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase))
    {
      var serviceNameLiteral = EscapePowerShellLiteral(service.ServiceName);
      var startupType = NormalizeStartupType(service.StartupType);
      if (!string.IsNullOrWhiteSpace(startupType))
        sb.AppendLine("Set-Service -Name '" + serviceNameLiteral + "' -StartupType " + startupType + " -ErrorAction SilentlyContinue");

      if (string.Equals(service.Status, "Running", StringComparison.OrdinalIgnoreCase))
        sb.AppendLine("Start-Service -Name '" + serviceNameLiteral + "' -ErrorAction SilentlyContinue");
      else if (string.Equals(service.Status, "Stopped", StringComparison.OrdinalIgnoreCase))
        sb.AppendLine("Stop-Service -Name '" + serviceNameLiteral + "' -Force -ErrorAction SilentlyContinue");
    }

    sb.AppendLine();
    sb.AppendLine("Write-Host 'GPO rollback guidance...' -ForegroundColor Cyan");
    foreach (var gpo in snapshot.GpoSettings.OrderBy(g => g.GpoName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
    {
      var name = string.IsNullOrWhiteSpace(gpo.GpoName) ? "unknown" : gpo.GpoName;
      sb.AppendLine("# Applied GPO captured at snapshot time: " + name);
    }

    sb.AppendLine();
    sb.AppendLine("Write-Host 'File rollback guidance...' -ForegroundColor Cyan");
    foreach (var file in snapshot.FilePaths.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
    {
      sb.AppendLine("# File: " + file.Path + " (exists=" + (file.Exists ? "true" : "false") + ", sha256=" + (file.Sha256 ?? "n/a") + ")");
    }

    sb.AppendLine();
    sb.AppendLine("Write-Host 'Rollback script completed.' -ForegroundColor Green");

    File.WriteAllText(scriptPath, sb.ToString(), Encoding.UTF8);
    return scriptPath;
  }

  private async Task<IReadOnlyList<RollbackRegistryKeyState>> CaptureRegistryStatesAsync(CancellationToken ct)
  {
    var states = new List<RollbackRegistryKeyState>();
    foreach (var target in DefaultRegistryTargets)
    {
      var state = new RollbackRegistryKeyState
      {
        Path = target.Path,
        ValueName = target.ValueName,
        ValueType = target.ValueType
      };

      if (_processRunner == null)
      {
        states.Add(state);
        continue;
      }

      var script = "$value = Get-ItemProperty -Path '" + EscapePowerShellLiteral(target.Path) + "' -Name '" + EscapePowerShellLiteral(target.ValueName) +
        "' -ErrorAction SilentlyContinue; if ($null -eq $value) { Write-Output '__MISSING__' } else { Write-Output ([string]$value.'" +
        EscapePowerShellLiteral(target.ValueName) + "') }";

      var result = await RunPowerShellCommandAsync(script, ct).ConfigureAwait(false);
      var output = (result.StandardOutput ?? string.Empty).Trim();
      if (result.ExitCode == 0 && !string.Equals(output, "__MISSING__", StringComparison.Ordinal))
      {
        state.Exists = true;
        state.Value = output;
      }

      states.Add(state);
    }

    return states;
  }

  private IReadOnlyList<RollbackFilePathState> CaptureFileStates(string bundleRoot)
  {
    var files = new List<RollbackFilePathState>();
    foreach (var relativePath in DefaultBundleRelativeFiles)
    {
      var fullPath = Path.Combine(bundleRoot, relativePath);
      var exists = File.Exists(fullPath);
      files.Add(new RollbackFilePathState
      {
        Path = fullPath,
        Exists = exists,
        Sha256 = exists ? ComputeSha256(fullPath) : null
      });
    }

    return files;
  }

  private async Task<IReadOnlyList<RollbackServiceState>> CaptureServiceStatesAsync(CancellationToken ct)
  {
    var states = new List<RollbackServiceState>();
    foreach (var serviceName in DefaultServiceNames)
    {
      var state = new RollbackServiceState
      {
        ServiceName = serviceName,
        Status = "Unknown",
        StartupType = "Unknown"
      };

      if (_processRunner == null)
      {
        states.Add(state);
        continue;
      }

      var script = "$svc = Get-CimInstance Win32_Service -Filter \"Name='" + EscapePowerShellLiteral(serviceName) +
        "'\" -ErrorAction SilentlyContinue; if ($null -eq $svc) { Write-Output '__MISSING__' } else { Write-Output ($svc.State + '|' + $svc.StartMode) }";

      var result = await RunPowerShellCommandAsync(script, ct).ConfigureAwait(false);
      var output = (result.StandardOutput ?? string.Empty).Trim();
      if (result.ExitCode == 0 && !string.Equals(output, "__MISSING__", StringComparison.Ordinal))
      {
        var parts = output.Split('|');
        state.Status = parts.Length > 0 ? parts[0] : "Unknown";
        state.StartupType = parts.Length > 1 ? parts[1] : "Unknown";
      }

      states.Add(state);
    }

    return states;
  }

  private async Task<IReadOnlyList<RollbackGpoSettingState>> CaptureGpoSettingsAsync(CancellationToken ct)
  {
    if (_processRunner == null)
      return [];

    var startInfo = new ProcessStartInfo
    {
      FileName = "gpresult.exe",
      Arguments = "/r /scope computer",
      CreateNoWindow = true,
      UseShellExecute = false
    };

    var result = await _processRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
    if (result.ExitCode != 0)
      return [];

    var names = ParseAppliedGpoNames(result.StandardOutput);
    return names
      .Select(name => new RollbackGpoSettingState
      {
        SettingPath = "AppliedGpo",
        Value = "Applied",
        GpoName = name
      })
      .ToList();
  }

  private async Task<ProcessResult> RunPowerShellFileAsync(string scriptPath, CancellationToken ct)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
      CreateNoWindow = true,
      UseShellExecute = false
    };

    if (_processRunner != null)
      return await _processRunner.RunAsync(startInfo, ct).ConfigureAwait(false);

    using var process = Process.Start(startInfo);
    if (process == null)
      return new ProcessResult { ExitCode = -1, StandardError = "Failed to start powershell.exe" };

    if (!process.WaitForExit(120000))
    {
      process.Kill();
      return new ProcessResult { ExitCode = -1, StandardError = "Rollback execution timed out." };
    }

    return new ProcessResult
    {
      ExitCode = process.ExitCode
    };
  }

  private async Task<ProcessResult> RunPowerShellCommandAsync(string script, CancellationToken ct)
  {
    if (_processRunner == null)
      return new ProcessResult { ExitCode = -1, StandardError = "Process runner unavailable." };

    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var startInfo = new ProcessStartInfo
    {
      FileName = "powershell.exe",
      Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
      CreateNoWindow = true,
      UseShellExecute = false
    };

    return await _processRunner.RunAsync(startInfo, ct).ConfigureAwait(false);
  }

  private static IReadOnlyList<string> ParseAppliedGpoNames(string output)
  {
    if (string.IsNullOrWhiteSpace(output))
      return [];

    var names = new List<string>();
    using var reader = new StringReader(output);
    var inAppliedSection = false;

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
      var trimmed = line.Trim();
      if (!inAppliedSection)
      {
        if (trimmed.StartsWith("Applied Group Policy Objects", StringComparison.OrdinalIgnoreCase))
          inAppliedSection = true;

        continue;
      }

      if (trimmed.Length == 0)
        continue;

      if (trimmed.StartsWith("The following GPOs were not applied", StringComparison.OrdinalIgnoreCase)
          || trimmed.StartsWith("The computer is a part", StringComparison.OrdinalIgnoreCase)
          || trimmed.StartsWith("Computer Settings", StringComparison.OrdinalIgnoreCase)
          || trimmed.EndsWith(":", StringComparison.Ordinal))
      {
        if (names.Count > 0)
          break;

        continue;
      }

      if (trimmed.IndexOf("----", StringComparison.Ordinal) >= 0)
        continue;

      names.Add(trimmed);
    }

    return names
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    var hash = SHA256.HashData(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  private static string NormalizeStartupType(string? startupType)
  {
    if (string.IsNullOrWhiteSpace(startupType))
      return string.Empty;

    var token = startupType.Trim();
    if (token.Equals("Auto", StringComparison.OrdinalIgnoreCase)
      || token.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
      return "Automatic";

    if (token.Equals("Manual", StringComparison.OrdinalIgnoreCase)
      || token.Equals("Demand", StringComparison.OrdinalIgnoreCase))
      return "Manual";

    if (token.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
      return "Disabled";

    return string.Empty;
  }

  private static string NormalizeRegistryValueType(string? valueType)
  {
    if (string.IsNullOrWhiteSpace(valueType))
      return "String";

    if (valueType.Equals("DWord", StringComparison.OrdinalIgnoreCase)
      || valueType.Equals("DWORD", StringComparison.OrdinalIgnoreCase))
      return "DWord";

    if (valueType.Equals("QWord", StringComparison.OrdinalIgnoreCase)
      || valueType.Equals("QWORD", StringComparison.OrdinalIgnoreCase))
      return "QWord";

    if (valueType.Equals("MultiString", StringComparison.OrdinalIgnoreCase))
      return "MultiString";

    if (valueType.Equals("ExpandString", StringComparison.OrdinalIgnoreCase))
      return "ExpandString";

    if (valueType.Equals("Binary", StringComparison.OrdinalIgnoreCase))
      return "Binary";

    return "String";
  }

  private static string EscapePowerShellLiteral(string value)
  {
    return (value ?? string.Empty).Replace("'", "''");
  }

  private sealed class RegistryCaptureTarget
  {
    public RegistryCaptureTarget(string path, string valueName, string valueType)
    {
      Path = path;
      ValueName = valueName;
      ValueType = valueType;
    }

    public string Path { get; }
    public string ValueName { get; }
    public string ValueType { get; }
  }
}
