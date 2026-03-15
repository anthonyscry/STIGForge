using System.Text;
using System.Text.Json;
using STIGForge.Core;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Build;

public sealed class BundleBuilder : IBundleBuilder
{
  private readonly IPathBuilder _paths;
  private readonly IHashingService _hash;
  private readonly IClassificationScopeService _scope;
  private readonly STIGForge.Core.Services.ReleaseAgeGate _releaseGate;
  private readonly STIGForge.Core.Services.OverlayConflictDetector _conflictDetector;
  private readonly OverlayMergeService _overlayMerge;
  private readonly STIGForge.Core.Services.CanonicalScapSelector? _scapSelector;

  public BundleBuilder(IPathBuilder paths, IHashingService hash, IClassificationScopeService scope, STIGForge.Core.Services.ReleaseAgeGate releaseGate, STIGForge.Core.Services.OverlayConflictDetector conflictDetector, OverlayMergeService overlayMerge, STIGForge.Core.Services.CanonicalScapSelector? scapSelector = null)
  {
    _paths = paths;
    _hash = hash;
    _scope = scope;
    _releaseGate = releaseGate;
    _conflictDetector = conflictDetector;
    _overlayMerge = overlayMerge;
    _scapSelector = scapSelector;
  }

  public async Task<BundleBuildResult> BuildAsync(BundleBuildRequest request, CancellationToken ct)
  {
    var bundleId = string.IsNullOrWhiteSpace(request.BundleId)
      ? Guid.NewGuid().ToString("n")
      : request.BundleId.Trim();

    var root = string.IsNullOrWhiteSpace(request.OutputRoot)
      ? _paths.GetBundleRoot(bundleId)
      : request.OutputRoot!;

    var directories = CreateBundleDirectories(root);
    var applyDir = directories.ApplyDir;
    var manualDir = directories.ManualDir;
    var reportsDir = directories.ReportsDir;
    var manifestDir = directories.ManifestDir;

    var templatesCopied = CopyApplyTemplates(applyDir);
    ValidateApplyTemplates(applyDir, templatesCopied);

    // Compile controls with classification scope
    var compiled = _scope.Compile(request.Profile, request.Controls);

    // Apply overlay merge with deterministic ordering
    var overlayResult = _overlayMerge.Merge(compiled.Controls, request.Overlays);

    // Build review queue from merged controls, excluding NotApplicable from overrides
    var reviewQueue = overlayResult.MergedControls.ToList();
    if (!request.ForceAutoApply && !_releaseGate.ShouldAutoApply(request.Profile, request.Pack))
      reviewQueue.AddRange(overlayResult.MergedControls.Where(c => c.Status == ControlStatus.Open));

    // Filter out controls that are NotApplicable (including from overlay overrides)
    reviewQueue = reviewQueue.Where(c => c.Status != ControlStatus.NotApplicable).ToList();

    // Write reports
    WriteNaScopeReport(Path.Combine(reportsDir, "na_scope_filter_report.csv"), overlayResult.MergedControls);
    WriteReviewQueue(Path.Combine(reportsDir, "review_required.csv"), reviewQueue);

    var conflictReport = _conflictDetector.DetectConflicts(request.Overlays);
    await WriteOverlayArtifacts(reportsDir, overlayResult, conflictReport, request, ct).ConfigureAwait(false);

    WriteAnswerTemplate(Path.Combine(manualDir, "answerfile.template.json"), request);

    var run = new RunManifest
    {
      RunId = Guid.NewGuid().ToString("n"),
      SystemName = Environment.MachineName,
      OsTarget = request.Profile.OsTarget,
      RoleTemplate = request.Profile.RoleTemplate,
      ProfileId = request.Profile.ProfileId,
      ProfileName = request.Profile.Name,
      PackId = request.Pack.PackId,
      PackName = request.Pack.Name,
      Timestamp = BuildTime.Now,
      ToolVersion = request.ToolVersion
    };

    var manifestPath = await WriteBundleManifest(manifestDir, bundleId, root, run, request, overlayResult, reviewQueue, ct).ConfigureAwait(false);
    var scapMappingManifestPath = await WriteScapMapping(manifestDir, request, ct).ConfigureAwait(false);

    await WriteHashManifestAsync(root, Path.Combine(manifestDir, "file_hashes.sha256"), ct).ConfigureAwait(false);

    return new BundleBuildResult
    {
      BundleId = bundleId,
      BundleRoot = root,
      ManifestPath = manifestPath,
      ScapMappingManifestPath = scapMappingManifestPath
    };
  }

