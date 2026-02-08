namespace STIGForge.Verify;

public sealed class VerificationCoverageInput
{
  public string ToolLabel { get; set; } = string.Empty;

  public string ReportPath { get; set; } = string.Empty;
}

public sealed class VerificationArtifactAggregationResult
{
  public string ReportsRoot { get; set; } = string.Empty;

  public string CoverageByToolCsvPath { get; set; } = string.Empty;

  public string CoverageByToolJsonPath { get; set; } = string.Empty;

  public string ControlSourcesCsvPath { get; set; } = string.Empty;

  public string CoverageOverlapCsvPath { get; set; } = string.Empty;

  public string CoverageOverlapJsonPath { get; set; } = string.Empty;

  public int InputCount { get; set; }

  public int TotalResultCount { get; set; }
}

public sealed class VerificationArtifactAggregationService
{
  public VerificationArtifactAggregationResult WriteCoverageArtifacts(string reportsRoot, IReadOnlyList<VerificationCoverageInput> inputs)
  {
    if (string.IsNullOrWhiteSpace(reportsRoot))
      throw new ArgumentException("Reports root is required.", nameof(reportsRoot));

    Directory.CreateDirectory(reportsRoot);

    var allResults = new List<ControlResult>();
    var inputList = inputs ?? Array.Empty<VerificationCoverageInput>();

    foreach (var input in inputList)
    {
      if (input == null || string.IsNullOrWhiteSpace(input.ReportPath))
        continue;

      var resolved = ResolveReportPath(input.ReportPath);
      var report = VerifyReportReader.LoadFromJson(resolved);

      if (!string.IsNullOrWhiteSpace(input.ToolLabel))
        report.Tool = input.ToolLabel;

      foreach (var row in report.Results)
      {
        if (string.IsNullOrWhiteSpace(row.Tool))
          row.Tool = report.Tool;
      }

      allResults.AddRange(report.Results);
    }

    var coverageByToolCsv = Path.Combine(reportsRoot, "coverage_by_tool.csv");
    var coverageByToolJson = Path.Combine(reportsRoot, "coverage_by_tool.json");
    var controlSourcesCsv = Path.Combine(reportsRoot, "control_sources.csv");
    var overlapCsv = Path.Combine(reportsRoot, "coverage_overlap.csv");
    var overlapJson = Path.Combine(reportsRoot, "coverage_overlap.json");

    var coverage = VerifyReportWriter.BuildCoverageSummary(allResults);
    VerifyReportWriter.WriteCoverageSummary(coverageByToolCsv, coverageByToolJson, coverage);

    var maps = VerifyReportWriter.BuildControlSourceMap(allResults);
    VerifyReportWriter.WriteControlSourceMap(controlSourcesCsv, maps);

    var overlaps = VerifyReportWriter.BuildOverlapSummary(allResults);
    VerifyReportWriter.WriteOverlapSummary(overlapCsv, overlapJson, overlaps);

    return new VerificationArtifactAggregationResult
    {
      ReportsRoot = reportsRoot,
      CoverageByToolCsvPath = coverageByToolCsv,
      CoverageByToolJsonPath = coverageByToolJson,
      ControlSourcesCsvPath = controlSourcesCsv,
      CoverageOverlapCsvPath = overlapCsv,
      CoverageOverlapJsonPath = overlapJson,
      InputCount = inputList.Count,
      TotalResultCount = allResults.Count
    };
  }

  private static string ResolveReportPath(string path)
  {
    if (File.Exists(path))
      return path;

    if (Directory.Exists(path))
    {
      var candidate = Path.Combine(path, "consolidated-results.json");
      if (File.Exists(candidate))
        return candidate;
    }

    throw new FileNotFoundException("Report not found: " + path);
  }
}
