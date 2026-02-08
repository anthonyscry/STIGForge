namespace STIGForge.Verify;

internal sealed class VerificationWorkflowArtifacts
{
  public string ConsolidatedJsonPath { get; set; } = string.Empty;

  public string ConsolidatedCsvPath { get; set; } = string.Empty;

  public string CoverageSummaryJsonPath { get; set; } = string.Empty;

  public string CoverageSummaryCsvPath { get; set; } = string.Empty;
}

internal static class VerificationWorkflowDefaults
{
  public const string ConsolidatedJsonFile = "consolidated-results.json";
  public const string ConsolidatedCsvFile = "consolidated-results.csv";
  public const string CoverageSummaryJsonFile = "coverage_summary.json";
  public const string CoverageSummaryCsvFile = "coverage_summary.csv";
}
