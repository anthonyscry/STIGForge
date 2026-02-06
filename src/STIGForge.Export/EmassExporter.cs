using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Export;

public sealed class EmassExporter
{
  private readonly IPathBuilder _paths;
  private readonly IHashingService _hash;

  public EmassExporter(IPathBuilder paths, IHashingService hash)
  {
    _paths = paths;
    _hash = hash;
  }

  public async Task<ExportResult> ExportAsync(ExportRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var bundleRoot = request.BundleRoot.Trim();
    if (!Directory.Exists(bundleRoot))
      throw new DirectoryNotFoundException("Bundle root not found: " + bundleRoot);

    var bundleManifest = ReadBundleManifest(bundleRoot);

    var exportRoot = string.IsNullOrWhiteSpace(request.OutputRoot)
      ? _paths.GetEmassExportRoot(
          bundleManifest.Run.SystemName,
          bundleManifest.Run.OsTarget.ToString(),
          bundleManifest.Run.RoleTemplate.ToString(),
          bundleManifest.Run.ProfileName,
          bundleManifest.Run.PackName,
          DateTimeOffset.Now)
      : request.OutputRoot!;

    Directory.CreateDirectory(exportRoot);

    var manifestDir = Path.Combine(exportRoot, "00_Manifest");
    var scansDir = Path.Combine(exportRoot, "01_Scans");
    var checklistsDir = Path.Combine(exportRoot, "02_Checklists");
    var poamDir = Path.Combine(exportRoot, "03_POAM");
    var evidenceDir = Path.Combine(exportRoot, "04_Evidence");
    var attestDir = Path.Combine(exportRoot, "05_Attestations");
    var indexDir = Path.Combine(exportRoot, "06_Index");

    Directory.CreateDirectory(manifestDir);
    Directory.CreateDirectory(scansDir);
    Directory.CreateDirectory(checklistsDir);
    Directory.CreateDirectory(poamDir);
    Directory.CreateDirectory(evidenceDir);
    Directory.CreateDirectory(attestDir);
    Directory.CreateDirectory(indexDir);

    CopyScans(bundleRoot, scansDir);
    CopyChecklists(bundleRoot, checklistsDir);
    CopyEvidence(bundleRoot, evidenceDir);

    var consolidated = LoadConsolidatedResults(bundleRoot);
    var answers = LoadManualAnswers(bundleRoot);
    MergeManualAnswers(consolidated, answers);
    
    // Generate POA&M using PoamGenerator
    var normalizedResults = ConvertToNormalizedResults(consolidated);
    var poamPackage = PoamGenerator.GeneratePoam(
      normalizedResults,
      bundleManifest.Run.SystemName,
      bundleManifest.BundleId);
    PoamGenerator.WritePoamFiles(poamPackage, poamDir);
    
    // Generate attestations using AttestationGenerator
    var manualControlIds = normalizedResults
      .Where(r => r.Status == VerifyStatus.NotReviewed)
      .Select(r => r.ControlId)
      .Distinct()
      .ToList();
    var attestationPackage = AttestationGenerator.GenerateAttestations(
      manualControlIds,
      bundleManifest.Run.SystemName,
      bundleManifest.BundleId);
    AttestationGenerator.WriteAttestationFiles(attestationPackage, attestDir);

    var naMap = LoadNaScopeReport(bundleRoot);
    var indexPath = Path.Combine(indexDir, "control_evidence_index.csv");
    WriteControlEvidenceIndex(indexPath, consolidated, evidenceDir, scansDir, naMap);

    var naReportSrc = Path.Combine(bundleRoot, "Reports", "na_scope_filter_report.csv");
    if (File.Exists(naReportSrc))
      File.Copy(naReportSrc, Path.Combine(indexDir, "na_scope_filter_report.csv"), true);

    WriteIndexHtml(indexDir, consolidated);

    var manifestPath = Path.Combine(manifestDir, "manifest.json");
    WriteManifest(manifestPath, bundleManifest, consolidated);

    var logPath = Path.Combine(manifestDir, "export_log.txt");
    File.WriteAllText(logPath, "Exported: " + DateTimeOffset.Now.ToString("o"), Encoding.UTF8);

    var hashPath = Path.Combine(manifestDir, "file_hashes.sha256");
    await WriteHashManifestAsync(exportRoot, hashPath, ct);

    var readmePath = Path.Combine(exportRoot, "README_Submission.txt");
    WriteReadme(readmePath);

    // Validate package integrity
    var validator = new EmassPackageValidator();
    var validationResult = validator.ValidatePackage(exportRoot);

    return new ExportResult
    {
      OutputRoot = exportRoot,
      ManifestPath = manifestPath,
      IndexPath = indexPath,
      ValidationResult = validationResult
    };
  }

