using System.Text;
using System.Text.RegularExpressions;

namespace STIGForge.Infrastructure.System;

/// <summary>
/// Manages Windows Task Scheduler integration for scheduled re-verification.
/// Creates/removes scheduled tasks that invoke the STIGForge CLI verify commands on a cron-like schedule.
/// </summary>
public sealed class ScheduledTaskService
{
  private const string TaskFolderName = "STIGForge";
  private static readonly Regex TaskNameRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
  private static readonly Regex StartTimeRegex = new("^[0-2][0-9]:[0-5][0-9]$", RegexOptions.Compiled);
  private static readonly HashSet<string> ValidDaysOfWeek = new(StringComparer.OrdinalIgnoreCase)
  {
    "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"
  };

  /// <summary>
  /// Register a scheduled verify task using Windows Task Scheduler (schtasks.exe).
  /// </summary>
  public ScheduledTaskResult Register(ScheduledTaskRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.TaskName))
      throw new ArgumentException("TaskName is required.");
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    ValidateTaskName(request.TaskName);
    ValidateDaysOfWeek(request.DaysOfWeek);

    var fullTaskName = TaskFolderName + "\\" + request.TaskName;
    var cliPath = NormalizeCliPath(request.CliPath ?? GetDefaultCliPath());
    var args = BuildVerifyArgs(request);

    // Build schtasks.exe command
    var frequency = MapFrequency(request.Frequency);
    var time = request.StartTime ?? "06:00";
    if (!StartTimeRegex.IsMatch(time))
      throw new ArgumentException("StartTime must be in HH:mm format (e.g. '06:00').", nameof(request));

    var taskRunCommand = BuildTaskRunCommand(cliPath, args);

    var schtasksArgs = new StringBuilder();
    schtasksArgs.Append("/Create /TN ").Append(QuoteForSchtasksArgument(fullTaskName)).Append(" /TR ").Append(QuoteForSchtasksArgument(taskRunCommand)).Append(' ');
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
    ValidateTaskName(taskName);
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
    var result = RunSchtasks($"/Query /TN \"{TaskFolderName}\\\" /FO LIST");

    return new ScheduledTaskResult
    {
      Success = result.ExitCode == 0,
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
      sb.Append("verify-scap --cmd ").Append(QuoteCliArgument(request.ScapCmd));
      if (!string.IsNullOrWhiteSpace(request.ScapArgs))
        sb.Append(" --args ").Append(QuoteCliArgument(request.ScapArgs));
      if (!string.IsNullOrWhiteSpace(request.OutputRoot))
        sb.Append(" --output-root ").Append(QuoteCliArgument(request.OutputRoot));
    }
    else if (!string.IsNullOrWhiteSpace(request.VerifyType) && request.VerifyType == "evaluate-stig")
    {
      sb.Append("verify-evaluate-stig");
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
        sb.Append(" --tool-root ").Append(QuoteCliArgument(request.EvaluateStigRoot));
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigArgs))
        sb.Append(" --args ").Append(QuoteCliArgument(request.EvaluateStigArgs));
      if (!string.IsNullOrWhiteSpace(request.OutputRoot))
        sb.Append(" --output-root ").Append(QuoteCliArgument(request.OutputRoot));
    }
    else
    {
      // Default: orchestrate (apply + verify)
      sb.Append("orchestrate --bundle ").Append(QuoteCliArgument(request.BundleRoot));
      if (!string.IsNullOrWhiteSpace(request.EvaluateStigRoot))
        sb.Append(" --evaluate-stig ").Append(QuoteCliArgument(request.EvaluateStigRoot));
      if (!string.IsNullOrWhiteSpace(request.ScapCmd))
        sb.Append(" --scap-cmd ").Append(QuoteCliArgument(request.ScapCmd));
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
    sb.AppendLine("$cliPath = '" + cliPath.Replace("'", "''") + "'");
    sb.AppendLine("$logDir = Join-Path $env:ProgramData 'STIGForge' 'logs'");
    sb.AppendLine("if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }");
    sb.AppendLine("$logFile = Join-Path $logDir ('scheduled-verify-" + request.TaskName.Replace("'", "''") + "-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')");
    sb.AppendLine();
    sb.AppendLine($"& $cliPath {args} 2>&1 | Tee-Object -FilePath $logFile");
    sb.AppendLine("exit $LASTEXITCODE");

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static string GetDefaultCliPath()
  {
    // Try to find the CLI in common locations
    var dir = AppContext.BaseDirectory;
    if (!string.IsNullOrWhiteSpace(dir))
    {
      var candidate = Path.Combine(dir, "STIGForge.Cli.exe");
      if (File.Exists(candidate)) return candidate;
      candidate = Path.Combine(dir, "STIGForge.Cli.dll");
      if (File.Exists(candidate)) return candidate;
    }

    throw new FileNotFoundException("Unable to locate STIGForge.Cli executable.");
  }

  private static string NormalizeCliPath(string cliPath)
  {
    if (string.IsNullOrWhiteSpace(cliPath))
      throw new ArgumentException("CliPath is required.", nameof(cliPath));

    if (cliPath.IndexOfAny([';', '|', '&', '>', '<', '\r', '\n']) >= 0)
      throw new ArgumentException("CliPath contains invalid characters.", nameof(cliPath));

    var normalized = Path.GetFullPath(cliPath.Trim());
    if (!File.Exists(normalized))
      throw new FileNotFoundException("CliPath does not exist.", normalized);

    var extension = Path.GetExtension(normalized);
    if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
      && !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
      throw new ArgumentException("CliPath must point to an .exe or .dll file.", nameof(cliPath));

    return normalized;
  }

  private static string BuildTaskRunCommand(string cliPath, string args)
  {
    if (string.Equals(Path.GetExtension(cliPath), ".dll", StringComparison.OrdinalIgnoreCase))
      return "\"dotnet\" \"" + EscapeDoubleQuotes(cliPath) + "\" " + args;

    return "\"" + EscapeDoubleQuotes(cliPath) + "\" " + args;
  }

  private static string QuoteForSchtasksArgument(string value)
  {
    return "\"" + EscapeDoubleQuotes(value) + "\"";
  }

  private static string QuoteCliArgument(string? value)
  {
    return "\"" + EscapeDoubleQuotes(value ?? string.Empty) + "\"";
  }

  private static string EscapeDoubleQuotes(string value)
  {
    return value.Replace("\"", "\\\"");
  }

  private static void ValidateTaskName(string taskName)
  {
    if (taskName.Length > 200)
      throw new ArgumentException("TaskName must be 200 characters or fewer.", nameof(taskName));

    if (!TaskNameRegex.IsMatch(taskName))
      throw new ArgumentException("TaskName contains invalid characters.", nameof(taskName));
  }

  private static void ValidateDaysOfWeek(string? daysOfWeek)
  {
    if (string.IsNullOrWhiteSpace(daysOfWeek))
      return;

    var parts = daysOfWeek.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
      throw new ArgumentException("DaysOfWeek is invalid.", nameof(daysOfWeek));

    foreach (var part in parts)
    {
      if (!ValidDaysOfWeek.Contains(part))
        throw new ArgumentException("DaysOfWeek contains invalid values.", nameof(daysOfWeek));
    }
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
