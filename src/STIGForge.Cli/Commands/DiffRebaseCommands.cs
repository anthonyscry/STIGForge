using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Cli.Commands;

internal static class DiffRebaseCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterListPacks(rootCmd, buildHost);
    RegisterListOverlays(rootCmd, buildHost);
    RegisterDiffPacks(rootCmd, buildHost);
    RegisterRebaseOverlay(rootCmd, buildHost);
  }

  private static void RegisterListPacks(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("list-packs", "List all imported content packs");
    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var packs = host.Services.GetRequiredService<IContentPackRepository>();
      var list = await packs.ListAsync(CancellationToken.None);
      if (list.Count == 0) { Console.WriteLine("No packs found."); }
      else
      {
        Console.WriteLine($"{"PackId",-40} {"Name",-30} {"Imported"}");
        Console.WriteLine(new string('-', 90));
        foreach (var p in list) Console.WriteLine($"{p.PackId,-40} {p.Name,-30} {p.ImportedAt:yyyy-MM-dd HH:mm}");
      }
      await host.StopAsync();
    });
    rootCmd.AddCommand(cmd);
  }

  private static void RegisterListOverlays(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("list-overlays", "List all overlays in the repository");
    cmd.SetHandler(async () =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var repo = host.Services.GetRequiredService<IOverlayRepository>();
      var list = await repo.ListAsync(CancellationToken.None);
      if (list.Count == 0) { Console.WriteLine("No overlays found."); }
      else
      {
        Console.WriteLine($"{"OverlayId",-40} {"Name",-30} {"Overrides",-10} {"Updated"}");
        Console.WriteLine(new string('-', 100));
        foreach (var o in list) Console.WriteLine($"{o.OverlayId,-40} {o.Name,-30} {o.Overrides.Count,-10} {o.UpdatedAt:yyyy-MM-dd HH:mm}");
      }
      await host.StopAsync();
    });
    rootCmd.AddCommand(cmd);
  }

  private static void RegisterDiffPacks(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("diff-packs", "Compare two content packs and show what changed");
    var baselineOpt = new Option<string>("--baseline", "Baseline (old) pack id") { IsRequired = true };
    var targetOpt = new Option<string>("--target", "Target (new) pack id") { IsRequired = true };
    var outputOpt = new Option<string>("--output", () => string.Empty, "Write Markdown report to file");
    var jsonOpt = new Option<bool>("--json", "Output full diff as JSON");
    cmd.AddOption(baselineOpt); cmd.AddOption(targetOpt); cmd.AddOption(outputOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (baseline, target, output, json) =>
    {
      using var host = buildHost();
      await host.StartAsync();
      var diffService = host.Services.GetRequiredService<BaselineDiffService>();
      var diff = await diffService.ComparePacksAsync(baseline, target, CancellationToken.None);

      if (json)
      {
        var jsonText = JsonSerializer.Serialize(diff, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(output)) { File.WriteAllText(output, jsonText); Console.WriteLine("JSON diff written to: " + output); }
        else Console.WriteLine(jsonText);
      }
      else
      {
        Console.WriteLine($"Diff: {baseline} -> {target}");
        Console.WriteLine($"  Added:     {diff.TotalAdded}");
        Console.WriteLine($"  Removed:   {diff.TotalRemoved}");
        Console.WriteLine($"  Changed:   {diff.TotalModified}");
        Console.WriteLine($"  Review-required: {diff.TotalReviewRequired}");
        Console.WriteLine($"  Unchanged: {diff.TotalUnchanged}");
        Console.WriteLine();

        if (diff.AddedControls.Count > 0) { Console.WriteLine("Added controls:"); foreach (var c in diff.AddedControls) Console.WriteLine($"  + {c.ControlKey}  {c.NewControl?.Title}"); Console.WriteLine(); }
        if (diff.RemovedControls.Count > 0) { Console.WriteLine("Removed controls:"); foreach (var c in diff.RemovedControls) Console.WriteLine($"  - {c.ControlKey}  {c.BaselineControl?.Title}"); Console.WriteLine(); }
        if (diff.ModifiedControls.Count > 0)
        {
          Console.WriteLine("Changed controls:");
          foreach (var c in diff.ModifiedControls)
          {
            var classification = c.RequiresReview ? "review-required" : "changed";
            Console.WriteLine($"  {(c.RequiresReview ? "!!" : "~")} {c.ControlKey}  [{string.Join(", ", c.Changes.Select(ch => ch.FieldName))}] ({classification})");
          }
          Console.WriteLine();
        }

        if (diff.ReviewRequiredControls.Count > 0)
        {
          Console.WriteLine("Review-required controls:");
          foreach (var c in diff.ReviewRequiredControls)
            Console.WriteLine($"  !! {c.ControlKey}  {c.ReviewReason}");
          Console.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
          var md = new System.Text.StringBuilder();
          md.AppendLine($"# Pack Diff: {baseline} \u2192 {target}");
          md.AppendLine(); md.AppendLine("| Metric | Count |"); md.AppendLine("|--------|-------|");
          md.AppendLine($"| Added | {diff.TotalAdded} |"); md.AppendLine($"| Removed | {diff.TotalRemoved} |");
          md.AppendLine($"| Changed | {diff.TotalModified} |");
          md.AppendLine($"| Review required | {diff.TotalReviewRequired} |");
          md.AppendLine($"| Unchanged | {diff.TotalUnchanged} |"); md.AppendLine();
          if (diff.AddedControls.Count > 0) { md.AppendLine("## Added Controls"); md.AppendLine(); foreach (var c in diff.AddedControls) md.AppendLine($"- **{c.ControlKey}** \u2014 {c.NewControl?.Title}"); md.AppendLine(); }
          if (diff.RemovedControls.Count > 0) { md.AppendLine("## Removed Controls"); md.AppendLine(); foreach (var c in diff.RemovedControls) md.AppendLine($"- **{c.ControlKey}** \u2014 {c.BaselineControl?.Title}"); md.AppendLine(); }
          if (diff.ModifiedControls.Count > 0) { md.AppendLine("## Changed Controls"); md.AppendLine(); foreach (var c in diff.ModifiedControls) { md.AppendLine($"### {c.ControlKey}"); md.AppendLine(); md.AppendLine($"- Classification: **{(c.RequiresReview ? "review-required" : "changed")}**"); if (!string.IsNullOrWhiteSpace(c.ReviewReason)) md.AppendLine($"- Review reason: {c.ReviewReason}"); md.AppendLine(); md.AppendLine("| Field | Impact | Old | New |"); md.AppendLine("|-------|--------|-----|-----|"); foreach (var ch in c.Changes) md.AppendLine($"| {ch.FieldName} | {ch.Impact} | {Helpers.Truncate(ch.OldValue, 60)} | {Helpers.Truncate(ch.NewValue, 60)} |"); md.AppendLine(); } }
          if (diff.ReviewRequiredControls.Count > 0) { md.AppendLine("## Review Required Controls"); md.AppendLine(); foreach (var c in diff.ReviewRequiredControls) md.AppendLine($"- **{c.ControlKey}** - {c.ReviewReason}"); md.AppendLine(); }
          File.WriteAllText(output, md.ToString());
          Console.WriteLine("Markdown report written to: " + output);
        }
      }
      await host.StopAsync();
    }, baselineOpt, targetOpt, outputOpt, jsonOpt);

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterRebaseOverlay(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("rebase-overlay", "Rebase an overlay from baseline pack to target pack");
    var overlayOpt = new Option<string>("--overlay", "Overlay id") { IsRequired = true };
    var baselineOpt = new Option<string>("--baseline", "Current baseline pack id") { IsRequired = true };
    var targetOpt = new Option<string>("--target", "New target pack id") { IsRequired = true };
    var applyOpt = new Option<bool>("--apply", "Apply the rebase");
    var outputOpt = new Option<string>("--output", () => string.Empty, "Write rebase report to file");
    var jsonOpt = new Option<bool>("--json", "Output report as JSON");
    cmd.AddOption(overlayOpt); cmd.AddOption(baselineOpt); cmd.AddOption(targetOpt);
    cmd.AddOption(applyOpt); cmd.AddOption(outputOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var overlayId = ctx.ParseResult.GetValueForOption(overlayOpt) ?? string.Empty;
      var baseline = ctx.ParseResult.GetValueForOption(baselineOpt) ?? string.Empty;
      var target = ctx.ParseResult.GetValueForOption(targetOpt) ?? string.Empty;
      var apply = ctx.ParseResult.GetValueForOption(applyOpt);
      var output = ctx.ParseResult.GetValueForOption(outputOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);

      using var host = buildHost();
      await host.StartAsync();
      var svc = host.Services.GetRequiredService<OverlayRebaseService>();
      var report = await svc.RebaseOverlayAsync(overlayId, baseline, target, CancellationToken.None);

      if (!report.Success) { Console.Error.WriteLine("Rebase failed: " + report.ErrorMessage); Environment.ExitCode = 1; await host.StopAsync(); return; }

      if (json)
      {
        var text = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(output)) { File.WriteAllText(output, text); Console.WriteLine("JSON report written to: " + output); }
        else Console.WriteLine(text);
      }
      else
      {
        Console.WriteLine($"Rebase: overlay {overlayId}  ({baseline} -> {target})");
        Console.WriteLine($"  Overall confidence: {report.OverallConfidence:P0}");
        Console.WriteLine($"  Safe actions:       {report.SafeActions}");
        Console.WriteLine($"  Review needed:      {report.ReviewNeeded}");
        Console.WriteLine($"  Blocking conflicts: {report.BlockingConflicts}");
        Console.WriteLine($"  High risk:          {report.HighRisk}");
        Console.WriteLine();
        foreach (var a in report.Actions)
        {
          var icon = a.ActionType switch { RebaseActionType.Keep => "  OK", RebaseActionType.KeepWithWarning => "  ~~", RebaseActionType.ReviewRequired => "  !!", RebaseActionType.Remove => "  --", RebaseActionType.Remap => "  =>", _ => "  ??" };
          Console.WriteLine($"{icon} {a.OriginalControlKey,-40} {a.ActionType,-20} ({a.Confidence:P0})  {a.Reason}");
          if (a.IsBlockingConflict || a.RequiresReview)
            Console.WriteLine($"     Action: {a.RecommendedAction}");
        }
        Console.WriteLine();

        var blockingActions = report.Actions.Where(a => a.IsBlockingConflict).ToList();
        if (blockingActions.Count > 0)
        {
          Console.WriteLine("Blocking conflicts:");
          foreach (var action in blockingActions)
            Console.WriteLine($"  !! {action.OriginalControlKey} - {action.Reason} | {action.RecommendedAction}");
          Console.WriteLine();
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
          var md = new System.Text.StringBuilder();
          md.AppendLine($"# Rebase Report: {overlayId}"); md.AppendLine();
          md.AppendLine($"- **Baseline:** {baseline}"); md.AppendLine($"- **Target:** {target}");
          md.AppendLine($"- **Overall Confidence:** {report.OverallConfidence:P0}");
          md.AppendLine($"- **Blocking conflicts:** {report.BlockingConflicts}");
          md.AppendLine();
          md.AppendLine("| Control | Action | Confidence | Requires Review | Blocking | Reason | Recommended Action |"); md.AppendLine("|---------|--------|------------|-----------------|----------|--------|--------------------|");
          foreach (var a in report.Actions) md.AppendLine($"| {a.OriginalControlKey} | {a.ActionType} | {a.Confidence:P0} | {(a.RequiresReview ? "Yes" : "No")} | {(a.IsBlockingConflict ? "Yes" : "No")} | {a.Reason} | {a.RecommendedAction} |");
          File.WriteAllText(output, md.ToString());
          Console.WriteLine("Markdown report written to: " + output);
        }
      }

      if (apply)
      {
        var blockingActions = report.Actions.Where(a => a.IsBlockingConflict).ToList();
        if (blockingActions.Count > 0)
        {
          Console.Error.WriteLine($"Rebase apply blocked: {blockingActions.Count} unresolved blocking conflict(s) require operator review.");
          foreach (var action in blockingActions)
            Console.Error.WriteLine($"  {action.OriginalControlKey}: {action.RecommendedAction}");
          Environment.ExitCode = 2;
          await host.StopAsync();
          return;
        }

        var rebased = await svc.ApplyRebaseAsync(overlayId, report, CancellationToken.None);
        Console.WriteLine($"Rebased overlay created: {rebased.OverlayId} ({rebased.Name})");
      }
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }
}
