using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;

namespace STIGForge.Cli.Commands;

internal static class AuditCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterAuditLog(rootCmd, buildHost);
    RegisterAuditVerify(rootCmd, buildHost);
  }

  private static void RegisterAuditLog(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("audit-log", "Query and export the tamper-evident audit trail");
    var actionOpt = new Option<string>("--action", () => string.Empty, "Filter by action type (e.g., apply, verify, export-emass)");
    var targetOpt = new Option<string>("--target", () => string.Empty, "Filter by target (bundle/pack/rule id substring)");
    var fromOpt = new Option<string>("--from", () => string.Empty, "Start date (ISO 8601)");
    var toOpt = new Option<string>("--to", () => string.Empty, "End date (ISO 8601)");
    var limitOpt = new Option<int>("--limit", () => 50, "Maximum entries to return");
    var jsonOpt = new Option<bool>("--json", "Output as JSON");
    var outputOpt = new Option<string>("--output", () => string.Empty, "Write results to file");
    cmd.AddOption(actionOpt); cmd.AddOption(targetOpt); cmd.AddOption(fromOpt);
    cmd.AddOption(toOpt); cmd.AddOption(limitOpt); cmd.AddOption(jsonOpt); cmd.AddOption(outputOpt);

    cmd.SetHandler(async (action, target, from, to, limit, json, output) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AuditCommands");
      var audit = host.Services.GetRequiredService<IAuditTrailService>();

      var query = new AuditQuery
      {
        Action = string.IsNullOrWhiteSpace(action) ? null : action,
        Target = string.IsNullOrWhiteSpace(target) ? null : target,
        From = string.IsNullOrWhiteSpace(from) ? null : DateTimeOffset.Parse(from),
        To = string.IsNullOrWhiteSpace(to) ? null : DateTimeOffset.Parse(to),
        Limit = limit
      };

      logger.LogInformation("audit-log query: action={Action}, target={Target}, limit={Limit}", query.Action, query.Target, query.Limit);
      var entries = await audit.QueryAsync(query, CancellationToken.None);

      if (json)
      {
        var text = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(output)) { File.WriteAllText(output, text); Console.WriteLine("Wrote " + entries.Count + " entries to: " + output); }
        else Console.WriteLine(text);
      }
      else
      {
        if (entries.Count == 0) { Console.WriteLine("No audit entries found."); }
        else
        {
          Console.WriteLine($"{"Timestamp",-28} {"Action",-20} {"Target",-30} {"Result",-10} {"User"}");
          Console.WriteLine(new string('-', 110));
          foreach (var e in entries)
            Console.WriteLine($"{e.Timestamp:yyyy-MM-dd HH:mm:ss zzz}  {Helpers.Truncate(e.Action, 18),-20} {Helpers.Truncate(e.Target, 28),-30} {Helpers.Truncate(e.Result, 8),-10} {e.User}");
        }
        Console.WriteLine();
        Console.WriteLine($"Total: {entries.Count} entries");

        if (!string.IsNullOrWhiteSpace(output))
        {
          var sb = new System.Text.StringBuilder();
          sb.AppendLine("Timestamp,Action,Target,Result,User,Machine,Detail,EntryHash");
          foreach (var e in entries)
            sb.AppendLine(string.Join(",", Helpers.Csv(e.Timestamp.ToString("o")), Helpers.Csv(e.Action), Helpers.Csv(e.Target), Helpers.Csv(e.Result), Helpers.Csv(e.User), Helpers.Csv(e.Machine), Helpers.Csv(e.Detail), Helpers.Csv(e.EntryHash)));
          File.WriteAllText(output, sb.ToString());
          Console.WriteLine("Wrote CSV to: " + output);
        }
      }
      await host.StopAsync();
    }, actionOpt, targetOpt, fromOpt, toOpt, limitOpt, jsonOpt, outputOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterAuditVerify(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("audit-verify", "Verify integrity of the audit trail chain");

    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AuditCommands");
      var audit = host.Services.GetRequiredService<IAuditTrailService>();

      logger.LogInformation("audit-verify started");
      var isValid = await audit.VerifyIntegrityAsync(CancellationToken.None);

      if (isValid)
      {
        Console.WriteLine("Audit trail integrity: VALID");
        Console.WriteLine("All entries have valid chained hashes.");
        logger.LogInformation("audit-verify completed: VALID");
      }
      else
      {
        Console.Error.WriteLine("Audit trail integrity: INVALID");
        Console.Error.WriteLine("One or more entries have been tampered with or the chain is broken.");
        logger.LogWarning("audit-verify completed: INVALID - chain integrity broken");
        Environment.ExitCode = 1;
      }
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}
