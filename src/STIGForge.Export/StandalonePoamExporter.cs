using System.Text.Json;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Standalone POA&M exporter for direct CLI usage.
/// Reads verification results from a bundle and generates POA&M files independently
/// of the full eMASS export pipeline.
/// </summary>
public static class StandalonePoamExporter
{
  public static PoamExportResult ExportPoam(PoamExportRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");
    if (!Directory.Exists(request.BundleRoot))
      throw new DirectoryNotFoundException("Bundle root not found: " + request.BundleRoot);

    var results = LoadAndNormalize(request.BundleRoot);
    if (results.Count == 0)
      return new PoamExportResult { OutputDirectory = string.Empty, ItemCount = 0, Message = "No verification results found." };

    var (manifestSystemName, manifestBundleId) = ReadManifest(request.BundleRoot);
    var systemName = request.SystemName ?? manifestSystemName ?? "Unknown";
    var bundleId = manifestBundleId ?? "unknown";

    var package = PoamGenerator.GeneratePoam(results, systemName, bundleId);

    string outputDir = string.IsNullOrWhiteSpace(request.OutputDirectory)
      ? Path.Combine(request.BundleRoot, "Export", "POAM")
      : request.OutputDirectory!;

    PoamGenerator.WritePoamFiles(package, outputDir);

    return new PoamExportResult
    {
      OutputDirectory = outputDir,
      ItemCount = package.Items.Count,
      CriticalCount = package.Summary.CriticalFindings,
      HighCount = package.Summary.HighFindings,
      MediumCount = package.Summary.MediumFindings,
      Message = "POA&M exported successfully."
    };
  }

  internal static List<NormalizedVerifyResult> LoadAndNormalize(string bundleRoot)
  {
    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot)) return new List<NormalizedVerifyResult>();

    var reports = Directory.EnumerateFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories);
    var all = new List<NormalizedVerifyResult>();
    foreach (var reportPath in reports)
    {
      var report = VerifyReportReader.LoadFromJson(reportPath);
      foreach (var r in report.Results)
      {
        all.Add(new NormalizedVerifyResult
        {
          ControlId = r.VulnId ?? r.RuleId ?? string.Empty,
          VulnId = r.VulnId,
          RuleId = r.RuleId,
          Title = r.Title,
          Severity = r.Severity,
          Status = ExportStatusMapper.MapToVerifyStatus(r.Status),
          Comments = r.Comments,
          FindingDetails = r.FindingDetails ?? r.Comments,
          Tool = r.Tool,
          SourceFile = r.SourceFile,
          VerifiedAt = r.VerifiedAt,
          Metadata = new Dictionary<string, string>()
        });
      }
    }
    return all;
  }

  internal static (string? SystemName, string? BundleId) ReadManifest(string bundleRoot)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath)) return (null, null);
    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
      string? systemName = null;
      string? bundleId = null;
      if (doc.RootElement.TryGetProperty("run", out var run) && run.TryGetProperty("systemName", out var sn))
        systemName = sn.GetString();
      if (doc.RootElement.TryGetProperty("bundleId", out var bi))
        bundleId = bi.GetString();
      return (systemName, bundleId);
    }
    catch (Exception ex)
    {
      System.Diagnostics.Trace.TraceWarning("POAM manifest read failed: " + ex.Message);
      return (null, null);
    }
  }
}

public sealed class PoamExportRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? OutputDirectory { get; set; }
  public string? SystemName { get; set; }
}

public sealed class PoamExportResult
{
  public string OutputDirectory { get; set; } = string.Empty;
  public int ItemCount { get; set; }
  public int CriticalCount { get; set; }
  public int HighCount { get; set; }
  public int MediumCount { get; set; }
  public string Message { get; set; } = string.Empty;
}
