using System.Text;

namespace STIGForge.Infrastructure.System;

/// <summary>
/// Manages Windows Task Scheduler integration for scheduled re-verification.
/// Creates/removes scheduled tasks that invoke the STIGForge CLI verify commands on a cron-like schedule.
/// </summary>
public sealed class ScheduledTaskService
{
  private const string TaskFolderName = "STIGForge";

  /// <summary>
  /// Register a scheduled verify task using Windows Task Scheduler (schtasks.exe).
  /// </summary>
  public ScheduledTaskResult Register(ScheduledTaskRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.TaskName))
      throw new ArgumentException("TaskName is required.");
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var fullTaskName = TaskFolderName + "\\" + request.TaskName;
    var cliPath = request.CliPath ?? GetDefaultCliPath();
    var args = BuildVerifyArgs(request);

    // Build schtasks.exe command
    var frequency = MapFrequency(request.Frequency);
    var time = request.StartTime ?? "06:00";

    var schtasksArgs = new StringBuilder();
    schtasksArgs.Append($"/Create /TN \"{fullTaskName}\" /TR \"\\\"{cliPath}\\\" {args}\" ");
    schtasksArgs.Append($"/SC {frequency} /ST {time} /F /RL HIGHEST");

    if (!string.IsNullOrWhiteSpace(request.DaysOfWeek) && frequency == "WEEKLY")
      schtasksArgs.Append($" /D {request.DaysOfWeek}");
    if (request.IntervalDays > 0 && frequency == "DAILY")
      schtasksArgs.Append($" /MO {request.IntervalDays}");

    var result = RunSchtasks(schtasksArgs.ToString());

    // Also write a companion script for manual or debug execution
    var scriptPath = Path.Combine(
      Path.GetDirectoryName(cliPath) ?? Environment.CurrentDirectory,
      $"scheduled-verify-{request.TaskName}.ps1");
    WriteCompanionScript(scriptPath, cliPath, args, request);

    return new ScheduledTaskResult
    {
      Success = result.ExitCode == 0,
      TaskName = fullTaskName,
      Message = result.ExitCode == 0 ? "Scheduled task registered." : "Failed: " + result.Output,
      ScriptPath = scriptPath,
      ExitCode = result.ExitCode
    };
  }

  /// <summary>
  /// Remove a scheduled verify task.
  /// </summary>
  public ScheduledTaskResult Unregister(string taskName)
  {
    var fullTaskName = TaskFolderName + "\\" + taskName;
    var result = RunSchtasks($"/Delete /TN \"{fullTaskName}\" /F");

    return new ScheduledTaskResult
    {
      Success = result.ExitCode == 0,
      TaskName = fullTaskName,
      Message = result.ExitCode == 0 ? "Scheduled task removed." : "Failed: " + result.Output,
      ExitCode = result.ExitCode
    };
  }

  /// <summary>
  /// List STIGForge scheduled tasks.
  /// </summary>
  public ScheduledTaskResult List()
  {
    var result = RunSchtasks($"/Query /TN \"{TaskFolderName}\\\" /FO LIST 2>nul");

    return new ScheduledTaskResult
    {
      Success = true,
      TaskName = TaskFolderName,
      Message = string.IsNullOrWhiteSpace(result.Output) ? "No scheduled tasks found." : result.Output,
      ExitCode = result.ExitCode
    };
  }

  private static string BuildVerifyArgs(ScheduledTaskRequest request)
  {
    var sb = new StringBuilder();

    if (!string.IsNullOrWhiteSpace(request.VerifyType) && request.VerifyType == "scap")
    {
      sb.Append($"verify-scap --cmd \"{request.ScapCmd}\"");
      if (!string.IsNullOrWhiteSpace(request.ScapArgs))
        sb.Append($" --args \"{request.ScapArgs}\"");
      if (!string.IsNullOrWhiteSpace(request.OutputRoot))
        sb.Append($" --output-root \"{request.OutputRoot}\"");
    }
    else if (!string.IsNullOrWhiteSpace(request.VerifyType) && request.VerifyType == "evaluate-stig")
    {
      sb.Append("verify-evaluate-stig");
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
        sb.Append($" --tool-root \"{request.EvaluateStigRoot}\"");
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigArgs))
        sb.Append($" --args \"{request.EvaluateStigArgs}\"");
      if (!string.IsNullOrWhiteSpace(request.OutputRoot))
        sb.Append($" --output-root \"{request.OutputRoot}\"");
    }
    else
    {
      // Default: orchestrate (apply + verify)
      sb.Append($"orchestrate --bundle \"{request.BundleRoot}\"");
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
        sb.Append($" --evaluate-stig \"{request.EvaluateStigRoot}\"");
      if (!string.IsNullOrWhiteSpace(request.ScapCmd))
        sb.Append($" --scap-cmd \"{request.ScapCmd}\"");
    }

    return sb.ToString();
  }

  private static string MapFrequency(string? frequency)
  {
    if (string.IsNullOrWhiteSpace(frequency)) return "DAILY";
    return frequency!.Trim().ToUpperInvariant() switch
    {
      "DAILY" => "DAILY",
      "WEEKLY" => "WEEKLY",
      "MONTHLY" => "MONTHLY",
      "ONCE" => "ONCE",
      _ => "DAILY"
    };
  }

  private static void WriteCompanionScript(string path, string cliPath, string args, ScheduledTaskRequest request)
  {
    var sb = new StringBuilder();
    sb.AppendLine("# STIGForge Scheduled Re-verification Script");
    sb.AppendLine($"# Task: {request.TaskName}");
    sb.AppendLine($"# Bundle: {request.BundleRoot}");
    sb.AppendLine($"# Generated: {DateTimeOffset.Now:o}");
    sb.AppendLine();
    sb.AppendLine($"$cliPath = \"{cliPath}\"");
    sb.AppendLine($"$logDir = Join-Path $env:ProgramData 'STIGForge' 'logs'");
    sb.AppendLine("if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }");
    sb.AppendLine($"$logFile = Join-Path $logDir (\"scheduled-verify-{request.TaskName}-\" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')");
    sb.AppendLine();
    sb.AppendLine($"& $cliPath {args} 2>&1 | Tee-Object -FilePath $logFile");
    sb.AppendLine("exit $LASTEXITCODE");

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static string GetDefaultCliPath()
  {
    // Try to find the CLI in common locations
    var current = typeof(ScheduledTaskService).Assembly.Location;
    if (!string.IsNullOrWhiteSpace(current))
    {
      var dir = Path.GetDirectoryName(current);
      if (dir != null)
      {
        var candidate = Path.Combine(dir, "STIGForge.Cli.exe");
        if (File.Exists(candidate)) return candidate;
        candidate = Path.Combine(dir, "STIGForge.Cli.dll");
        if (File.Exists(candidate)) return "dotnet \"" + candidate + "\"";
      }
    }
    return "STIGForge.Cli.exe";
  }

  private static (int ExitCode, string Output) RunSchtasks(string args)
  {
    try
    {
      var psi = new global::System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var proc = global::System.Diagnostics.Process.Start(psi);
      if (proc == null) return (-1, "Failed to start schtasks.exe");

      var output = proc.StandardOutput.ReadToEnd();
      var error = proc.StandardError.ReadToEnd();
      proc.WaitForExit(30000);

      var combined = output + (string.IsNullOrWhiteSpace(error) ? "" : Environment.NewLine + error);
      return (proc.ExitCode, combined.Trim());
    }
    catch (Exception ex)
    {
      return (-1, "Error running schtasks: " + ex.Message);
    }
  }
}

public sealed class ScheduledTaskRequest
{
  public string TaskName { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string? CliPath { get; set; }
  public string? Frequency { get; set; }
  public string? StartTime { get; set; }
  public string? DaysOfWeek { get; set; }
  public int IntervalDays { get; set; }
  public string? VerifyType { get; set; }
  public string? ScapCmd { get; set; }
  public string? ScapArgs { get; set; }
  public string? EvaluateStigRoot { get; set; }
  public string? EvaluateStigArgs { get; set; }
  public string? OutputRoot { get; set; }
}

public sealed class ScheduledTaskResult
{
  public bool Success { get; set; }
  public string TaskName { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public string? ScriptPath { get; set; }
  public int ExitCode { get; set; }
}
