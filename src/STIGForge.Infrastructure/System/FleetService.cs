using System.Collections.Concurrent;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.System;

/// <summary>
/// Fleet orchestration service for multi-machine STIG hardening via WinRM/PSRemoting.
/// Supports parallel apply/verify across multiple Windows targets with result aggregation.
/// </summary>
public sealed class FleetService
{
  private readonly ICredentialStore? _credentialStore;

  public FleetService(ICredentialStore? credentialStore = null)
  {
    _credentialStore = credentialStore;
  }
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

  private async Task<FleetMachineResult> ExecuteOnMachineAsync(FleetTarget target, FleetRequest request, CancellationToken ct)
  {
    target = ResolveCredentials(target);
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
    var cliPath = request.RemoteCliPath ?? "C:\\STIGForge\\STIGForge.Cli.exe";
    var bundleRoot = request.RemoteBundleRoot ?? "C:\\STIGForge\\bundle";
    var applyMode = (request.ApplyMode ?? string.Empty).Trim();
    var scapCmd = (request.ScapCmd ?? string.Empty).Trim();
    var scapArgs = (request.ScapArgs ?? string.Empty).Trim();
    var evaluateStigRoot = (request.EvaluateStigRoot ?? string.Empty).Trim();
    var args = new List<string>();

    switch (request.Operation?.ToLowerInvariant())
    {
      case "apply":
        args.Add("apply-run");
        args.Add("--bundle");
        args.Add(bundleRoot);
        if (!string.IsNullOrWhiteSpace(applyMode))
        {
          args.Add("--mode");
          args.Add(applyMode);
        }
        break;

      case "verify":
        if (!string.IsNullOrWhiteSpace(scapCmd))
        {
          args.Add("verify-scap");
          args.Add("--cmd");
          args.Add(scapCmd);
          if (!string.IsNullOrWhiteSpace(scapArgs))
          {
            args.Add("--args");
            args.Add(scapArgs);
          }
        }
        else if (!string.IsNullOrWhiteSpace(evaluateStigRoot))
        {
          args.Add("verify-evaluate-stig");
          args.Add("--tool-root");
          args.Add(evaluateStigRoot);
        }
        else
        {
          args.Add("orchestrate");
          args.Add("--bundle");
          args.Add(bundleRoot);
        }
        break;

      case "orchestrate":
      default:
        args.Add("orchestrate");
        args.Add("--bundle");
        args.Add(bundleRoot);
        if (!string.IsNullOrWhiteSpace(evaluateStigRoot))
        {
          args.Add("--evaluate-stig");
          args.Add(evaluateStigRoot);
        }

        if (!string.IsNullOrWhiteSpace(scapCmd))
        {
          args.Add("--scap-cmd");
          args.Add(scapCmd);
        }

        if (!string.IsNullOrWhiteSpace(scapArgs))
        {
          args.Add("--scap-args");
          args.Add(scapArgs);
        }
        break;
    }

    return BuildCliInvocationScript(cliPath, args);
  }

  private static string BuildCliInvocationScript(string cliPath, IReadOnlyList<string> args)
  {
    var script = new StringBuilder();
    script.Append("$cliPath = ").Append(ToPowerShellSingleQuoted(cliPath)).Append("; ");
    script.Append("$cliArgs = @(");
    for (var i = 0; i < args.Count; i++)
    {
      if (i > 0) script.Append(", ");
      script.Append(ToPowerShellSingleQuoted(args[i]));
    }

    script.Append("); ");
    script.Append("& $cliPath @cliArgs");
    return script.ToString();
  }

  private static string ToPowerShellSingleQuoted(string? value)
  {
    return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
  }

