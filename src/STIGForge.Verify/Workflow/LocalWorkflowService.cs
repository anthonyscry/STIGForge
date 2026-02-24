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

    diagnostics.AddRange(verificationResult.Diagnostics);

    var findings = LoadScannerFindings(verificationResult.ConsolidatedJsonPath, diagnostics);
    var mapResult = _scannerEvidenceMapper.Map(canonicalChecklist, findings);
    diagnostics.AddRange(mapResult.Diagnostics);

    var mission = new LocalWorkflowMission
    {
      CanonicalChecklist = canonicalChecklist,
      ScannerEvidence = mapResult.ScannerEvidence.ToList(),
      Unmapped = mapResult.Unmapped.ToList()
    };

    var missionPath = Path.Combine(request.OutputRoot, "mission.json");
    await WriteMissionAsync(missionPath, mission, ct).ConfigureAwait(false);

    return new LocalWorkflowResult
    {
      Mission = mission,
      Diagnostics = diagnostics
    };
  }

  private static IReadOnlyList<ControlResult> LoadScannerFindings(string? consolidatedJsonPath, ICollection<string> diagnostics)
  {
    var path = consolidatedJsonPath?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      diagnostics.Add("Scan stage did not produce consolidated-results.json; continuing with empty scanner findings.");
      return Array.Empty<ControlResult>();
    }

    try
    {
      var report = VerifyReportReader.LoadFromJson(path);
      return report.Results;
    }
    catch (Exception ex)
    {
      diagnostics.Add("Failed to read consolidated scanner report: " + ex.Message);
      return Array.Empty<ControlResult>();
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
