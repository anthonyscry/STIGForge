using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Constants;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Export;

public sealed class EmassExporter
{
  private readonly IPathBuilder _paths;
  private readonly IHashingService _hash;
  private readonly IAuditTrailService? _audit;

  public EmassExporter(IPathBuilder paths, IHashingService hash, IAuditTrailService? audit = null)
  {
    _paths = paths;
    _hash = hash;
    _audit = audit;
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

    var consolidatedLoad = LoadConsolidatedResults(bundleRoot);
    var consolidated = consolidatedLoad.Results;
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

    var naReportSrc = Path.Combine(bundleRoot, BundlePaths.ReportsDirectory, "na_scope_filter_report.csv");
    if (File.Exists(naReportSrc))
      File.Copy(naReportSrc, Path.Combine(indexDir, "na_scope_filter_report.csv"), true);

    WriteIndexHtml(indexDir, consolidated);

    var manifestPath = Path.Combine(manifestDir, "manifest.json");
    var exportTrace = BuildExportTrace(bundleRoot, consolidatedLoad.SourceReports, consolidated);
    WriteManifest(manifestPath, bundleManifest, consolidated, exportTrace);

    var logPath = Path.Combine(manifestDir, "export_log.txt");
    File.WriteAllText(logPath, "Exported: " + DateTimeOffset.Now.ToString("o"), Encoding.UTF8);

    var hashPath = Path.Combine(manifestDir, "file_hashes.sha256");
    await WriteHashManifestAsync(exportRoot, hashPath, ct).ConfigureAwait(false);

    var readmePath = Path.Combine(exportRoot, "README_Submission.txt");
    WriteReadme(readmePath);

    // Validate package integrity
    var validator = new EmassPackageValidator();
    var validationResult = validator.ValidatePackage(exportRoot);
    var validationReportPath = Path.Combine(manifestDir, "validation_report.txt");
    validator.WriteValidationReport(validationResult, validationReportPath);

    var validationReportJsonPath = Path.Combine(manifestDir, "validation_report.json");
    WriteValidationReportJson(validationReportJsonPath, validationResult);

    var blockingFailures = validationResult.Errors.ToList();
    var warnings = validationResult.Warnings.ToList();

    if (_audit != null)
    {
      try
      {
        await _audit.RecordAsync(new AuditEntry
        {
          Action = "export-emass",
          Target = bundleRoot,
          Result = validationResult.IsValid ? "success" : "failure",
          Detail = $"ExportRoot={exportRoot}, Controls={consolidated.Count}, Valid={validationResult.IsValid}, Errors={validationResult.Errors.Count}, Warnings={validationResult.Warnings.Count}",
          User = Environment.UserName,
          Machine = Environment.MachineName,
          Timestamp = DateTimeOffset.Now
        }, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        warnings.Add("Failed to record export audit entry: " + ex.Message);
      }
    }

    return new ExportResult
    {
      OutputRoot = exportRoot,
      ManifestPath = manifestPath,
      IndexPath = indexPath,
      ValidationReportPath = validationReportPath,
      ValidationReportJsonPath = validationReportJsonPath,
      ValidationResult = validationResult,
      IsReadyForSubmission = validationResult.IsValid,
      BlockingFailures = blockingFailures,
      Warnings = warnings
    };
  }

  private static void CopyScans(string bundleRoot, string scansDir)
  {
    var src = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
    if (!Directory.Exists(src)) return;

    var raw = Path.Combine(scansDir, "raw");
    Directory.CreateDirectory(raw);
    CopyDirectory(src, raw);
  }

  private static void CopyChecklists(string bundleRoot, string checklistsDir)
  {
    var verify = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
    if (Directory.Exists(verify))
    {
      foreach (var ckl in Directory.GetFiles(verify, "*.ckl", SearchOption.AllDirectories))
      {
        var dest = Path.Combine(checklistsDir, Path.GetFileName(ckl));
        File.Copy(ckl, dest, true);
      }
    }

    var manual = Path.Combine(bundleRoot, BundlePaths.ManualDirectory);
    if (Directory.Exists(manual))
    {
      var dest = Path.Combine(checklistsDir, "AnswerFiles");
      Directory.CreateDirectory(dest);
      CopyDirectory(manual, dest);
    }
  }

  private static void CopyEvidence(string bundleRoot, string evidenceDir)
  {
    var src = Path.Combine(bundleRoot, BundlePaths.EvidenceDirectory);
    if (!Directory.Exists(src)) return;

    CopyDirectory(src, evidenceDir);
  }



  private static ConsolidatedLoadResult LoadConsolidatedResults(string bundleRoot)
  {
    var verifyRoot = Path.Combine(bundleRoot, BundlePaths.VerifyDirectory);
    if (!Directory.Exists(verifyRoot))
    {
      return new ConsolidatedLoadResult
      {
        Results = new List<ControlResult>(),
        SourceReports = new List<string>()
      };
    }

    var reports = Directory.GetFiles(verifyRoot, BundlePaths.ConsolidatedResultsFileName, SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();
    var all = new List<ControlResult>();
    foreach (var reportPath in reports)
    {
      var report = VerifyReportReader.LoadFromJson(reportPath);
      all.AddRange(report.Results);
    }

    // Deduplicate by VulnId/RuleId — keep the latest result per control
    var deduped = new Dictionary<string, ControlResult>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < all.Count; i++)
    {
      var r = all[i];
      var key = !string.IsNullOrWhiteSpace(r.RuleId) ? "RULE:" + r.RuleId
              : !string.IsNullOrWhiteSpace(r.VulnId) ? "VULN:" + r.VulnId
              : "IDX:" + i.ToString();
      deduped[key] = r; // last one wins (most recent tool result)
    }

    return new ConsolidatedLoadResult
    {
      Results = deduped.Values.ToList(),
      SourceReports = reports
    };
  }

  private static IReadOnlyList<ManualAnswer> LoadManualAnswers(string bundleRoot)
  {
    var path = Path.Combine(bundleRoot, BundlePaths.ManualDirectory, BundlePaths.AnswersFileName);
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
      Status = ExportStatusMapper.MapToVerifyStatus(r.Status),
      Comments = r.Comments,
      FindingDetails = r.Comments,
      Tool = r.Tool,
      SourceFile = r.SourceFile,
      VerifiedAt = r.VerifiedAt,
      Metadata = new Dictionary<string, string>()
    }).ToList();
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
    var grouped = results
      .GroupBy(r => GetControlKey(r), StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .ToList();
    var sb = new StringBuilder(4096);
    sb.AppendLine("VulnId,RuleId,Title,Severity,Status,NaReason,NaOrigin,EvidencePaths,ScanSources,LastVerified");

    foreach (var g in grouped)
    {
      var orderedGroup = g
        .OrderBy(x => x.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.VulnId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Title ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(x => x.Tool ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.SourceFile ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.VerifiedAt ?? DateTimeOffset.MinValue)
        .ToList();

      var sample = orderedGroup[0];
      var status = ExportStatusMapper.MapToIndexStatus(orderedGroup.Select(x => x.Status));
      var naReason = ResolveNaReason(sample, naMap);
      var naOrigin = string.IsNullOrWhiteSpace(naReason) ? string.Empty : "classification_scope_filter";

      var evidencePaths = ResolveEvidencePaths(evidenceDir, sample);
      var scanPaths = string.Join(";", orderedGroup
        .Select(x => RelOrFull(scansDir, x.SourceFile))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
      var lastVerified = orderedGroup.Select(x => x.VerifiedAt).Where(x => x.HasValue).Select(x => x!.Value).OrderByDescending(x => x).FirstOrDefault();

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
    var open = results.Count(r => ExportStatusMapper.IsOpenStatus(r.Status));
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

  private static void WriteManifest(string path, BundleManifestDto bundle, IReadOnlyList<ControlResult> results, ExportTrace exportTrace)
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
      totalControls = results.Count,
      exportTrace
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
      .Where(p => !IsValidationReportFile(root, p))
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var sb = new StringBuilder(files.Count * 80);
    foreach (var file in files)
    {
      var hash = await _hash.Sha256FileAsync(file, ct).ConfigureAwait(false);
      var rel = GetRelativePath(root, file);
      sb.AppendLine(hash + "  " + rel);
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteValidationReportJson(string outputPath, ValidationResult validationResult)
  {
    var json = JsonSerializer.Serialize(validationResult, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(outputPath, json, Encoding.UTF8);
  }

  private static BundleManifestDto ReadBundleManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, BundlePaths.ManifestDirectory, "manifest.json");
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
    var path = Path.Combine(bundleRoot, BundlePaths.ReportsDirectory, "na_scope_filter_report.csv");
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

    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in candidates.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
    {
      var dir = Path.Combine(evidenceDir, "by_control", c);
      if (Directory.Exists(dir))
      {
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
          files.Add(RelOrFull(evidenceDir, file));
      }
    }

    return string.Join(";", files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
  }

  private static ExportTrace BuildExportTrace(string bundleRoot, IReadOnlyList<string> sourceReports, IReadOnlyList<ControlResult> consolidated)
  {
    var reportPaths = sourceReports
      .Select(path => RelOrFull(bundleRoot, path))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var toolCounts = consolidated
      .GroupBy(r => string.IsNullOrWhiteSpace(r.Tool) ? "Unknown" : r.Tool.Trim(), StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    var statusTotals = consolidated
      .GroupBy(r => ExportStatusMapper.MapToVerifyStatus(r.Status).ToString(), StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

    return new ExportTrace
    {
      SourceReports = reportPaths,
      ToolCounts = toolCounts,
      StatusTotals = statusTotals,
      ManualOverrideCount = consolidated.Count(r => string.Equals(r.Tool, "Manual", StringComparison.OrdinalIgnoreCase)),
      TotalConsolidatedResults = consolidated.Count
    };
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

  private static bool IsValidationReportFile(string root, string path)
  {
    var rel = GetRelativePath(root, path).Replace('\\', '/');
    return string.Equals(rel, "00_Manifest/validation_report.txt", StringComparison.OrdinalIgnoreCase)
      || string.Equals(rel, "00_Manifest/validation_report.json", StringComparison.OrdinalIgnoreCase);
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

  private sealed class ConsolidatedLoadResult
  {
    public List<ControlResult> Results { get; set; } = new();

    public List<string> SourceReports { get; set; } = new();
  }

  private sealed class ExportTrace
  {
    public IReadOnlyList<string> SourceReports { get; set; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, int> ToolCounts { get; set; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> StatusTotals { get; set; } = new Dictionary<string, int>();

    public int ManualOverrideCount { get; set; }

    public int TotalConsolidatedResults { get; set; }
  }
}
