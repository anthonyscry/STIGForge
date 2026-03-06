using System.Text.Json;
using Microsoft.Extensions.Logging;
using STIGForge.Evidence;

namespace STIGForge.Apply.Steps;

internal sealed class StepEvidenceWriter
{
  private readonly ILogger<ApplyRunner> _logger;
  private readonly EvidenceCollector? _evidenceCollector;

  public StepEvidenceWriter(ILogger<ApplyRunner> logger, EvidenceCollector? evidenceCollector)
  {
    _logger = logger;
    _evidenceCollector = evidenceCollector;
  }

  public ApplyStepOutcome Write(ApplyStepOutcome outcome, string bundleRoot, string runId, IReadOnlyDictionary<string, string> priorStepSha256)
  {
    if (_evidenceCollector == null)
      return outcome;

    try
    {
      var artifactPath = !string.IsNullOrWhiteSpace(outcome.StdOutPath) && File.Exists(outcome.StdOutPath)
        ? outcome.StdOutPath
        : null;

      string? sha256 = null;
      if (artifactPath != null)
        sha256 = ComputeSha256(artifactPath);

      string? continuityMarker = null;
      string? supersedesEvidenceId = null;
      if (sha256 != null && priorStepSha256.TryGetValue(outcome.StepName, out var priorSha))
      {
        continuityMarker = string.Equals(sha256, priorSha, StringComparison.OrdinalIgnoreCase)
          ? "retained"
          : "superseded";
      }

      var result = _evidenceCollector.WriteEvidence(new EvidenceWriteRequest
      {
        BundleRoot = bundleRoot,
        Title = "Apply step: " + outcome.StepName,
        Type = EvidenceArtifactType.File,
        Source = "ApplyRunner",
        SourceFilePath = artifactPath,
        ContentText = artifactPath == null ? $"Step {outcome.StepName} completed with exit code {outcome.ExitCode}" : null,
        FileExtension = artifactPath == null ? ".txt" : null,
        RunId = runId,
        StepName = outcome.StepName,
        SupersedesEvidenceId = supersedesEvidenceId
      });

      outcome.EvidenceMetadataPath = result.MetadataPath;
      outcome.ArtifactSha256 = sha256 ?? result.Sha256;
      outcome.ContinuityMarker = continuityMarker;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to write step evidence for {StepName} (non-blocking)", outcome.StepName);
    }

    return outcome;
  }

  public static IReadOnlyDictionary<string, string> LoadPriorRunStepSha256(string bundleRoot, string? priorRunId)
  {
    if (string.IsNullOrWhiteSpace(priorRunId))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var logPath = Path.Combine(bundleRoot, "Apply", "apply_run.json");
    if (!File.Exists(logPath))
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    try
    {
      using var stream = File.OpenRead(logPath);
      using var doc = JsonDocument.Parse(stream);

      if (!doc.RootElement.TryGetProperty("runId", out var storedRunId))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      if (!string.Equals(storedRunId.GetString(), priorRunId, StringComparison.OrdinalIgnoreCase))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      if (!doc.RootElement.TryGetProperty("steps", out var stepsEl))
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      foreach (var step in stepsEl.EnumerateArray())
      {
        if (step.TryGetProperty("StepName", out var stepName)
          && step.TryGetProperty("ArtifactSha256", out var sha)
          && sha.ValueKind == JsonValueKind.String
          && !string.IsNullOrWhiteSpace(sha.GetString()))
        {
          result[stepName.GetString()!] = sha.GetString()!;
        }
      }

      return result;
    }
    catch (Exception)
    {
      return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
  }

  private static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    var hash = System.Security.Cryptography.SHA256.HashData(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }
}
