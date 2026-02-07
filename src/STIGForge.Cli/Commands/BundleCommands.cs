using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Evidence;
using STIGForge.Verify;

namespace STIGForge.Cli.Commands;

internal static class BundleCommands
{
  public static void Register(RootCommand rootCmd, Func<IHost> buildHost)
  {
    RegisterListManualControls(rootCmd);
    RegisterManualAnswer(rootCmd);
    RegisterEvidenceSave(rootCmd);
    RegisterBundleSummary(rootCmd);
    RegisterSupportBundle(rootCmd, buildHost);
    RegisterOverlayEdit(rootCmd, buildHost);
  }

  private static void RegisterListManualControls(RootCommand rootCmd)
  {
    var cmd = new Command("list-manual-controls", "List manual controls and their answer status from a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var statusOpt = new Option<string>("--status", () => string.Empty, "Filter by status: Pass|Fail|NA|Open");
    var searchOpt = new Option<string>("--search", () => string.Empty, "Text search filter");
    cmd.AddOption(bundleOpt); cmd.AddOption(statusOpt); cmd.AddOption(searchOpt);

    cmd.SetHandler((InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var statusFilter = ctx.ParseResult.GetValueForOption(statusOpt) ?? string.Empty;
      var searchFilter = ctx.ParseResult.GetValueForOption(searchOpt) ?? string.Empty;

      if (!Directory.Exists(bundle)) { Console.Error.WriteLine("Bundle not found: " + bundle); Environment.ExitCode = 2; return; }
      var controlsPath = Path.Combine(bundle, "Manifest", "pack_controls.json");
      if (!File.Exists(controlsPath)) { Console.Error.WriteLine("No pack_controls.json found."); Environment.ExitCode = 2; return; }

      var allControls = JsonSerializer.Deserialize<List<ControlRecord>>(File.ReadAllText(controlsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ControlRecord>();
      var manualControls = allControls.Where(c => c.IsManual).ToList();
      var svc = new ManualAnswerService();
      var answerFile = svc.LoadAnswerFile(bundle);

      var rows = new List<(string Id, string Title, string Status)>();
      foreach (var c in manualControls)
      {
        var key = c.ExternalIds.RuleId ?? c.ExternalIds.VulnId ?? c.ControlId;
        var answer = answerFile.Answers.FirstOrDefault(a =>
          (!string.IsNullOrWhiteSpace(a.RuleId) && string.Equals(a.RuleId, c.ExternalIds.RuleId, StringComparison.OrdinalIgnoreCase)) ||
          (!string.IsNullOrWhiteSpace(a.VulnId) && string.Equals(a.VulnId, c.ExternalIds.VulnId, StringComparison.OrdinalIgnoreCase)));
        rows.Add((key, c.Title, answer?.Status ?? "Open"));
      }

      if (!string.IsNullOrWhiteSpace(statusFilter)) rows = rows.Where(r => r.Status.IndexOf(statusFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
      if (!string.IsNullOrWhiteSpace(searchFilter)) rows = rows.Where(r => r.Id.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 || r.Title.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

      var stats = svc.GetProgressStats(bundle, manualControls);
      Console.WriteLine($"Manual controls: {stats.TotalControls} total, {stats.AnsweredControls} answered ({stats.PercentComplete:F0}%)");
      Console.WriteLine($"  Pass: {stats.PassCount}  Fail: {stats.FailCount}  NA: {stats.NotApplicableCount}  Open: {stats.UnansweredControls}");
      Console.WriteLine();

      if (rows.Count == 0) Console.WriteLine("No matching controls.");
      else
      {
        Console.WriteLine($"{"Id",-30} {"Status",-15} {"Title"}");
        Console.WriteLine(new string('-', 90));
        foreach (var r in rows) Console.WriteLine($"{r.Id,-30} {r.Status,-15} {Helpers.Truncate(r.Title, 44)}");
      }
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterManualAnswer(RootCommand rootCmd)
  {
    var cmd = new Command("manual-answer", "Save manual control answers (single or batch CSV)");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var csvOpt = new Option<string>("--csv", () => string.Empty, "CSV file (RuleId,Status,Reason,Comment)");
    var ruleOpt = new Option<string>("--rule-id", () => string.Empty, "Single: Rule ID");
    var statusOpt = new Option<string>("--status", () => string.Empty, "Single: Pass|Fail|NotApplicable|Open");
    var reasonOpt = new Option<string>("--reason", () => string.Empty, "Single: Reason");
    var commentOpt = new Option<string>("--comment", () => string.Empty, "Single: Comment");
    cmd.AddOption(bundleOpt); cmd.AddOption(csvOpt); cmd.AddOption(ruleOpt);
    cmd.AddOption(statusOpt); cmd.AddOption(reasonOpt); cmd.AddOption(commentOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var csvPath = ctx.ParseResult.GetValueForOption(csvOpt) ?? string.Empty;
      var ruleId = ctx.ParseResult.GetValueForOption(ruleOpt) ?? string.Empty;
      var status = ctx.ParseResult.GetValueForOption(statusOpt) ?? string.Empty;
      var reason = ctx.ParseResult.GetValueForOption(reasonOpt) ?? string.Empty;
      var comment = ctx.ParseResult.GetValueForOption(commentOpt) ?? string.Empty;

      if (!Directory.Exists(bundle)) { Console.Error.WriteLine("Bundle not found: " + bundle); Environment.ExitCode = 2; return; }
      var svc = new ManualAnswerService();

      if (!string.IsNullOrWhiteSpace(csvPath))
      {
        if (!File.Exists(csvPath)) { Console.Error.WriteLine("CSV not found: " + csvPath); Environment.ExitCode = 3; return; }
        int saved = 0;
        foreach (var line in File.ReadAllLines(csvPath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("RuleId", StringComparison.OrdinalIgnoreCase)) continue;
          var parts = Helpers.ParseCsvLine(line);
          if (parts.Length < 2) continue;
          var csvRuleId = parts[0].Trim(); var csvStatus = parts[1].Trim();
          if (string.IsNullOrWhiteSpace(csvRuleId) || string.IsNullOrWhiteSpace(csvStatus)) continue;
          svc.SaveAnswer(bundle, new ManualAnswer { RuleId = csvRuleId, Status = csvStatus, Reason = parts.Length > 2 ? parts[2].Trim() : null, Comment = parts.Length > 3 ? parts[3].Trim() : null });
          saved++;
        }
        Console.WriteLine($"Saved {saved} manual answers from CSV.");
      }
      else if (!string.IsNullOrWhiteSpace(ruleId) && !string.IsNullOrWhiteSpace(status))
      {
        svc.SaveAnswer(bundle, new ManualAnswer { RuleId = ruleId, Status = status, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason, Comment = string.IsNullOrWhiteSpace(comment) ? null : comment });
        Console.WriteLine($"Saved answer for {ruleId}: {status}");
      }
      else { Console.Error.WriteLine("Provide --csv or --rule-id + --status."); Environment.ExitCode = 2; }
      await Task.CompletedTask;
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterEvidenceSave(RootCommand rootCmd)
  {
    var cmd = new Command("evidence-save", "Save evidence artifact to a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var ruleOpt = new Option<string>("--rule-id", "Rule ID") { IsRequired = true };
    var typeOpt = new Option<string>("--type", () => "Other", "Evidence type");
    var sourceOpt = new Option<string>("--source-file", () => string.Empty, "Source file path");
    var cmdOpt = new Option<string>("--command", () => string.Empty, "Command that produced evidence");
    var textOpt = new Option<string>("--text", () => string.Empty, "Inline text content");
    cmd.AddOption(bundleOpt); cmd.AddOption(ruleOpt); cmd.AddOption(typeOpt);
    cmd.AddOption(sourceOpt); cmd.AddOption(cmdOpt); cmd.AddOption(textOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var ruleId = ctx.ParseResult.GetValueForOption(ruleOpt) ?? string.Empty;
      var typeName = ctx.ParseResult.GetValueForOption(typeOpt) ?? "Other";
      if (!Enum.TryParse<EvidenceArtifactType>(typeName, true, out var artifactType)) artifactType = EvidenceArtifactType.Other;

      var collector = new EvidenceCollector();
      var result = collector.WriteEvidence(new EvidenceWriteRequest
      {
        BundleRoot = bundle, RuleId = ruleId, Type = artifactType, Source = "CLI",
        Command = NullIfEmpty(ctx, cmdOpt), ContentText = NullIfEmpty(ctx, textOpt), SourceFilePath = NullIfEmpty(ctx, sourceOpt)
      });
      Console.WriteLine("Evidence saved:"); Console.WriteLine("  Path: " + result.EvidencePath);
      Console.WriteLine("  Metadata: " + result.MetadataPath); Console.WriteLine("  SHA256: " + result.Sha256);
      await Task.CompletedTask;
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterBundleSummary(RootCommand rootCmd)
  {
    var cmd = new Command("bundle-summary", "Show dashboard summary of a bundle");
    var bundleOpt = new Option<string>("--bundle", "Bundle root path") { IsRequired = true };
    var jsonOpt = new Option<bool>("--json", "Output as JSON");
    cmd.AddOption(bundleOpt); cmd.AddOption(jsonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var bundle = ctx.ParseResult.GetValueForOption(bundleOpt) ?? string.Empty;
      var json = ctx.ParseResult.GetValueForOption(jsonOpt);
      if (!Directory.Exists(bundle)) { Console.Error.WriteLine("Bundle not found: " + bundle); Environment.ExitCode = 2; return; }

      string packName = "unknown", profileName = "unknown";
      var manifestPath = Path.Combine(bundle, "Manifest", "manifest.json");
      if (File.Exists(manifestPath))
        try { using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath)); var run = doc.RootElement.GetProperty("run"); packName = run.GetProperty("packName").GetString() ?? "unknown"; profileName = run.GetProperty("profileName").GetString() ?? "unknown"; }
        catch (Exception ex) { Console.Error.WriteLine("Warning: " + ex.Message); }

      int totalControls = 0, autoControls = 0, manualControls = 0;
      var manualControlsList = new List<ControlRecord>();
      var controlsPath = Path.Combine(bundle, "Manifest", "pack_controls.json");
      if (File.Exists(controlsPath))
        try { var all = JsonSerializer.Deserialize<List<ControlRecord>>(File.ReadAllText(controlsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<ControlRecord>(); totalControls = all.Count; manualControlsList = all.Where(c => c.IsManual).ToList(); manualControls = manualControlsList.Count; autoControls = totalControls - manualControls; }
        catch (Exception ex) { Console.Error.WriteLine("Warning: " + ex.Message); }

      int verifyClosed = 0, verifyOpen = 0, verifyTotal = 0;
      var verifyDir = Path.Combine(bundle, "Verify");
      if (Directory.Exists(verifyDir))
        foreach (var dir in Directory.GetDirectories(verifyDir))
        {
          var rp = Path.Combine(dir, "consolidated-results.json");
          if (!File.Exists(rp)) continue;
          try { var report = VerifyReportReader.LoadFromJson(rp); foreach (var r in report.Results) { verifyTotal++; var s = (r.Status ?? "").ToLowerInvariant(); if (s == "notafinding" || s == "pass" || s == "not_applicable") verifyClosed++; else verifyOpen++; } }
          catch (Exception ex) { Console.Error.WriteLine("Warning: " + ex.Message); }
        }

      var svc = new ManualAnswerService();
      var stats = svc.GetProgressStats(bundle, manualControlsList);

      if (json)
      {
        Console.WriteLine(JsonSerializer.Serialize(new { pack = packName, profile = profileName, totalControls, autoControls, manualControls, verify = new { closed = verifyClosed, open = verifyOpen, total = verifyTotal }, manual = new { pass = stats.PassCount, fail = stats.FailCount, na = stats.NotApplicableCount, open = stats.UnansweredControls, percentComplete = stats.PercentComplete } }, new JsonSerializerOptions { WriteIndented = true }));
      }
      else
      {
        Console.WriteLine("Bundle: " + bundle); Console.WriteLine($"  Pack: {packName}"); Console.WriteLine($"  Profile: {profileName}"); Console.WriteLine();
        Console.WriteLine($"Controls: {totalControls} total ({autoControls} auto, {manualControls} manual)"); Console.WriteLine();
        if (verifyTotal > 0) Console.WriteLine($"Verify: {verifyClosed} closed / {verifyTotal} total ({verifyClosed * 100.0 / verifyTotal:F0}%)");
        else Console.WriteLine("Verify: no results");
        Console.WriteLine(); Console.WriteLine($"Manual: {stats.AnsweredControls}/{stats.TotalControls} answered ({stats.PercentComplete:F0}%)");
        Console.WriteLine($"  Pass: {stats.PassCount}  Fail: {stats.FailCount}  NA: {stats.NotApplicableCount}  Open: {stats.UnansweredControls}");
      }
      await Task.CompletedTask;
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterOverlayEdit(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("overlay-edit", "Add or remove rule overrides from an overlay");
    var overlayOpt = new Option<string>("--overlay", "Overlay ID") { IsRequired = true };
    var addOpt = new Option<string>("--add-rule", () => string.Empty, "Add override for rule ID");
    var removeOpt = new Option<string>("--remove-rule", () => string.Empty, "Remove override for rule ID");
    var statusOpt = new Option<string>("--status", () => "NotApplicable", "Override status");
    var reasonOpt = new Option<string>("--reason", () => string.Empty, "NA reason or notes");
    cmd.AddOption(overlayOpt); cmd.AddOption(addOpt); cmd.AddOption(removeOpt); cmd.AddOption(statusOpt); cmd.AddOption(reasonOpt);

    cmd.SetHandler(async (InvocationContext ctx) =>
    {
      var overlayId = ctx.ParseResult.GetValueForOption(overlayOpt) ?? string.Empty;
      var addRule = ctx.ParseResult.GetValueForOption(addOpt) ?? string.Empty;
      var removeRule = ctx.ParseResult.GetValueForOption(removeOpt) ?? string.Empty;
      var statusStr = ctx.ParseResult.GetValueForOption(statusOpt) ?? "NotApplicable";
      var reason = ctx.ParseResult.GetValueForOption(reasonOpt) ?? string.Empty;

      using var host = buildHost();
      await host.StartAsync();
      var repo = host.Services.GetRequiredService<IOverlayRepository>();
      var overlay = await repo.GetAsync(overlayId, CancellationToken.None);
      if (overlay == null) { Console.Error.WriteLine("Overlay not found: " + overlayId); Environment.ExitCode = 2; await host.StopAsync(); return; }

      var overrides = overlay.Overrides.ToList();
      if (!string.IsNullOrWhiteSpace(addRule))
      {
        if (!Enum.TryParse<ControlStatus>(statusStr, true, out var ps)) ps = ControlStatus.NotApplicable;
        var existing = overrides.FirstOrDefault(o => string.Equals(o.RuleId, addRule, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { existing.StatusOverride = ps; existing.NaReason = string.IsNullOrWhiteSpace(reason) ? existing.NaReason : reason; Console.WriteLine($"Updated override for {addRule}: {ps}"); }
        else { overrides.Add(new ControlOverride { RuleId = addRule, StatusOverride = ps, NaReason = string.IsNullOrWhiteSpace(reason) ? null : reason }); Console.WriteLine($"Added override for {addRule}: {ps}"); }
      }
      else if (!string.IsNullOrWhiteSpace(removeRule))
      {
        var removed = overrides.RemoveAll(o => string.Equals(o.RuleId, removeRule, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(removed > 0 ? $"Removed override for {removeRule}" : $"No override found for {removeRule}");
      }
      else { Console.Error.WriteLine("Provide --add-rule or --remove-rule."); Environment.ExitCode = 2; await host.StopAsync(); return; }

      await repo.SaveAsync(new Overlay { OverlayId = overlay.OverlayId, Name = overlay.Name, UpdatedAt = DateTimeOffset.Now, Overrides = overrides, PowerStigOverrides = overlay.PowerStigOverrides.ToList() }, CancellationToken.None);
      Console.WriteLine($"Overlay {overlayId} saved ({overrides.Count} overrides).");
      await host.StopAsync();
    });

    rootCmd.AddCommand(cmd);
  }

  private static void RegisterSupportBundle(RootCommand rootCmd, Func<IHost> buildHost)
  {
    var cmd = new Command("support-bundle", "Collect logs and diagnostics into a support zip package");
    var outputOpt = new Option<string>("--output", () => Environment.CurrentDirectory, "Output directory for support bundle artifacts");
    var bundleOpt = new Option<string>("--bundle", () => string.Empty, "Optional bundle root to include diagnostics from");
    var includeDbOpt = new Option<bool>("--include-db", "Include .stigforge/data/stigforge.db in support bundle");
    var maxLogsOpt = new Option<int>("--max-log-files", () => 20, "Maximum number of recent log files to include");
    cmd.AddOption(outputOpt);
    cmd.AddOption(bundleOpt);
    cmd.AddOption(includeDbOpt);
    cmd.AddOption(maxLogsOpt);

    cmd.SetHandler(async (output, bundle, includeDb, maxLogFiles) =>
    {
      using var host = buildHost();
      await host.StartAsync();

      var paths = host.Services.GetRequiredService<IPathBuilder>();
      var builder = new SupportBundleBuilder();
      var result = builder.Create(new SupportBundleRequest
      {
        OutputDirectory = output,
        AppDataRoot = paths.GetAppDataRoot(),
        BundleRoot = string.IsNullOrWhiteSpace(bundle) ? null : bundle,
        IncludeDatabase = includeDb,
        MaxLogFiles = maxLogFiles
      });

      Console.WriteLine("Support bundle created:");
      Console.WriteLine("  Zip: " + result.BundleZipPath);
      Console.WriteLine("  Manifest: " + result.ManifestPath);
      Console.WriteLine("  Files: " + result.FileCount);

      if (result.Warnings.Count > 0)
      {
        Console.WriteLine("Warnings:");
        foreach (var warning in result.Warnings) Console.WriteLine("  - " + warning);
      }

      await host.StopAsync();
    }, outputOpt, bundleOpt, includeDbOpt, maxLogsOpt);

    rootCmd.AddCommand(cmd);
  }

  private static string? NullIfEmpty(InvocationContext ctx, Option<string> opt)
  {
    var val = ctx.ParseResult.GetValueForOption(opt);
    return string.IsNullOrWhiteSpace(val) ? null : val;
  }
}