  private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellRemoteAsync(
    FleetTarget target, string remoteScript, int timeoutSeconds, CancellationToken ct)
  {
    var connectionTarget = !string.IsNullOrWhiteSpace(target.IpAddress) ? target.IpAddress : target.HostName;

    var psScript = new StringBuilder();
    psScript.Append("$computerName = ").Append(ToPowerShellSingleQuoted(connectionTarget)).Append("; ");
    psScript.Append("$remoteScript = ").Append(ToPowerShellSingleQuoted(remoteScript)).Append("; ");

    if (!string.IsNullOrWhiteSpace(target.CredentialUser))
    {
      psScript.Append("$fleetUser = $env:STIGFORGE_FLEET_USER; ");
      psScript.Append("$fleetPass = $env:STIGFORGE_FLEET_PASS; ");
      psScript.Append("$fleetSecure = ConvertTo-SecureString $fleetPass -AsPlainText -Force; ");
      psScript.Append("$fleetCredential = New-Object System.Management.Automation.PSCredential($fleetUser, $fleetSecure); ");
      psScript.Append("$result = Invoke-Command -ComputerName $computerName -Credential $fleetCredential -ScriptBlock ([ScriptBlock]::Create($remoteScript)) -ErrorAction Stop; ");
    }
    else
    {
      psScript.Append("$result = Invoke-Command -ComputerName $computerName -ScriptBlock ([ScriptBlock]::Create($remoteScript)) -ErrorAction Stop; ");
    }

    psScript.Append("$result");

    var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript.ToString()));

    var psi = new global::System.Diagnostics.ProcessStartInfo("powershell.exe",
      $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}")
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    if (!string.IsNullOrWhiteSpace(target.CredentialUser))
    {
      psi.Environment["STIGFORGE_FLEET_USER"] = target.CredentialUser;
      psi.Environment["STIGFORGE_FLEET_PASS"] = target.CredentialPassword ?? string.Empty;
    }

    using var proc = global::System.Diagnostics.Process.Start(psi);
    if (proc == null) return (-1, string.Empty, "Failed to start PowerShell");

    var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    proc.EnableRaisingEvents = true;
    proc.Exited += (_, _) => exited.TrySetResult(true);
    if (proc.HasExited) exited.TrySetResult(true);

    using var cancelRegistration = ct.Register(() =>
    {
      TryKill(proc);
      exited.TrySetCanceled(ct);
    });

    var outputTask = proc.StandardOutput.ReadToEndAsync();
    var errorTask = proc.StandardError.ReadToEndAsync();

    var effectiveTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 600;
    var timeoutTask = Task.Delay(effectiveTimeoutSeconds * 1000);
    var completed = await Task.WhenAny(exited.Task, timeoutTask).ConfigureAwait(false);

    if (completed == timeoutTask)
    {
      TryKill(proc);
      await Task.WhenAny(exited.Task, Task.Delay(2000)).ConfigureAwait(false);
      var timeoutOutput = await outputTask.ConfigureAwait(false);
      var timeoutError = await errorTask.ConfigureAwait(false);
      var timeoutMessage = "Timed out after " + effectiveTimeoutSeconds + " seconds.";
      if (!string.IsNullOrWhiteSpace(timeoutError))
        timeoutMessage += " " + timeoutError.Trim();
      return (-1, timeoutOutput.Trim(), timeoutMessage.Trim());
    }

    try
    {
      await exited.Task.ConfigureAwait(false);
    }
    catch (TaskCanceledException)
    {
      var canceledOutput = await outputTask.ConfigureAwait(false);
      var canceledError = await errorTask.ConfigureAwait(false);
      var canceledMessage = "Operation cancelled.";
      if (!string.IsNullOrWhiteSpace(canceledError))
        canceledMessage += " " + canceledError.Trim();
      return (-1, canceledOutput.Trim(), canceledMessage.Trim());
    }

    var output = await outputTask.ConfigureAwait(false);
    var error = await errorTask.ConfigureAwait(false);

    return (proc.ExitCode, output.Trim(), error.Trim());
  }

  private static void TryKill(global::System.Diagnostics.Process process)
  {
    try
    {
      if (!process.HasExited)
        process.Kill();
    }
    catch
    {
    }
  }

  private async Task<FleetMachineStatus> TestConnectionAsync(FleetTarget target, CancellationToken ct)
  {
    target = ResolveCredentials(target);
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

  private FleetTarget ResolveCredentials(FleetTarget target)
  {
    if (!string.IsNullOrWhiteSpace(target.CredentialUser))
      return target;
    if (_credentialStore == null)
      return target;

    var cred = _credentialStore.Load(target.HostName);
    if (cred == null)
      return target;

    return new FleetTarget
    {
      HostName = target.HostName,
      IpAddress = target.IpAddress,
      CredentialUser = cred.Value.Username,
      CredentialPassword = cred.Value.Password
    };
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
