using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Build;

public sealed class BundleBuilder
{
  private readonly IPathBuilder _paths;
  private readonly IHashingService _hash;
  private readonly IClassificationScopeService _scope;
  private readonly STIGForge.Core.Services.ReleaseAgeGate _releaseGate;

  public BundleBuilder(IPathBuilder paths, IHashingService hash, IClassificationScopeService scope, STIGForge.Core.Services.ReleaseAgeGate releaseGate)
  {
    _paths = paths;
    _hash = hash;
    _scope = scope;
    _releaseGate = releaseGate;
  }

  public async Task<BundleBuildResult> BuildAsync(BundleBuildRequest request, CancellationToken ct)
  {
    var bundleId = string.IsNullOrWhiteSpace(request.BundleId)
      ? Guid.NewGuid().ToString("n")
      : request.BundleId.Trim();

    var root = string.IsNullOrWhiteSpace(request.OutputRoot)
      ? _paths.GetBundleRoot(bundleId)
      : request.OutputRoot!;

    Directory.CreateDirectory(root);

    var applyDir = Path.Combine(root, "Apply");
    var verifyDir = Path.Combine(root, "Verify");
    var manualDir = Path.Combine(root, "Manual");
    var evidenceDir = Path.Combine(root, "Evidence");
    var reportsDir = Path.Combine(root, "Reports");
    var manifestDir = Path.Combine(root, "Manifest");

    Directory.CreateDirectory(applyDir);
    Directory.CreateDirectory(verifyDir);
    Directory.CreateDirectory(manualDir);
    Directory.CreateDirectory(evidenceDir);
    Directory.CreateDirectory(reportsDir);
    Directory.CreateDirectory(manifestDir);

    CopyApplyTemplates(applyDir);

    var compiled = _scope.Compile(request.Profile, request.Controls);
    var reviewQueue = compiled.ReviewQueue.ToList();
    if (!request.ForceAutoApply && !_releaseGate.ShouldAutoApply(request.Profile, request.Pack))
      reviewQueue.AddRange(compiled.Controls.Where(c => c.Status == ControlStatus.Open));

    WriteNaScopeReport(Path.Combine(reportsDir, "na_scope_filter_report.csv"), compiled.Controls);
    WriteReviewQueue(Path.Combine(reportsDir, "review_required.csv"), reviewQueue);

    var automationNote = new
    {
      forceAutoApply = request.ForceAutoApply,
      releaseDate = request.Pack.ReleaseDate,
      graceDays = request.Profile.AutomationPolicy.NewRuleGraceDays,
      autoApplyAllowed = request.ForceAutoApply || _releaseGate.ShouldAutoApply(request.Profile, request.Pack)
    };
    File.WriteAllText(Path.Combine(reportsDir, "automation_gate.json"),
      JsonSerializer.Serialize(automationNote, new JsonSerializerOptions { WriteIndented = true }),
      Encoding.UTF8);

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
      Timestamp = DateTimeOffset.Now,
      ToolVersion = request.ToolVersion
    };

    var manifest = new BundleManifest
    {
      BundleId = bundleId,
      BundleRoot = root,
      Run = run,
      Pack = request.Pack,
      Profile = request.Profile,
      TotalControls = request.Controls.Count,
      AutoNaCount = compiled.Controls.Count(c => c.Status == ControlStatus.NotApplicable),
      ReviewQueueCount = compiled.ReviewQueue.Count
    };

    var manifestPath = Path.Combine(manifestDir, "manifest.json");
    var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

    var controlsPath = Path.Combine(manifestDir, "pack_controls.json");
    var controlsJson = JsonSerializer.Serialize(request.Controls, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(controlsPath, controlsJson, Encoding.UTF8);

    var overlaysPath = Path.Combine(manifestDir, "overlays.json");
    var overlaysJson = JsonSerializer.Serialize(request.Overlays, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(overlaysPath, overlaysJson, Encoding.UTF8);

    var runLogPath = Path.Combine(manifestDir, "run_log.txt");
    File.WriteAllText(runLogPath, "Bundle created: " + DateTimeOffset.Now.ToString("o"), Encoding.UTF8);

    await WriteHashManifestAsync(root, Path.Combine(manifestDir, "file_hashes.sha256"), ct);

    return new BundleBuildResult
    {
      BundleId = bundleId,
      BundleRoot = root,
      ManifestPath = manifestPath
    };
  }

  private static void CopyApplyTemplates(string applyDir)
  {
    var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
    if (repoRoot == null) return;

    var templateRoot = Path.Combine(repoRoot, "tools", "apply");
    if (!Directory.Exists(templateRoot)) return;

    CopyDirectory(templateRoot, applyDir);
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
    foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
      var rel = GetRelativePath(source, dir);
      Directory.CreateDirectory(Path.Combine(dest, rel));
    }

    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
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
      createdAt = DateTimeOffset.Now,
      answers = Array.Empty<object>()
    };

    var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, Encoding.UTF8);
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

  private async Task WriteHashManifestAsync(string root, string outputPath, CancellationToken ct)
  {
    var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
      .Where(p => !string.Equals(p, outputPath, StringComparison.OrdinalIgnoreCase))
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var sb = new StringBuilder(files.Count * 80);
    foreach (var file in files)
    {
      var hash = await _hash.Sha256FileAsync(file, ct);
      var rel = GetRelativePath(root, file);
      sb.AppendLine(hash + "  " + rel);
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }

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