  private static void CopyScans(string bundleRoot, string scansDir)
  {
    var src = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(src)) return;

    var raw = Path.Combine(scansDir, "raw");
    Directory.CreateDirectory(raw);
    CopyDirectory(src, raw);
  }

  private static void CopyChecklists(string bundleRoot, string checklistsDir)
  {
    var verify = Path.Combine(bundleRoot, "Verify");
    if (Directory.Exists(verify))
    {
      foreach (var ckl in Directory.GetFiles(verify, "*.ckl", SearchOption.AllDirectories))
      {
        var dest = Path.Combine(checklistsDir, Path.GetFileName(ckl));
        File.Copy(ckl, dest, true);
      }
    }

    var manual = Path.Combine(bundleRoot, "Manual");
    if (Directory.Exists(manual))
    {
      var dest = Path.Combine(checklistsDir, "AnswerFiles");
      Directory.CreateDirectory(dest);
      CopyDirectory(manual, dest);
    }
  }

  private static void CopyEvidence(string bundleRoot, string evidenceDir)
  {
    var src = Path.Combine(bundleRoot, "Evidence");
    if (!Directory.Exists(src)) return;

    CopyDirectory(src, evidenceDir);
  }



  private static List<ControlResult> LoadConsolidatedResults(string bundleRoot)
  {
    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot)) return new List<ControlResult>();

    var reports = Directory.GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories);
    var all = new List<ControlResult>();
    foreach (var reportPath in reports)
    {
      var report = VerifyReportReader.LoadFromJson(reportPath);
      all.AddRange(report.Results);
    }

    return all;
  }

  private static IReadOnlyList<ManualAnswer> LoadManualAnswers(string bundleRoot)
  {
    var path = Path.Combine(bundleRoot, "Manual", "answers.json");
    if (!File.Exists(path)) return Array.Empty<ManualAnswer>();

    var json = File.ReadAllText(path);
    var file = JsonSerializer.Deserialize<AnswerFile>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (file == null) return Array.Empty<ManualAnswer>();
    return file.Answers;
  }

  private static List<NormalizedVerifyResult> ConvertToNormalizedResults(List<ControlResult> results)
  {
    return results.Select(r => new NormalizedVerifyResult
    {
      ControlId = r.VulnId ?? r.RuleId ?? string.Empty,
      VulnId = r.VulnId,
      RuleId = r.RuleId,
      Title = r.Title,
      Severity = r.Severity,
      Status = MapStatusToVerifyStatus(r.Status),
      Comments = r.Comments,
      FindingDetails = r.Comments,
      Tool = r.Tool,
      SourceFile = r.SourceFile,
      VerifiedAt = r.VerifiedAt,
      Metadata = new Dictionary<string, string>()
    }).ToList();
  }

  private static VerifyStatus MapStatusToVerifyStatus(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return VerifyStatus.NotReviewed;

    var s = status.Trim().ToLowerInvariant();
    if (s.Contains("pass") || s.Contains("notafinding"))
      return VerifyStatus.Pass;
    if (s.Contains("fail") || s.Contains("open"))
      return VerifyStatus.Fail;
    if (s.Contains("not_applicable") || s.Contains("not applicable") || s == "na")
      return VerifyStatus.NotApplicable;
    if (s.Contains("not_reviewed") || s.Contains("not reviewed"))
      return VerifyStatus.NotReviewed;
    if (s.Contains("error"))
      return VerifyStatus.Error;

    return VerifyStatus.NotReviewed;
  }

  private static void MergeManualAnswers(List<ControlResult> results, IReadOnlyList<ManualAnswer> answers)
  {
    if (answers.Count == 0) return;

    foreach (var ans in answers)
    {
      var match = results.FirstOrDefault(r =>
        (!string.IsNullOrWhiteSpace(ans.RuleId) && string.Equals(ans.RuleId, r.RuleId, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(ans.VulnId) && string.Equals(ans.VulnId, r.VulnId, StringComparison.OrdinalIgnoreCase)));

      if (match == null)
      {
        results.Add(new ControlResult
        {
          RuleId = ans.RuleId,
          VulnId = ans.VulnId,
          Title = string.Empty,
          Severity = string.Empty,
          Status = ans.Status,
          Comments = ans.Comment,
          Tool = "Manual",
          SourceFile = "Manual/answers.json",
          VerifiedAt = ans.UpdatedAt
        });
      }
      else
      {
        match.Status = ans.Status;
        match.Comments = ans.Comment;
        match.Tool = "Manual";
        match.VerifiedAt = ans.UpdatedAt;
      }
    }
  }



  private static void WriteControlEvidenceIndex(
    string outputPath,
    IReadOnlyList<ControlResult> results,
    string evidenceDir,
    string scansDir,
    IReadOnlyDictionary<string, string> naMap)
  {
    var grouped = results.GroupBy(r => GetControlKey(r), StringComparer.OrdinalIgnoreCase);
    var sb = new StringBuilder(4096);
    sb.AppendLine("VulnId,RuleId,Title,Severity,Status,NaReason,NaOrigin,EvidencePaths,ScanSources,LastVerified");

    foreach (var g in grouped)
    {
      var sample = g.First();
      var status = ResolveStatus(g.Select(x => x.Status));
      var naReason = ResolveNaReason(sample, naMap);
      var naOrigin = string.IsNullOrWhiteSpace(naReason) ? string.Empty : "classification_scope_filter";

      var evidencePaths = ResolveEvidencePaths(evidenceDir, sample);
      var scanPaths = string.Join(";", g.Select(x => RelOrFull(scansDir, x.SourceFile)).Distinct());
      var lastVerified = g.Select(x => x.VerifiedAt).Where(x => x.HasValue).Select(x => x!.Value).OrderByDescending(x => x).FirstOrDefault();

      sb.AppendLine(string.Join(",",
        Csv(sample.VulnId),
        Csv(sample.RuleId),
        Csv(sample.Title),
        Csv(sample.Severity),
        Csv(status),
        Csv(naReason),
        Csv(naOrigin),
        Csv(evidencePaths),
        Csv(scanPaths),
        Csv(lastVerified == default ? string.Empty : lastVerified.ToString("o"))));
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteIndexHtml(string indexDir, IReadOnlyList<ControlResult> results)
  {
    var total = results.Count;
    var open = results.Count(r => IsOpen(r.Status));
    var closed = total - open;

    var html = new StringBuilder(1024);
    html.AppendLine("<html><head><title>STIGForge eMASS Index</title></head><body>");
    html.AppendLine("<h1>STIGForge eMASS Submission</h1>");
    html.AppendLine("<p>Total controls: " + total + "</p>");
    html.AppendLine("<p>Closed: " + closed + "</p>");
    html.AppendLine("<p>Open: " + open + "</p>");
    html.AppendLine("<ul>");
    html.AppendLine("<li><a href='control_evidence_index.csv'>control_evidence_index.csv</a></li>");
    html.AppendLine("<li><a href='na_scope_filter_report.csv'>na_scope_filter_report.csv</a></li>");
    html.AppendLine("</ul>");
    html.AppendLine("</body></html>");

    File.WriteAllText(Path.Combine(indexDir, "index.html"), html.ToString(), Encoding.UTF8);
  }

  private static void WriteManifest(string path, BundleManifestDto bundle, IReadOnlyList<ControlResult> results)
  {
    var manifest = new
    {
      exportId = Guid.NewGuid().ToString("n"),
      createdAt = DateTimeOffset.Now,
      bundleId = bundle.BundleId,
      systemName = bundle.Run.SystemName,
      os = bundle.Run.OsTarget.ToString(),
      role = bundle.Run.RoleTemplate.ToString(),
      profile = bundle.Run.ProfileName,
      pack = bundle.Run.PackName,
      totalControls = results.Count
    };

    var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, Encoding.UTF8);
  }

  private static void WriteReadme(string path)
  {
    var text = new StringBuilder(512);
    text.AppendLine("STIGForge eMASS Submission Package");
    text.AppendLine("00_Manifest: export manifest + hashes + logs");
    text.AppendLine("01_Scans: raw and consolidated scan outputs");
    text.AppendLine("02_Checklists: CKL files and answer files");
    text.AppendLine("03_POAM: POA&M exports");
    text.AppendLine("04_Evidence: evidence files by control");
    text.AppendLine("05_Attestations: attestation records");
    text.AppendLine("06_Index: indices and NA scope report");
    File.WriteAllText(path, text.ToString(), Encoding.UTF8);
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

  private static BundleManifestDto ReadBundleManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      throw new FileNotFoundException("Bundle manifest not found", manifestPath);

    var json = File.ReadAllText(manifestPath);
    var manifest = JsonSerializer.Deserialize<BundleManifestDto>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (manifest == null)
      throw new InvalidOperationException("Invalid bundle manifest.");

    return manifest;
  }

  private static IReadOnlyDictionary<string, string> LoadNaScopeReport(string bundleRoot)
  {
    var path = Path.Combine(bundleRoot, "Reports", "na_scope_filter_report.csv");
    if (!File.Exists(path)) return new Dictionary<string, string>();

    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var lines = File.ReadAllLines(path).Skip(1);
    foreach (var line in lines)
    {
      var parts = ParseCsvLine(line);
      if (parts.Length < 6) continue;
      var vulnId = parts[0];
      var ruleId = parts[1];
      var reason = parts[5];

      if (!string.IsNullOrWhiteSpace(ruleId) && !map.ContainsKey("RULE:" + ruleId))
        map["RULE:" + ruleId] = reason;
      if (!string.IsNullOrWhiteSpace(vulnId) && !map.ContainsKey("VULN:" + vulnId))
        map["VULN:" + vulnId] = reason;
    }

    return map;
  }

  private static string ResolveStatus(IEnumerable<string?> statuses)
  {
    var list = statuses.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().ToLowerInvariant()).ToList();
    if (list.Any(s => s.Contains("open") || s.Contains("fail"))) return "Fail";
    if (list.Any(s => s.Contains("not_reviewed") || s.Contains("not reviewed"))) return "Open";
    if (list.Any(s => s.Contains("not_applicable") || s.Contains("not applicable"))) return "NA";
    if (list.Any(s => s.Contains("notafinding") || s.Contains("pass"))) return "Pass";
    return "Open";
  }

  private static bool IsOpen(string? status)
  {
    var s = (status ?? string.Empty).Trim().ToLowerInvariant();
    if (s.Contains("open") || s.Contains("fail")) return true;
    if (s.Contains("not_reviewed") || s.Contains("not reviewed")) return true;
    return false;
  }

  private static string ResolveNaReason(ControlResult sample, IReadOnlyDictionary<string, string> naMap)
  {
    var key = GetControlKey(sample);
    return naMap.TryGetValue(key, out var reason) ? reason : string.Empty;
  }

  private static string GetControlKey(ControlResult r)
  {
    if (!string.IsNullOrWhiteSpace(r.RuleId)) return "RULE:" + r.RuleId!.Trim();
    if (!string.IsNullOrWhiteSpace(r.VulnId)) return "VULN:" + r.VulnId!.Trim();
    return "TITLE:" + (r.Title ?? string.Empty).Trim();
  }

  private static string ResolveEvidencePaths(string evidenceDir, ControlResult sample)
  {
    if (!Directory.Exists(evidenceDir)) return string.Empty;

    var candidates = new List<string>();
    if (!string.IsNullOrWhiteSpace(sample.RuleId))
      candidates.Add(sample.RuleId!.Trim());
    if (!string.IsNullOrWhiteSpace(sample.VulnId))
      candidates.Add(sample.VulnId!.Trim());

    foreach (var c in candidates)
    {
      var dir = Path.Combine(evidenceDir, "by_control", c);
      if (Directory.Exists(dir))
      {
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
          .Select(p => RelOrFull(evidenceDir, p))
          .ToList();
        return string.Join(";", files);
      }
    }

    return string.Empty;
  }

  private static string RelOrFull(string root, string path)
  {
    if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
      return GetRelativePath(root, path);
    return path;
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

  private static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new StringBuilder();
    bool inQuotes = false;
    for (int i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          sb.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        list.Add(sb.ToString());
        sb.Clear();
      }
      else
      {
        sb.Append(ch);
      }
    }
    list.Add(sb.ToString());
    return list.ToArray();
  }

  private sealed class BundleManifestDto
  {
    public string BundleId { get; set; } = string.Empty;
    public RunManifest Run { get; set; } = new();
  }
}
