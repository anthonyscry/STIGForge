using System.Reflection;
using System.Text;
using System.Text.Json;
using Scriban;
using Scriban.Runtime;
using STIGForge.Core;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Generates self-contained HTML compliance reports from verification results.
/// No JavaScript, no external URLs — fully offline.
/// </summary>
public static class HtmlReportGenerator
{
  private const string TemplateResourceName = "STIGForge.Export.Templates.executive-report.html";

  public static ExecutiveReportData BuildReportData(
    string bundleRoot,
    IReadOnlyList<ComplianceSnapshot>? trendSnapshots = null,
    string? systemNameOverride = null,
    string audience = "executive")
  {
    var results = StandalonePoamExporter.LoadAndNormalize(bundleRoot);
    var systemName = systemNameOverride
      ?? StandalonePoamExporter.ReadSystemName(bundleRoot)
      ?? "Unknown System";
    var bundleId = StandalonePoamExporter.ReadBundleId(bundleRoot) ?? "unknown";

    var passCount = results.Count(r => r.Status == VerifyStatus.Pass);
    var failCount = results.Count(r => r.Status == VerifyStatus.Fail);
    var errorCount = results.Count(r => r.Status == VerifyStatus.Error);
    var naCount = results.Count(r => r.Status == VerifyStatus.NotApplicable);
    var nrCount = results.Count(r => r.Status == VerifyStatus.NotReviewed);
    var evaluated = passCount + failCount + errorCount;
    var compliancePercent = evaluated > 0 ? Math.Round((passCount / (double)evaluated) * 100.0, 2) : 0.0;

    // Severity breakdown
    var severity = BuildSeverityBreakdown(results);

    // Trend points
    var trendData = (trendSnapshots ?? [])
      .OrderBy(s => s.CapturedAt)
      .Select(s => new TrendPoint { CapturedAt = s.CapturedAt, CompliancePercent = s.CompliancePercent })
      .ToList();

    // Open findings (sorted by severity: high first)
    var openFindings = results
      .Where(r => r.Status == VerifyStatus.Fail || r.Status == VerifyStatus.Error)
      .OrderBy(r => SeverityOrder(r.Severity))
      .Select(r => new OpenFinding
      {
        VulnId = r.VulnId ?? r.ControlId,
        Title = r.Title,
        Severity = r.Severity,
        Status = r.Status.ToString(),
        Tool = r.Tool,
        SourceFile = r.SourceFile,
        VerifiedAt = r.VerifiedAt
      })
      .ToList();

    // POA&M age buckets (using VerifiedAt as proxy)
    var now = DateTimeOffset.UtcNow;
    var poamAges = new PoamAgeSummary();
    foreach (var f in openFindings)
    {
      if (!f.VerifiedAt.HasValue) { poamAges.Age0To30++; continue; }
      var days = (now - f.VerifiedAt.Value).TotalDays;
      if (days <= 30) poamAges.Age0To30++;
      else if (days <= 90) poamAges.Age31To90++;
      else poamAges.Age91Plus++;
    }

    // Per-STIG breakdown
    var stigBreakdowns = results
      .Where(r => !string.IsNullOrEmpty(r.BenchmarkId))
      .GroupBy(r => r.BenchmarkId!)
      .Select(g =>
      {
        var gPass = g.Count(r => r.Status == VerifyStatus.Pass);
        var gFail = g.Count(r => r.Status == VerifyStatus.Fail || r.Status == VerifyStatus.Error);
        var gTotal = gPass + gFail;
        return new StigBreakdown
        {
          BenchmarkId = g.Key,
          PassCount = gPass,
          FailCount = gFail,
          TotalCount = gTotal,
          CompliancePercent = gTotal > 0 ? Math.Round((gPass / (double)gTotal) * 100.0, 2) : 0.0
        };
      })
      .OrderBy(s => s.CompliancePercent)
      .ToList();

    return new ExecutiveReportData
    {
      SystemName = systemName,
      BundleId = bundleId,
      GeneratedAt = now,
      GeneratedAtFormatted = now.ToString("yyyy-MM-dd HH:mm"),
      OverallCompliancePercent = compliancePercent,
      TotalControls = results.Count,
      PassCount = passCount,
      FailCount = failCount,
      ErrorCount = errorCount,
      NotApplicableCount = naCount,
      NotReviewedCount = nrCount,
      Severity = severity,
      TrendData = trendData,
      OpenFindings = openFindings,
      PoamAges = poamAges,
      StigBreakdowns = stigBreakdowns,
      Audience = audience
    };
  }