  private static bool CopyApplyTemplates(string applyDir)
  {
    var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
    if (repoRoot == null) return false;

    var templateRoot = Path.Combine(repoRoot, "tools", "apply");
    if (!Directory.Exists(templateRoot)) return false;

    CopyDirectory(templateRoot, applyDir);
    return true;
  }

  private static void ValidateApplyTemplates(string applyDir, bool templatesCopied)
  {
    if (!templatesCopied)
      return; // Template copy is best-effort for non-repo contexts

    if (!Directory.Exists(applyDir))
      throw new InvalidOperationException($"Apply templates are incomplete: Apply directory does not exist at {applyDir}");

    var hasApplyScripts = Directory.EnumerateFiles(applyDir, "*.ps1", SearchOption.AllDirectories).Any();
    if (!hasApplyScripts)
      throw new InvalidOperationException($"Apply templates are incomplete: no apply scripts found in {applyDir}");
  }

  private static string? FindRepoRoot(string start)
  {
    var dir = new DirectoryInfo(start);
    while (dir != null)
    {
      if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
        return dir.FullName;
      dir = dir.Parent;
    }

    return null;
  }

  private static void CopyDirectory(string source, string dest)
  {
    Directory.CreateDirectory(dest);
    foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
      var rel = GetRelativePath(source, dir);
      Directory.CreateDirectory(Path.Combine(dest, rel));
    }

    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
      var rel = GetRelativePath(source, file);
      var target = Path.Combine(dest, rel);
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, true);
    }
  }

  private static void WriteAnswerTemplate(string path, BundleBuildRequest request)
  {
    var template = new
    {
      profileId = request.Profile.ProfileId,
      profileName = request.Profile.Name,
      packId = request.Pack.PackId,
      packName = request.Pack.Name,
      createdAt = BuildTime.Now,
      answers = Array.Empty<object>()
    };

    var json = JsonSerializer.Serialize(template, JsonOptions.Indented);
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  private static BundleDirectories CreateBundleDirectories(string root)
  {
    Directory.CreateDirectory(root);

    var directories = new BundleDirectories(
      Path.Combine(root, "Apply"),
      Path.Combine(root, "Verify"),
      Path.Combine(root, "Manual"),
      Path.Combine(root, "Evidence"),
      Path.Combine(root, "Reports"),
      Path.Combine(root, "Manifest"));

    Directory.CreateDirectory(directories.ApplyDir);
    Directory.CreateDirectory(directories.VerifyDir);
    Directory.CreateDirectory(directories.ManualDir);
    Directory.CreateDirectory(directories.EvidenceDir);
    Directory.CreateDirectory(directories.ReportsDir);
    Directory.CreateDirectory(directories.ManifestDir);

    return directories;
  }

  private async Task WriteOverlayArtifacts(
    string reportsDir,
    OverlayMergeResult overlayResult,
    STIGForge.Core.Services.OverlayConflictReport conflictReport,
    BundleBuildRequest request,
    CancellationToken ct)
  {
    WriteOverlayConflictsCsv(Path.Combine(reportsDir, "overlay_conflicts.csv"), overlayResult.Conflicts);
    WriteOverlayDecisionsJson(Path.Combine(reportsDir, "overlay_decisions.json"), overlayResult.AppliedDecisions);
    WriteOverlayConflictReport(Path.Combine(reportsDir, "overlay_conflict_report.csv"), conflictReport);

    if (conflictReport.HasBlockingConflicts && !request.ForceAutoApply)
    {
      var blockingDetails = string.Join("; ", conflictReport.Conflicts
        .Where(c => c.IsBlockingConflict)
        .Select(c => $"{c.ControlKey} (overlays {c.WinningOverlayId} vs {c.OverriddenOverlayId})"));
      throw new InvalidOperationException($"Overlay conflicts block build: {blockingDetails}");
    }

    var automationNote = new
    {
      forceAutoApply = request.ForceAutoApply,
      releaseDate = request.Pack.ReleaseDate,
      graceDays = request.Profile.AutomationPolicy.NewRuleGraceDays,
      autoApplyAllowed = request.ForceAutoApply || _releaseGate.ShouldAutoApply(request.Profile, request.Pack)
    };
    await File.WriteAllTextAsync(
      Path.Combine(reportsDir, "automation_gate.json"),
      JsonSerializer.Serialize(automationNote, JsonOptions.Indented),
      Encoding.UTF8,
      ct).ConfigureAwait(false);
  }

  private static async Task<string> WriteBundleManifest(
    string manifestDir,
    string bundleId,
    string root,
    RunManifest run,
    BundleBuildRequest request,
    OverlayMergeResult overlayResult,
    IReadOnlyList<CompiledControl> reviewQueue,
    CancellationToken ct)
  {
    var manifest = new BundleManifest
    {
      BundleId = bundleId,
      BundleRoot = root,
      Run = run,
      Pack = request.Pack,
      Profile = request.Profile,
      TotalControls = request.Controls.Count,
      AutoNaCount = overlayResult.MergedControls.Count(c => c.Status == ControlStatus.NotApplicable),
      ReviewQueueCount = reviewQueue.Count
    };

    var manifestPath = Path.Combine(manifestDir, "manifest.json");
    var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions.Indented);
    await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, ct).ConfigureAwait(false);

    var controlsPath = Path.Combine(manifestDir, "pack_controls.json");
    var controlsJson = JsonSerializer.Serialize(request.Controls, JsonOptions.Indented);
    await File.WriteAllTextAsync(controlsPath, controlsJson, Encoding.UTF8, ct).ConfigureAwait(false);

    var overlaysPath = Path.Combine(manifestDir, "overlays.json");
    var overlaysJson = JsonSerializer.Serialize(request.Overlays, JsonOptions.Indented);
    await File.WriteAllTextAsync(overlaysPath, overlaysJson, Encoding.UTF8, ct).ConfigureAwait(false);

    var runLogPath = Path.Combine(manifestDir, "run_log.txt");
    await File.WriteAllTextAsync(runLogPath, "Bundle created: " + BuildTime.Now.ToString("o"), Encoding.UTF8, ct).ConfigureAwait(false);

    return manifestPath;
  }

  private async Task<string?> WriteScapMapping(string manifestDir, BundleBuildRequest request, CancellationToken ct)
  {
    if (_scapSelector == null || request.ScapCandidates == null)
      return null;

    var scapInput = new STIGForge.Core.Services.CanonicalScapSelectionInput
    {
      StigPackId = request.Pack.PackId,
      StigName = request.Pack.Name,
      StigImportedAt = request.Pack.ImportedAt,
      StigBenchmarkIds = request.Controls
        .Select(c => c.ExternalIds.BenchmarkId)
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray()!,
      Candidates = request.ScapCandidates
    };

    var scapManifest = _scapSelector.BuildMappingManifest(scapInput, request.Controls);
    var scapMappingManifestPath = Path.Combine(manifestDir, "scap_mapping_manifest.json");
    var scapManifestJson = JsonSerializer.Serialize(scapManifest, JsonOptions.Indented);
    await File.WriteAllTextAsync(scapMappingManifestPath, scapManifestJson, Encoding.UTF8, ct).ConfigureAwait(false);
    return scapMappingManifestPath;
  }

  private static void WriteNaScopeReport(string path, IReadOnlyList<CompiledControl> controls)
  {
    var sb = new StringBuilder(4096);
    sb.AppendLine("VulnId,RuleId,Title,Scope,Confidence,Reason");

    foreach (var c in controls
      .Where(x => x.Status == ControlStatus.NotApplicable)
      .OrderBy(x => x.Control.ExternalIds.RuleId, StringComparer.OrdinalIgnoreCase))
    {
      var reason = c.Comment ?? "Auto-NA (classification scope)";
      sb.AppendLine(string.Join(",",
        Csv(c.Control.ExternalIds.VulnId),
        Csv(c.Control.ExternalIds.RuleId),
        Csv(c.Control.Title),
        Csv(c.Control.Applicability.ClassificationScope.ToString()),
        Csv(c.Control.Applicability.Confidence.ToString()),
        Csv(reason)));
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteReviewQueue(string path, IReadOnlyList<CompiledControl> reviewQueue)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("VulnId,RuleId,Title,Reason");

    foreach (var c in reviewQueue
      .OrderBy(x => x.Control.ExternalIds.RuleId, StringComparer.OrdinalIgnoreCase))
    {
      sb.AppendLine(string.Join(",",
        Csv(c.Control.ExternalIds.VulnId),
        Csv(c.Control.ExternalIds.RuleId),
        Csv(c.Control.Title),
        Csv(c.ReviewReason)));
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteOverlayConflictsCsv(string path, IReadOnlyList<OverlayConflict> conflicts)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("Key,PreviousOverlayId,PreviousOverlayName,PreviousStatus,CurrentOverlayId,CurrentOverlayName,CurrentStatus,ConflictType");

    foreach (var c in conflicts
      .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => x.Previous.OverlayOrder)
      .ThenBy(x => x.Previous.OverrideOrder))
    {
      var previousStatus = c.Previous.Outcome.StatusOverride?.ToString() ?? "Open";
      var currentStatus = c.Current.Outcome.StatusOverride?.ToString() ?? "Open";
      var conflictType = previousStatus != currentStatus ? "Blocking" : "Non-blocking";

      sb.AppendLine(string.Join(",",
        Csv(c.Key),
        Csv(c.Previous.OverlayId),
        Csv(c.Previous.OverlayName),
        Csv(previousStatus),
        Csv(c.Current.OverlayId),
        Csv(c.Current.OverlayName),
        Csv(currentStatus),
        Csv(conflictType)));
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteOverlayDecisionsJson(string path, IReadOnlyList<OverlayAppliedDecision> decisions)
  {
    var decisionsArray = decisions
      .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
      .ThenBy(d => d.OverlayOrder)
      .ThenBy(d => d.OverrideOrder)
      .Select(d => new
      {
        key = d.Key,
        overlayId = d.OverlayId,
        overlayName = d.OverlayName,
        overlayOrder = d.OverlayOrder,
        overrideOrder = d.OverrideOrder,
        outcome = new
        {
          statusOverride = d.Outcome.StatusOverride?.ToString(),
          naReason = d.Outcome.NaReason,
          notes = d.Outcome.Notes
        }
      })
      .ToArray();

    var json = JsonSerializer.Serialize(decisionsArray, JsonOptions.Indented);
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  private async Task WriteHashManifestAsync(string root, string outputPath, CancellationToken ct)
  {
    var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
      .Where(p => !string.Equals(p, outputPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    // Hash files in parallel for significant speedup on large bundles
    var hashes = new string[files.Count];
    await Parallel.ForEachAsync(
      Enumerable.Range(0, files.Count),
      new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct },
      async (i, token) => { hashes[i] = await _hash.Sha256FileAsync(files[i], token).ConfigureAwait(false); })
      .ConfigureAwait(false);

    var sb = new StringBuilder(files.Count * 80);
    for (var i = 0; i < files.Count; i++)
      sb.AppendLine(hashes[i] + "  " + GetRelativePath(root, files[i]));

    await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
  }

  private static void WriteOverlayConflictReport(string path, STIGForge.Core.Services.OverlayConflictReport report)
  {
    var sb = new StringBuilder(2048);
    sb.AppendLine("ControlKey,WinningOverlayId,OverriddenOverlayId,WinningValue,OverriddenValue,Reason");

    foreach (var c in report.Conflicts
      .OrderBy(x => x.ControlKey, StringComparer.OrdinalIgnoreCase)
      .ThenBy(x => x.OverriddenOverlayId, StringComparer.OrdinalIgnoreCase))
    {
      sb.AppendLine(string.Join(",",
        Csv(c.ControlKey),
        Csv(c.WinningOverlayId),
        Csv(c.OverriddenOverlayId),
        Csv(c.WinningValue),
        Csv(c.OverriddenValue),
        Csv(c.Reason)));
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private sealed record BundleDirectories(
    string ApplyDir,
    string VerifyDir,
    string ManualDir,
    string EvidenceDir,
    string ReportsDir,
    string ManifestDir);

  private static string Csv(string? value) => CsvEscape.Escape(value);

  private static string GetRelativePath(string root, string path)
  {
    var rootUri = new Uri(AppendDirSeparator(root));
    var pathUri = new Uri(path);
    var rel = rootUri.MakeRelativeUri(pathUri).ToString();
    return Uri.UnescapeDataString(rel).Replace('/', Path.DirectorySeparatorChar);
  }

  private static string AppendDirSeparator(string path)
  {
    if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
      return path + Path.DirectorySeparatorChar;
    return path;
  }
}
