using System;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Generates STIG Viewer-compatible checklist outputs from verification results.
/// Supports .ckl XML and .cklb bundle output with optional CSV companion.
/// </summary>
public static class CklExporter
{
  /// <summary>
  /// Export verification results to CKL/CKLB format.
  /// </summary>
  public static CklExportResult ExportCkl(CklExportRequest request)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    var bundleRoots = ResolveBundleRoots(request);
    if (bundleRoots.Count == 0)
      throw new ArgumentException("BundleRoot or BundleRoots is required.", nameof(request));

    foreach (var bundleRoot in bundleRoots)
    {
      if (!Directory.Exists(bundleRoot))
        throw new DirectoryNotFoundException("Bundle root not found: " + bundleRoot);
    }

    var resultSets = LoadResults(bundleRoots);
    if (resultSets.Count == 0 || resultSets.Sum(s => s.Results.Count) == 0)
    {
      return new CklExportResult
      {
        OutputPath = string.Empty,
        OutputPaths = Array.Empty<string>(),
        ControlCount = 0,
        Message = "No verification results found."
      };
    }

    var outputDir = string.IsNullOrWhiteSpace(request.OutputDirectory)
      ? Path.Combine(bundleRoots[0], "Export")
      : request.OutputDirectory!.Trim();
    Directory.CreateDirectory(outputDir);

    var fileStem = string.IsNullOrWhiteSpace(request.FileName)
      ? "stigforge_checklist"
      : Path.GetFileNameWithoutExtension(request.FileName);

    var outputs = new List<string>();
    var checklistPath = WriteChecklistFile(
      outputDir,
      fileStem,
      request.FileFormat,
      BuildCklDocument(resultSets, request.HostName, request.HostIp, request.HostMac, request.StigId));
    outputs.Add(checklistPath);

    if (request.IncludeCsv)
    {
      var csvPath = Path.Combine(outputDir, fileStem + ".csv");
      WriteChecklistCsv(csvPath, resultSets);
      outputs.Add(csvPath);
    }

