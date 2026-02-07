using System.Collections.Concurrent;
using System.Text;

namespace STIGForge.Infrastructure.System;

/// <summary>
/// Fleet orchestration service for multi-machine STIG hardening via WinRM/PSRemoting.
/// Supports parallel apply/verify across multiple Windows targets with result aggregation.
/// </summary>
public sealed class FleetService
{
  /// <summary>
  /// Execute a fleet operation (apply or verify) across multiple target machines.
  /// Uses PowerShell Remoting (WinRM) to invoke STIGForge CLI on each target.
  /// </summary>
  public async Task<FleetResult> ExecuteAsync(FleetRequest request, CancellationToken ct)
  {
    if (request.Targets == null || request.Targets.Count == 0)
      throw new ArgumentException("At least one target machine is required.");

    var results = new ConcurrentBag<FleetMachineResult>();
    var startedAt = DateTimeOffset.Now;

    // Execute in parallel, respecting max concurrency
    var semaphore = new SemaphoreSlim(request.MaxConcurrency > 0 ? request.MaxConcurrency : 5);
    var tasks = new List<Task>();

    foreach (var target in request.Targets)
    {
      var t = Task.Run(async () =>
      {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
          var machineResult = await ExecuteOnMachineAsync(target, request, ct).ConfigureAwait(false);
          results.Add(machineResult);
        }
        finally
        {
          semaphore.Release();
        }
      }, ct);
      tasks.Add(t);
    }

    await Task.WhenAll(tasks).ConfigureAwait(false);

    var allResults = results.ToList();
    var finishedAt = DateTimeOffset.Now;

    return new FleetResult
    {
      MachineResults = allResults,
      StartedAt = startedAt,
      FinishedAt = finishedAt,
      TotalMachines = allResults.Count,
      SuccessCount = allResults.Count(r => r.Success),
      FailureCount = allResults.Count(r => !r.Success),
      Operation = request.Operation
    };
  }

  /// <summary>
  /// Query fleet status — check which machines are reachable via WinRM.
  /// </summary>
  public async Task<FleetStatusResult> CheckStatusAsync(IReadOnlyList<FleetTarget> targets, CancellationToken ct)
  {
    var results = new ConcurrentBag<FleetMachineStatus>();
    var tasks = targets.Select(target => Task.Run(async () =>
    {
      var status = await TestConnectionAsync(target, ct).ConfigureAwait(false);
      results.Add(status);
    }, ct));

    await Task.WhenAll(tasks).ConfigureAwait(false);

    var allResults = results.ToList();
    return new FleetStatusResult
    {
      MachineStatuses = allResults,
      TotalMachines = allResults.Count,
      ReachableCount = allResults.Count(s => s.IsReachable),
      UnreachableCount = allResults.Count(s => !s.IsReachable)
    };
  }

  private static async Task<FleetMachineResult> ExecuteOnMachineAsync(FleetTarget target, FleetRequest request, CancellationToken ct)
  {
    var startedAt = DateTimeOffset.Now;
    try
    {
      var script = BuildRemoteScript(target, request);
      var result = await RunPowerShellRemoteAsync(target, script, request.TimeoutSeconds, ct).ConfigureAwait(false);

      return new FleetMachineResult
      {
        MachineName = target.HostName,
        IpAddress = target.IpAddress,
        Success = result.ExitCode == 0,
        ExitCode = result.ExitCode,
        Output = result.Output,
        Error = result.Error,
        StartedAt = startedAt,
        FinishedAt = DateTimeOffset.Now
      };
    }
    catch (Exception ex)
    {
      return new FleetMachineResult
      {
        MachineName = target.HostName,
        IpAddress = target.IpAddress,
        Success = false,
        ExitCode = -1,
        Output = string.Empty,
        Error = ex.Message,
        StartedAt = startedAt,
        FinishedAt = DateTimeOffset.Now
      };
    }
  }

  private static string BuildRemoteScript(FleetTarget target, FleetRequest request)
  {
    var sb = new StringBuilder();
    var cliPath = request.RemoteCliPath ?? "C:\\STIGForge\\STIGForge.Cli.exe";
    var bundleRoot = request.RemoteBundleRoot ?? "C:\\STIGForge\\bundle";

    switch (request.Operation?.ToLowerInvariant())
    {
      case "apply":
        sb.Append($"& '{cliPath}' apply-run --bundle '{bundleRoot}'");
        if (!string.IsNullOrWhiteSpace(request.ApplyMode))
          sb.Append($" --mode {request.ApplyMode}");
        break;

      case "verify":
        if (!string.IsNullOrWhiteSpace(request.ScapCmd))
        {
          sb.Append($"& '{cliPath}' verify-scap --cmd '{request.ScapCmd}'");
          if (!string.IsNullOrWhiteSpace(request.ScapArgs))
            sb.Append($" --args '{request.ScapArgs}'");
        }
        else if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
        {
          sb.Append($"& '{cliPath}' verify-evaluate-stig --tool-root '{request.EvaluateStigRoot}'");
        }
        else
        {
          sb.Append($"& '{cliPath}' orchestrate --bundle '{bundleRoot}'");
        }
        break;

      case "orchestrate":
      default:
        sb.Append($"& '{cliPath}' orchestrate --bundle '{bundleRoot}'");
        if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
          sb.Append($" --evaluate-stig '{request.EvaluateStigRoot}'");
        if (!string.IsNullOrWhiteSpace(request.ScapCmd))
          sb.Append($" --scap-cmd '{request.ScapCmd}'");
        break;
    }

    return sb.ToString();
  }

  private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellRemoteAsync(
    FleetTarget target, string remoteScript, int timeoutSeconds, CancellationToken ct)
  {
    var connectionTarget = !string.IsNullOrWhiteSpace(target.IpAddress) ? target.IpAddress : target.HostName;

    // Build Invoke-Command PSRemoting call
    var psScript = new StringBuilder();
    psScript.Append($"$result = Invoke-Command -ComputerName '{connectionTarget}'");

    if (!string.IsNullOrWhiteSpace(target.CredentialUser))
    {
      psScript.Append($" -Credential (New-Object PSCredential('{target.CredentialUser}', (ConvertTo-SecureString '{target.CredentialPassword}' -AsPlainText -Force)))");
    }

    psScript.Append($" -ScriptBlock {{ {remoteScript} }} -ErrorAction Stop; ");
    psScript.Append("$result");

    var psi = new global::System.Diagnostics.ProcessStartInfo("powershell.exe",
      $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psScript}\"")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var proc = global::System.Diagnostics.Process.Start(psi);
    if (proc == null) return (-1, string.Empty, "Failed to start PowerShell");

    var outputTask = proc.StandardOutput.ReadToEndAsync();
    var errorTask = proc.StandardError.ReadToEndAsync();

    var timeout = timeoutSeconds > 0 ? timeoutSeconds * 1000 : 600000; // default 10 min
    proc.WaitForExit(timeout);

    var output = await outputTask.ConfigureAwait(false);
    var error = await errorTask.ConfigureAwait(false);

    return (proc.ExitCode, output.Trim(), error.Trim());
  }

  private static async Task<FleetMachineStatus> TestConnectionAsync(FleetTarget target, CancellationToken ct)
  {
    var connectionTarget = !string.IsNullOrWhiteSpace(target.IpAddress) ? target.IpAddress : target.HostName;

    try
    {
      var psi = new global::System.Diagnostics.ProcessStartInfo("powershell.exe",
        $"-NoProfile -NonInteractive -Command \"Test-WSMan -ComputerName '{connectionTarget}' -ErrorAction Stop | Out-Null; Write-Output 'OK'\"")
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var proc = global::System.Diagnostics.Process.Start(psi);
      if (proc == null)
        return new FleetMachineStatus { MachineName = target.HostName, IsReachable = false, Message = "Failed to start PowerShell" };

      var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
      proc.WaitForExit(15000);

      return new FleetMachineStatus
      {
        MachineName = target.HostName,
        IpAddress = target.IpAddress,
        IsReachable = proc.ExitCode == 0 && output.Trim() == "OK",
        Message = proc.ExitCode == 0 ? "WinRM reachable" : "WinRM unreachable"
      };
    }
    catch (Exception ex)
    {
      return new FleetMachineStatus
      {
        MachineName = target.HostName,
        IpAddress = target.IpAddress,
        IsReachable = false,
        Message = ex.Message
      };
    }
  }
}

