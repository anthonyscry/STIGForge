using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class ExceptionCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterExceptionCreate(rootCmd, buildHost);
    RegisterExceptionRevoke(rootCmd, buildHost);
    RegisterExceptionList(rootCmd, buildHost);
    RegisterExceptionAudit(rootCmd, buildHost);
  }

  private static void RegisterExceptionCreate(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("exception-create", "Create a control exception");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var ruleIdOpt = new Option<string>("--rule-id", "Rule ID") { IsRequired = true };
    var vulnIdOpt = new Option<string>("--vuln-id", () => string.Empty, "Vulnerability ID");
    var typeOpt = new Option<string>("--type", "Exception type (Waiver/RiskAcceptance/TechnicalException)") { IsRequired = true };
    var riskOpt = new Option<string>("--risk", "Risk level (High/Medium/Low)") { IsRequired = true };
    var approvedByOpt = new Option<string>("--approved-by", "Approver name") { IsRequired = true };
    var justificationOpt = new Option<string>("--justification", () => string.Empty, "Justification text");
    var justificationDocOpt = new Option<string>("--justification-doc", () => string.Empty, "Justification document path");
    var expiresOpt = new Option<string>("--expires", () => string.Empty, "Expiration date (ISO 8601)");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(ruleIdOpt);
    cmd.AddOption(vulnIdOpt);
    cmd.AddOption(typeOpt);
    cmd.AddOption(riskOpt);
    cmd.AddOption(approvedByOpt);
    cmd.AddOption(justificationOpt);
    cmd.AddOption(justificationDocOpt);
    cmd.AddOption(expiresOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var ruleId = ctx.ParseResult.GetValueForOption(ruleIdOpt) ?? string.Empty;
      var vulnId = ctx.ParseResult.GetValueForOption(vulnIdOpt) ?? string.Empty;
      var type = ctx.ParseResult.GetValueForOption(typeOpt) ?? string.Empty;
      var risk = ctx.ParseResult.GetValueForOption(riskOpt) ?? string.Empty;
      var approvedBy = ctx.ParseResult.GetValueForOption(approvedByOpt) ?? string.Empty;
      var justification = ctx.ParseResult.GetValueForOption(justificationOpt) ?? string.Empty;
      var justificationDoc = ctx.ParseResult.GetValueForOption(justificationDocOpt) ?? string.Empty;
      var expires = ctx.ParseResult.GetValueForOption(expiresOpt) ?? string.Empty;

      DateTimeOffset? expiresAt = null;
      if (!string.IsNullOrWhiteSpace(expires))
      {
        if (!DateTimeOffset.TryParse(expires, out var parsed))
          throw new ArgumentException("Invalid --expires value. Use ISO 8601 date/time.");
        expiresAt = parsed;
      }

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionCommands");
      var service = host.Services.GetRequiredService<ExceptionWorkflowService>();

      logger.LogInformation("exception-create started: bundle={Bundle}, rule={RuleId}", bundle, ruleId);
      var created = await service.CreateExceptionAsync(new CreateExceptionRequest
      {
        BundleRoot = bundle,
        RuleId = ruleId,
        VulnId = string.IsNullOrWhiteSpace(vulnId) ? null : vulnId,
        ExceptionType = type,
        RiskLevel = risk,
        ApprovedBy = approvedBy,
        Justification = string.IsNullOrWhiteSpace(justification) ? null : justification,
        JustificationDoc = string.IsNullOrWhiteSpace(justificationDoc) ? null : justificationDoc,
        ExpiresAt = expiresAt
      }, ct);

      Console.WriteLine("Exception created: " + created.ExceptionId);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExceptionRevoke(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("exception-revoke", "Revoke an exception");
    var exceptionIdOpt = new Option<string>("--exception-id", "Exception ID") { IsRequired = true };
    var revokedByOpt = new Option<string>("--revoked-by", "Who is revoking") { IsRequired = true };

    cmd.AddOption(exceptionIdOpt);
    cmd.AddOption(revokedByOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var exceptionId = ctx.ParseResult.GetValueForOption(exceptionIdOpt) ?? string.Empty;
      var revokedBy = ctx.ParseResult.GetValueForOption(revokedByOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionCommands");
      var service = host.Services.GetRequiredService<ExceptionWorkflowService>();

      logger.LogInformation("exception-revoke started: exceptionId={ExceptionId}", exceptionId);
      await service.RevokeExceptionAsync(exceptionId, revokedBy, ct);
      Console.WriteLine("Exception revoked: " + exceptionId);
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExceptionList(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("exception-list", "List active/expired exceptions");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var expiredOpt = new Option<bool>("--expired", "Show expired instead of active");
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(expiredOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var showExpired = ctx.ParseResult.GetValueForOption(expiredOpt);
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionCommands");
      var service = host.Services.GetRequiredService<ExceptionWorkflowService>();

      logger.LogInformation("exception-list started: bundle={Bundle}, expired={Expired}", bundle, showExpired);
      var list = showExpired
        ? await service.GetExpiredExceptionsAsync(bundle, ct)
        : await service.GetActiveExceptionsAsync(bundle, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(list, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine(showExpired ? "Expired exceptions:" : "Active exceptions:");
        foreach (var e in list)
          Console.WriteLine($"  {e.ExceptionId}  Rule={e.RuleId}  Type={e.ExceptionType}  Risk={e.RiskLevel}  Expires={e.ExpiresAt:yyyy-MM-dd}");
        Console.WriteLine("Total: " + list.Count);
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterExceptionAudit(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("exception-audit", "Audit exception health");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", "JSON output");

    cmd.AddOption(bundleOpt);
    cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var ct = ctx.GetCancellationToken();
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExceptionCommands");
      var service = host.Services.GetRequiredService<ExceptionWorkflowService>();

      logger.LogInformation("exception-audit started: bundle={Bundle}", bundle);
      var report = await service.AuditExceptionsAsync(bundle, ct);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions.Indented));
      }
      else
      {
        Console.WriteLine($"Active: {report.ActiveCount}  Expired: {report.ExpiredCount}  Revoked: {report.RevokedCount}");
        Console.WriteLine($"Expiring soon (30 days): {report.ExpiringWithin30Days}");
        foreach (var e in report.ExpiringExceptions)
          Console.WriteLine($"  {e.ExceptionId}  Rule={e.RuleId}  Expires={e.ExpiresAt:yyyy-MM-dd}  Risk={e.RiskLevel}");
      }

      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}