    var controlCount = resultSets.Sum(s => s.Results.Count);
    return new CklExportResult
    {
      OutputPath = outputs[0],
      OutputPaths = outputs,
      ControlCount = controlCount,
      Message = "Checklist export complete."
    };
  }

  private static List<string> ResolveBundleRoots(CklExportRequest request)
  {
    var roots = new List<string>();
    if (request.BundleRoots != null)
    {
      foreach (var root in request.BundleRoots)
      {
        if (!string.IsNullOrWhiteSpace(root))
          roots.Add(root.Trim());
      }
    }

    if (!string.IsNullOrWhiteSpace(request.BundleRoot))
      roots.Add(request.BundleRoot.Trim());

    return roots
      .Where(r => !string.IsNullOrWhiteSpace(r))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static string WriteChecklistFile(string outputDir, string fileStem, CklFileFormat format, XDocument document)
  {
    if (format == CklFileFormat.Cklb)
    {
      var cklbPath = Path.Combine(outputDir, fileStem + ".cklb");
      if (File.Exists(cklbPath))
        File.Delete(cklbPath);

      using var archive = ZipFile.Open(cklbPath, ZipArchiveMode.Create);
      var entry = archive.CreateEntry(fileStem + ".ckl", CompressionLevel.Optimal);
      using var stream = entry.Open();
      document.Save(stream);
      return cklbPath;
    }

    var cklPath = Path.Combine(outputDir, fileStem + ".ckl");
    document.Save(cklPath);
    return cklPath;
  }

  private static XDocument BuildCklDocument(
    IReadOnlyList<BundleChecklistResultSet> resultSets,
    string? hostName,
    string? hostIp,
    string? hostMac,
    string? stigIdOverride)
  {
    var stigsElement = new XElement("STIGS");
    for (var i = 0; i < resultSets.Count; i++)
    {
      var resultSet = resultSets[i];
      var setStigId = !string.IsNullOrWhiteSpace(stigIdOverride)
        ? stigIdOverride!
        : !string.IsNullOrWhiteSpace(resultSet.PackId)
          ? resultSet.PackId
          : "STIGForge_Export_" + (i + 1).ToString("D2");

      var title = !string.IsNullOrWhiteSpace(resultSet.PackName)
        ? resultSet.PackName
        : "STIGForge Exported Checklist";

      stigsElement.Add(new XElement("iSTIG",
        new XElement("STIG_INFO",
          new XElement("SI_DATA",
            new XElement("SID_NAME", "stigid"),
            new XElement("SID_DATA", setStigId)),
          new XElement("SI_DATA",
            new XElement("SID_NAME", "title"),
            new XElement("SID_DATA", title)),
          new XElement("SI_DATA",
            new XElement("SID_NAME", "releaseinfo"),
            new XElement("SID_DATA", "STIGForge " + DateTimeOffset.Now.ToString("yyyy-MM-dd")))),
        BuildVulnElements(resultSet.Results)));
    }

    var checklist = new XElement("CHECKLIST",
      new XElement("ASSET",
        new XElement("ROLE", "None"),
        new XElement("ASSET_TYPE", "Computing"),
        new XElement("HOST_NAME", hostName ?? Environment.MachineName),
        new XElement("HOST_IP", hostIp ?? string.Empty),
        new XElement("HOST_MAC", hostMac ?? string.Empty),
        new XElement("HOST_FQDN", string.Empty),
        new XElement("TARGET_COMMENT", string.Empty),
        new XElement("TECH_AREA", string.Empty),
        new XElement("TARGET_KEY", string.Empty),
        new XElement("WEB_OR_DATABASE", "false"),
        new XElement("WEB_DB_SITE", string.Empty),
        new XElement("WEB_DB_INSTANCE", string.Empty)),
      stigsElement);

    return new XDocument(new XDeclaration("1.0", "UTF-8", null), checklist);
  }

  private static object[] BuildVulnElements(IReadOnlyList<ControlResult> results)
  {
    var elements = new List<object>(results.Count);
    foreach (var r in results)
    {
      var vuln = new XElement("VULN",
        StigData("Vuln_Num", r.VulnId ?? string.Empty),
        StigData("Severity", r.Severity ?? "medium"),
        StigData("Rule_ID", r.RuleId ?? string.Empty),
        StigData("Rule_Title", r.Title ?? string.Empty),
        StigData("Rule_Ver", string.Empty),
        StigData("Vuln_Discuss", string.Empty),
        StigData("IA_Controls", string.Empty),
        StigData("Check_Content", string.Empty),
        StigData("Fix_Text", string.Empty),
        StigData("STIGRef", r.Tool),
        new XElement("STATUS", ExportStatusMapper.MapToCklStatus(r.Status)),
        new XElement("FINDING_DETAILS", r.FindingDetails ?? string.Empty),
        new XElement("COMMENTS", r.Comments ?? string.Empty),
        new XElement("SEVERITY_OVERRIDE", string.Empty),
        new XElement("SEVERITY_JUSTIFICATION", string.Empty));
      elements.Add(vuln);
    }

    return elements.ToArray();
  }

  private static XElement StigData(string attribute, string data)
  {
    return new XElement("STIG_DATA",
      new XElement("VULN_ATTRIBUTE", attribute),
      new XElement("ATTRIBUTE_DATA", data));
  }

  private static List<BundleChecklistResultSet> LoadResults(IReadOnlyList<string> bundleRoots)
  {
    var sets = new List<BundleChecklistResultSet>();
    foreach (var bundleRoot in bundleRoots)
    {
      var set = LoadResultsForBundle(bundleRoot);
      if (set.Results.Count > 0)
        sets.Add(set);
    }

    return sets;
  }

  private static BundleChecklistResultSet LoadResultsForBundle(string bundleRoot)
  {
    var set = new BundleChecklistResultSet
    {
      BundleRoot = bundleRoot
    };

    TryReadBundleManifest(bundleRoot, out var packId, out var packName);
    set.PackId = packId;
    set.PackName = packName;

    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot))
      return set;

    var reports = Directory.GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories)
      .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var dedup = new Dictionary<string, ControlResult>(StringComparer.OrdinalIgnoreCase);
    foreach (var reportPath in reports)
    {
      var report = VerifyReportReader.LoadFromJson(reportPath);
      foreach (var result in report.Results)
      {
        var key = BuildControlKey(result);
        if (dedup.TryGetValue(key, out var existing))
        {
          MergeControlResults(existing, result);
        }
        else
        {
          dedup[key] = result;
        }
      }
    }

    set.Results = dedup.Values
      .OrderBy(r => r.VulnId ?? r.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.RuleId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
      .ToList();
    return set;
  }

  private static string BuildControlKey(ControlResult result)
  {
    if (!string.IsNullOrWhiteSpace(result.VulnId))
      return "VULN:" + result.VulnId;
    if (!string.IsNullOrWhiteSpace(result.RuleId))
      return "RULE:" + result.RuleId;
    return "TITLE:" + (result.Title ?? string.Empty);
  }

  private static void MergeControlResults(ControlResult existing, ControlResult incoming)
  {
    existing.AssetId = PreferValue(existing.AssetId, incoming.AssetId);
    existing.VulnId = PreferValue(existing.VulnId, incoming.VulnId);
    existing.RuleId = PreferValue(existing.RuleId, incoming.RuleId);
    existing.Title = PreferValue(existing.Title, incoming.Title);
    existing.Severity = PreferValue(existing.Severity, incoming.Severity);

    var statusOutcome = ResolveStatusWinner(existing, incoming);
    var winner = statusOutcome == StatusWinner.Incoming ? incoming : existing;
    var loser = statusOutcome == StatusWinner.Incoming ? existing : incoming;

    existing.Status = winner.Status;
    existing.Tool = ChooseMetadataValue(winner.Tool, loser.Tool);
    existing.SourceFile = ChooseMetadataValue(winner.SourceFile, loser.SourceFile);
    existing.VerifiedAt = winner.VerifiedAt ?? loser.VerifiedAt;

    existing.BenchmarkId = PreferValue(existing.BenchmarkId, incoming.BenchmarkId);
    existing.FindingDetails = MergeText(existing.FindingDetails, incoming.FindingDetails);
    existing.Comments = MergeText(existing.Comments, incoming.Comments);
  }

  private static string? PreferValue(string? current, string? candidate)
  {
    if (!string.IsNullOrWhiteSpace(current))
      return current;

    return string.IsNullOrWhiteSpace(candidate) ? current : candidate;
  }

  private enum StatusWinner
  {
    Existing,
    Incoming
  }

  private static StatusWinner ResolveStatusWinner(ControlResult current, ControlResult candidate)
  {
    var currentTimestamp = current.VerifiedAt ?? DateTimeOffset.MinValue;
    var candidateTimestamp = candidate.VerifiedAt ?? DateTimeOffset.MinValue;
    if (candidateTimestamp != currentTimestamp)
      return candidateTimestamp > currentTimestamp ? StatusWinner.Incoming : StatusWinner.Existing;

    var currentPriority = MapStatusPriority(current.Status);
    var candidatePriority = MapStatusPriority(candidate.Status);
    if (candidatePriority != currentPriority)
      return candidatePriority > currentPriority ? StatusWinner.Incoming : StatusWinner.Existing;

    var comparison = string.CompareOrdinal(BuildTieBreakKey(current), BuildTieBreakKey(candidate));
    if (comparison < 0)
      return StatusWinner.Incoming;

    return StatusWinner.Existing;
  }

  private static int MapStatusPriority(string? value)
  {
    return ExportStatusMapper.MapToVerifyStatus(value ?? string.Empty) switch
    {
      VerifyStatus.Fail => 3,
      VerifyStatus.Error => 3,
      VerifyStatus.NotReviewed => 2,
      VerifyStatus.Unknown => 2,
      VerifyStatus.NotApplicable => 1,
      VerifyStatus.Pass => 0,
      VerifyStatus.Informational => 0,
      _ => 0
    };
  }

  private static string? MergeText(string? existing, string? candidate)
  {
    var hasExisting = !string.IsNullOrWhiteSpace(existing);
    var hasCandidate = !string.IsNullOrWhiteSpace(candidate);

    if (!hasExisting)
      return candidate;
    if (!hasCandidate)
      return existing;
    var existingValue = existing!;
    var candidateValue = candidate!;
    if (string.Equals(existingValue, candidateValue, StringComparison.Ordinal))
      return existingValue;
    if (existingValue.Contains(candidateValue, StringComparison.Ordinal))
      return existingValue;

    return existingValue + Environment.NewLine + candidateValue;
  }

  private static string ChooseMetadataValue(string winnerValue, string loserValue)
  {
    if (!string.IsNullOrWhiteSpace(winnerValue))
      return winnerValue;

    return string.IsNullOrWhiteSpace(loserValue) ? string.Empty : loserValue;
  }

  private static string BuildTieBreakKey(ControlResult result)
  {
    var status = result.Status ?? string.Empty;
    var tool = result.Tool ?? string.Empty;
    var sourceFile = result.SourceFile ?? string.Empty;
    return string.Join('|', status, tool, sourceFile);
  }

  private static void TryReadBundleManifest(string bundleRoot, out string packId, out string packName)
  {
    packId = string.Empty;
    packName = string.Empty;

    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return;

    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (!doc.RootElement.TryGetProperty("run", out var run) || run.ValueKind != JsonValueKind.Object)
        return;

      if (run.TryGetProperty("packId", out var packIdElement) && packIdElement.ValueKind == JsonValueKind.String)
        packId = packIdElement.GetString() ?? string.Empty;
      if (run.TryGetProperty("packName", out var packNameElement) && packNameElement.ValueKind == JsonValueKind.String)
        packName = packNameElement.GetString() ?? string.Empty;
    }
    catch
    {
      packId = string.Empty;
      packName = string.Empty;
    }
  }

  private static void WriteChecklistCsv(string path, IReadOnlyList<BundleChecklistResultSet> sets)
  {
    var sb = new StringBuilder();
    sb.AppendLine("PackId,PackName,VulnId,RuleId,Title,Severity,Status,Tool,FindingDetails,Comments");

    foreach (var set in sets)
    {
      foreach (var result in set.Results)
      {
        sb.AppendLine(string.Join(",",
          Csv(set.PackId),
          Csv(set.PackName),
          Csv(result.VulnId),
          Csv(result.RuleId),
          Csv(result.Title),
          Csv(result.Severity),
          Csv(result.Status),
          Csv(result.Tool),
          Csv(result.FindingDetails),
          Csv(result.Comments)));
      }
    }

    File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var safe = value ?? string.Empty;
    if (safe.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      return '"' + safe.Replace("\"", "\"\"") + '"';
    return safe;
  }
}

public enum CklFileFormat
{
  Ckl,
  Cklb
}

public sealed class CklExportRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public IReadOnlyList<string>? BundleRoots { get; set; }
  public string? OutputDirectory { get; set; }
  public string? FileName { get; set; }
  public string? HostName { get; set; }
  public string? HostIp { get; set; }
  public string? HostMac { get; set; }
  public string? StigId { get; set; }
  public CklFileFormat FileFormat { get; set; } = CklFileFormat.Ckl;
  public bool IncludeCsv { get; set; }
}

public sealed class CklExportResult
{
  public string OutputPath { get; set; } = string.Empty;
  public IReadOnlyList<string> OutputPaths { get; set; } = Array.Empty<string>();
  public int ControlCount { get; set; }
  public string Message { get; set; } = string.Empty;
}

internal sealed class BundleChecklistResultSet
{
  public string BundleRoot { get; set; } = string.Empty;
  public string PackId { get; set; } = string.Empty;
  public string PackName { get; set; } = string.Empty;
  public IReadOnlyList<ControlResult> Results { get; set; } = Array.Empty<ControlResult>();
}