  public static string RenderHtml(ExecutiveReportData data)
  {
    var templateText = LoadTemplate();
    var template = Template.Parse(templateText);
    if (template.HasErrors)
      throw new InvalidOperationException("Template parse error: " + string.Join("; ", template.Messages));

    var scriptObject = new ScriptObject();
    scriptObject.Import(data, renamer: member => ToSnakeCase(member.Name));
    var context = new TemplateContext();
    context.PushGlobal(scriptObject);
    context.MemberRenamer = member => ToSnakeCase(member.Name);

    return template.Render(context);
  }

  public static void WriteReport(ExecutiveReportData data, string outputPath)
  {
    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var html = RenderHtml(data);
    File.WriteAllText(outputPath, html, Encoding.UTF8);
  }

  public static void WriteReportJson(ExecutiveReportData data, string outputPath)
  {
    var dir = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    var json = JsonSerializer.Serialize(data, JsonOptions.IndentedCamelCase);
    File.WriteAllText(outputPath, json, Encoding.UTF8);
  }

  private static SeverityBreakdown BuildSeverityBreakdown(IReadOnlyList<NormalizedVerifyResult> results)
  {
    var breakdown = new SeverityBreakdown();

    foreach (var r in results)
    {
      var sev = NormalizeSeverity(r.Severity);
      var isPass = r.Status == VerifyStatus.Pass;
      var isFail = r.Status == VerifyStatus.Fail || r.Status == VerifyStatus.Error;

      switch (sev)
      {
        case "high":
          breakdown.CatITotal++;
          if (isPass) breakdown.CatIPass++;
          if (isFail) breakdown.CatIFail++;
          break;
        case "medium":
          breakdown.CatIITotal++;
          if (isPass) breakdown.CatIIPass++;
          if (isFail) breakdown.CatIIFail++;
          break;
        case "low":
          breakdown.CatIIITotal++;
          if (isPass) breakdown.CatIIIPass++;
          if (isFail) breakdown.CatIIIFail++;
          break;
      }
    }

    return breakdown;
  }

  private static string NormalizeSeverity(string? severity)
  {
    if (string.IsNullOrWhiteSpace(severity)) return "medium";
    var s = severity!.Trim().ToLowerInvariant();
    return s switch
    {
      "cat i" or "cati" => "high",
      "cat ii" or "catii" => "medium",
      "cat iii" or "catiii" => "low",
      _ => s
    };
  }

  private static int SeverityOrder(string? severity) =>
    NormalizeSeverity(severity) switch
    {
      "high" => 0,
      "medium" => 1,
      "low" => 2,
      _ => 3
    };

  private static string LoadTemplate()
  {
    var assembly = typeof(HtmlReportGenerator).Assembly;
    using var stream = assembly.GetManifestResourceStream(TemplateResourceName)
      ?? throw new InvalidOperationException("Embedded template not found: " + TemplateResourceName);
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  private static string ToSnakeCase(string name)
  {
    if (string.IsNullOrEmpty(name)) return name;
    var sb = new StringBuilder(name.Length + 4);
    for (int i = 0; i < name.Length; i++)
    {
      var c = name[i];
      if (char.IsUpper(c))
      {
        if (i > 0) sb.Append('_');
        sb.Append(char.ToLowerInvariant(c));
      }
      else
      {
        sb.Append(c);
      }
    }
    return sb.ToString();
  }
}
