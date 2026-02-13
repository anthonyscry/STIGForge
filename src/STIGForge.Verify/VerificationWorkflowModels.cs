using BundlePaths = STIGForge.Core.Constants.BundlePaths;
using PackTypes = STIGForge.Core.Constants.PackTypes;

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
  public const string ConsolidatedJsonFile = BundlePaths.ConsolidatedResultsFileName;
  public const string ConsolidatedCsvFile = "consolidated-results.csv";
  public const string CoverageSummaryJsonFile = "coverage_summary.json";
  public const string CoverageSummaryCsvFile = "coverage_summary.csv";
  public const string DscScanOutputDir = "DSC-Scan";
  public const string DscScanToolLabel = "PowerSTIG-DSC";
  public const string EvaluateStigOutputDir = "Evaluate-STIG";
  public const string ScapOutputDir = PackTypes.Scap;
  public const string ScapFallbackOutputDir = "SCAP-Fallback";
}
