using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.Cli.Commands;

internal static class PhaseCExpansionCommands
{
  internal const int ExitSuccess = 0;
  internal const int ExitFailure = 2;
  internal const int ExitActionRequired = 4;

  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    PhaseCImportCommands.Register(rootCmd, buildHost);
    PhaseCCklCommands.Register(rootCmd, buildHost);
    PhaseCEmassCommands.Register(rootCmd, buildHost);
    PhaseCAgentCommands.Register(rootCmd, buildHost);
  }


  internal static void HandleCommandFailure(InvocationContext ctx, ILogger logger, string command, Exception ex, bool json)
  {
    logger.LogError(ex, "{Command} failed", command);
    if (json)
    {
      WriteJsonEnvelope(command, false, ExitFailure, new { error = ex.Message }, "Command failed.");
    }
    else
    {
      Console.Error.WriteLine($"Error: {ex.Message}");
    }

    ctx.ExitCode = ExitFailure;
  }

  internal static void WriteJsonEnvelope(string command, bool success, int exitCode, object data, string message)
  {
    var envelope = new CommandEnvelope
    {
      Command = command,
      Success = success,
      ExitCode = exitCode,
      Message = message,
      TimestampUtc = DateTimeOffset.UtcNow,
      Data = data
    };

    Console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions.Indented));
  }

  internal static CklConflictResolutionStrategy ParseCklMergeStrategy(string value)
  {
    if (Enum.TryParse<CklConflictResolutionStrategy>(value, ignoreCase: true, out var parsed))
      return parsed;

    throw new ArgumentException("Invalid --strategy value. Allowed: CklWins, StigForgeWins, MostRecent, Manual.");
  }

  private sealed class CommandEnvelope
  {
    public string Command { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public object Data { get; set; } = new();
  }
}
