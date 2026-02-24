using System.Text.Json;
using STIGForge.Content.Import;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Infrastructure.Workflow;

namespace STIGForge.Verify;

public sealed class LocalWorkflowService : ILocalWorkflowService
{
  private readonly ImportInboxScanner _importInboxScanner;
  private readonly LocalSetupValidator _localSetupValidator;
  private readonly IVerificationWorkflowService _verificationWorkflowService;
  private readonly ScannerEvidenceMapper _scannerEvidenceMapper;

  public LocalWorkflowService(
    ImportInboxScanner importInboxScanner,
    LocalSetupValidator localSetupValidator,
    IVerificationWorkflowService verificationWorkflowService,
    ScannerEvidenceMapper scannerEvidenceMapper)
  {
    _importInboxScanner = importInboxScanner;
    _localSetupValidator = localSetupValidator;
    _verificationWorkflowService = verificationWorkflowService;
    _scannerEvidenceMapper = scannerEvidenceMapper;
  }

  public async Task<LocalWorkflowResult> RunAsync(LocalWorkflowRequest request, CancellationToken ct)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    if (string.IsNullOrWhiteSpace(request.OutputRoot))
      throw new ArgumentException("OutputRoot is required.", nameof(request));

    if (string.IsNullOrWhiteSpace(request.ImportRoot))
      throw new ArgumentException("ImportRoot is required.", nameof(request));

    if (string.IsNullOrWhiteSpace(request.ToolRoot))
      throw new ArgumentException("ToolRoot is required.", nameof(request));

    ct.ThrowIfCancellationRequested();

    Directory.CreateDirectory(request.OutputRoot);

    var diagnostics = new List<string>();
    var resolvedToolRoot = _localSetupValidator.ValidateRequiredTools(request.ToolRoot);

    var importResult = await _importInboxScanner.ScanWithCanonicalChecklistAsync(request.ImportRoot, ct).ConfigureAwait(false);
    diagnostics.AddRange(importResult.Warnings);
    var canonicalChecklist = importResult.CanonicalChecklist.ToList();

    if (canonicalChecklist.Count == 0)
      throw new InvalidOperationException("Import stage did not produce canonical checklist items.");

    var verificationResult = await _verificationWorkflowService.RunAsync(new VerificationWorkflowRequest
    {
      OutputRoot = request.OutputRoot,
      ConsolidatedToolLabel = "Evaluate-STIG",
      EvaluateStig = new EvaluateStigWorkflowOptions
      {
        Enabled = true,
        ToolRoot = resolvedToolRoot
      }
    }, ct).ConfigureAwait(false);

    EnsureScanStageSucceeded(verificationResult);

    diagnostics.AddRange(verificationResult.Diagnostics);

    var findings = LoadScannerFindings(verificationResult.ConsolidatedJsonPath);
    var mapResult = _scannerEvidenceMapper.Map(canonicalChecklist, findings);
    diagnostics.AddRange(mapResult.Diagnostics);

    var missionPath = Path.Combine(request.OutputRoot, "mission.json");

    var mission = new LocalWorkflowMission
    {
      CanonicalChecklist = canonicalChecklist,
      ScannerEvidence = mapResult.ScannerEvidence.ToList(),
      Unmapped = mapResult.Unmapped.ToList(),
      Diagnostics = diagnostics,
      StageMetadata = new LocalWorkflowStageMetadata
      {
        MissionJsonPath = missionPath,
        ConsolidatedJsonPath = verificationResult.ConsolidatedJsonPath,
        ConsolidatedCsvPath = verificationResult.ConsolidatedCsvPath,
        CoverageSummaryJsonPath = verificationResult.CoverageSummaryJsonPath,
        CoverageSummaryCsvPath = verificationResult.CoverageSummaryCsvPath,
        StartedAt = verificationResult.StartedAt,
        FinishedAt = verificationResult.FinishedAt
      }
    };

    await WriteMissionAsync(missionPath, mission, ct).ConfigureAwait(false);

    return new LocalWorkflowResult
    {
      Mission = mission,
      Diagnostics = diagnostics
    };
  }

  private static void EnsureScanStageSucceeded(VerificationWorkflowResult verificationResult)
  {
    var executionFailure = verificationResult.ToolRuns
      .FirstOrDefault(run =>
        string.Equals(run.Tool, "Evaluate-STIG", StringComparison.OrdinalIgnoreCase)
        && !run.Executed
        && (run.ExitCode < 0 || !string.IsNullOrWhiteSpace(run.Error)));
    if (executionFailure is not null)
      throw new InvalidOperationException(
        "Scan stage failed: "
        + executionFailure.Tool
        + " did not execute successfully. "
        + (string.IsNullOrWhiteSpace(executionFailure.Error)
          ? "Exit code " + executionFailure.ExitCode + "."
          : executionFailure.Error));

    var failedToolRun = verificationResult.ToolRuns
      .FirstOrDefault(run => run.Executed && run.ExitCode != 0);
    if (failedToolRun is null)
      return;

    throw new InvalidOperationException(
      "Scan stage failed: "
      + failedToolRun.Tool
      + " exited with code "
      + failedToolRun.ExitCode
      + ".");
  }

  private static IReadOnlyList<ControlResult> LoadScannerFindings(string? consolidatedJsonPath)
  {
    var path = consolidatedJsonPath?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      throw new InvalidOperationException("Scan stage did not produce readable consolidated-results.json at: " + path);

    try
    {
      var report = VerifyReportReader.LoadFromJson(path);
      return report.Results;
    }
    catch (Exception ex)
    {
      throw new InvalidOperationException("Failed to read consolidated scanner report: " + ex.Message, ex);
    }
  }

  private static async Task WriteMissionAsync(string missionPath, LocalWorkflowMission mission, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(mission, new JsonSerializerOptions
    {
      WriteIndented = true
    });

    await File.WriteAllTextAsync(missionPath, json, ct).ConfigureAwait(false);
  }
}