public sealed class FleetTarget
{
  public string HostName { get; set; } = string.Empty;
  public string? IpAddress { get; set; }
  public string? CredentialUser { get; set; }
  public string? CredentialPassword { get; set; }
}

public sealed class FleetRequest
{
  public IReadOnlyList<FleetTarget> Targets { get; set; } = Array.Empty<FleetTarget>();
  public string Operation { get; set; } = "orchestrate";
  public string? RemoteCliPath { get; set; }
  public string? RemoteBundleRoot { get; set; }
  public string? ApplyMode { get; set; }
  public string? ScapCmd { get; set; }
  public string? ScapArgs { get; set; }
  public string? EvaluateStigRoot { get; set; }
  public int MaxConcurrency { get; set; } = 5;
  public int TimeoutSeconds { get; set; } = 600;
}

public sealed class FleetResult
{
  public IReadOnlyList<FleetMachineResult> MachineResults { get; set; } = Array.Empty<FleetMachineResult>();
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
  public int TotalMachines { get; set; }
  public int SuccessCount { get; set; }
  public int FailureCount { get; set; }
  public string Operation { get; set; } = string.Empty;
}

public sealed class FleetMachineResult
{
  public string MachineName { get; set; } = string.Empty;
  public string? IpAddress { get; set; }
  public bool Success { get; set; }
  public int ExitCode { get; set; }
  public string Output { get; set; } = string.Empty;
  public string Error { get; set; } = string.Empty;
  public DateTimeOffset StartedAt { get; set; }
  public DateTimeOffset FinishedAt { get; set; }
}

public sealed class FleetStatusResult
{
  public IReadOnlyList<FleetMachineStatus> MachineStatuses { get; set; } = Array.Empty<FleetMachineStatus>();
  public int TotalMachines { get; set; }
  public int ReachableCount { get; set; }
  public int UnreachableCount { get; set; }
}

public sealed class FleetMachineStatus
{
  public string MachineName { get; set; } = string.Empty;
  public string? IpAddress { get; set; }
  public bool IsReachable { get; set; }
  public string Message { get; set; } = string.Empty;
}
