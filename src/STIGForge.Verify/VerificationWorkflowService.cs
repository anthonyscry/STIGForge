using STIGForge.Core.Abstractions;

namespace STIGForge.Verify;

public sealed class VerificationWorkflowService : IVerificationWorkflowService
{
  private readonly EvaluateStigRunner _evaluateStigRunner;
  private readonly ScapRunner _scapRunner;

  public VerificationWorkflowService(EvaluateStigRunner evaluateStigRunner, ScapRunner scapRunner)
  {
    _evaluateStigRunner = evaluateStigRunner;
    _scapRunner = scapRunner;
  }

  public Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    if (string.IsNullOrWhiteSpace(request.OutputRoot))
      throw new ArgumentException("OutputRoot is required.", nameof(request));

    ct.ThrowIfCancellationRequested();

    Directory.CreateDirectory(request.OutputRoot);

    var workflowStartedAt = DateTimeOffset.Now;
    var diagnostics = new List<string>();
    var toolRuns = new List<VerificationToolRunResult>(2);

    toolRuns.Add(RunEvaluateStigIfConfigured(request, diagnostics));
    toolRuns.Add(RunScapIfConfigured(request, diagnostics));

    var artifacts = BuildArtifactPaths(request.OutputRoot);
    var toolLabel = ResolveConsolidatedToolLabel(request, toolRuns);

    var report = VerifyReportWriter.BuildFromCkls(request.OutputRoot, toolLabel);
    report.StartedAt = ResolveReportStart(workflowStartedAt, toolRuns);
    report.FinishedAt = ResolveReportFinish(report.StartedAt, toolRuns);

    VerifyReportWriter.WriteJson(artifacts.ConsolidatedJsonPath, report);
    VerifyReportWriter.WriteCsv(artifacts.ConsolidatedCsvPath, report.Results);

    var coverage = VerifyReportWriter.BuildCoverageSummary(report.Results);
    VerifyReportWriter.WriteCoverageSummary(artifacts.CoverageSummaryCsvPath, artifacts.CoverageSummaryJsonPath, coverage);

    if (report.Results.Count == 0)
      diagnostics.Add("No CKL results were found under output root.");

    var workflowFinishedAt = DateTimeOffset.Now;

    return Task.FromResult(new VerificationWorkflowResult
    {
      StartedAt = report.StartedAt,
      FinishedAt = workflowFinishedAt > report.FinishedAt ? workflowFinishedAt : report.FinishedAt,
      ConsolidatedJsonPath = artifacts.ConsolidatedJsonPath,
      ConsolidatedCsvPath = artifacts.ConsolidatedCsvPath,
      CoverageSummaryJsonPath = artifacts.CoverageSummaryJsonPath,
      CoverageSummaryCsvPath = artifacts.CoverageSummaryCsvPath,
      ConsolidatedResultCount = report.Results.Count,
      ToolRuns = toolRuns,
      Diagnostics = diagnostics
    });
  }

  private VerificationToolRunResult RunEvaluateStigIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics)
  {
    var now = DateTimeOffset.Now;

    if (!request.EvaluateStig.Enabled)
    {
      return BuildSkippedRun("Evaluate-STIG", now, "Evaluate-STIG execution not enabled.");
    }

    if (string.IsNullOrWhiteSpace(request.EvaluateStig.ToolRoot))
    {
      diagnostics.Add("Evaluate-STIG enabled but ToolRoot was empty.");
      return BuildSkippedRun("Evaluate-STIG", now, "Missing tool root.");
    }

    try
    {
      var runResult = _evaluateStigRunner.Run(
        request.EvaluateStig.ToolRoot,
        request.EvaluateStig.Arguments,
        string.IsNullOrWhiteSpace(request.EvaluateStig.WorkingDirectory) ? null : request.EvaluateStig.WorkingDirectory);

      if (runResult.ExitCode != 0)
        diagnostics.Add($"Evaluate-STIG exited with code {runResult.ExitCode}.");

      return new VerificationToolRunResult
      {
        Tool = "Evaluate-STIG",
        Executed = true,
        ExitCode = runResult.ExitCode,
        StartedAt = runResult.StartedAt,
        FinishedAt = runResult.FinishedAt,
        Output = runResult.Output,
        Error = runResult.Error
      };
    }
    catch (Exception ex)
    {
      diagnostics.Add($"Evaluate-STIG execution failed: {ex.Message}");
      return new VerificationToolRunResult
      {
        Tool = "Evaluate-STIG",
        Executed = false,
        ExitCode = -1,
        StartedAt = now,
        FinishedAt = DateTimeOffset.Now,
        Output = string.Empty,
        Error = ex.Message
      };
    }
  }

  private VerificationToolRunResult RunScapIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics)
  {
    var toolName = string.IsNullOrWhiteSpace(request.Scap.ToolLabel) ? "SCAP" : request.Scap.ToolLabel.Trim();
    var now = DateTimeOffset.Now;

    if (!request.Scap.Enabled)
    {
      return BuildSkippedRun(toolName, now, "SCAP execution not enabled.");
    }

    if (string.IsNullOrWhiteSpace(request.Scap.CommandPath))
    {
      diagnostics.Add("SCAP enabled but CommandPath was empty.");
      return BuildSkippedRun(toolName, now, "Missing command path.");
    }

    if (string.IsNullOrWhiteSpace(request.Scap.Arguments))
    {
      diagnostics.Add($"{toolName} enabled but SCAP arguments were empty. Configure explicit SCC arguments (benchmark/input and output path).");
      return BuildSkippedRun(toolName, now, "Missing SCAP arguments.");
    }

    var artifactsBeforeRun = SnapshotScapArtifactPaths(request.OutputRoot);

    try
    {
      var runResult = _scapRunner.Run(
        request.Scap.CommandPath,
        request.Scap.Arguments,
        string.IsNullOrWhiteSpace(request.Scap.WorkingDirectory) ? null : request.Scap.WorkingDirectory);

      if (runResult.ExitCode != 0)
        diagnostics.Add($"{toolName} exited with code {runResult.ExitCode}.");
      else
      {
        var artifactsAfterRun = SnapshotScapArtifactPaths(request.OutputRoot);
        var producedArtifacts = artifactsAfterRun.Except(artifactsBeforeRun, StringComparer.OrdinalIgnoreCase).Any();
        if (!producedArtifacts)
          diagnostics.Add($"{toolName} executed with exit code 0 but produced no SCAP artifacts.");
      }

      return new VerificationToolRunResult
      {
        Tool = toolName,
        Executed = true,
        ExitCode = runResult.ExitCode,
        StartedAt = runResult.StartedAt,
        FinishedAt = runResult.FinishedAt,
        Output = runResult.Output,
        Error = runResult.Error
      };
    }
    catch (Exception ex)
    {
      diagnostics.Add($"{toolName} execution failed: {ex.Message}");
      return new VerificationToolRunResult
      {
        Tool = toolName,
        Executed = false,
        ExitCode = -1,
        StartedAt = now,
        FinishedAt = DateTimeOffset.Now,
        Output = string.Empty,
        Error = ex.Message
      };
    }
  }

  private static HashSet<string> SnapshotScapArtifactPaths(string outputRoot)
  {
    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
      return paths;

    try
    {
      foreach (var path in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories))
      {
        if (IsScapArtifactPath(path))
          paths.Add(Path.GetFullPath(path));
      }
    }
    catch (UnauthorizedAccessException)
    {
    }
    catch (IOException)
    {
    }

    return paths;
  }

  private static bool IsScapArtifactPath(string path)
  {
    var extension = Path.GetExtension(path);
    if (string.IsNullOrWhiteSpace(extension))
      return false;

    if (string.Equals(extension, ".arf", StringComparison.OrdinalIgnoreCase)
      || string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase)
      || string.Equals(extension, ".xccdf", StringComparison.OrdinalIgnoreCase))
      return true;

    if (!string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
      return false;

    var fileName = Path.GetFileName(path);
    return fileName.Contains("xccdf", StringComparison.OrdinalIgnoreCase)
      || fileName.Contains("scap", StringComparison.OrdinalIgnoreCase)
      || fileName.Contains("arf", StringComparison.OrdinalIgnoreCase)
      || fileName.Contains("results", StringComparison.OrdinalIgnoreCase);
  }

  private static VerificationToolRunResult BuildSkippedRun(string tool, DateTimeOffset at, string message)
  {
    return new VerificationToolRunResult
    {
      Tool = tool,
      Executed = false,
      ExitCode = 0,
      StartedAt = at,
      FinishedAt = at,
      Output = string.Empty,
      Error = message
    };
  }

  private static VerificationWorkflowArtifacts BuildArtifactPaths(string outputRoot)
  {
    return new VerificationWorkflowArtifacts
    {
      ConsolidatedJsonPath = BuildConsolidatedJsonPath(outputRoot),
      ConsolidatedCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedCsvFile),
      CoverageSummaryJsonPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryJsonFile),
      CoverageSummaryCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryCsvFile)
    };
  }

  public static string BuildConsolidatedJsonPath(string outputRoot)
  {
    return Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedJsonFile);
  }

  private static string ResolveConsolidatedToolLabel(VerificationWorkflowRequest request, IReadOnlyList<VerificationToolRunResult> runs)
  {
    if (!string.IsNullOrWhiteSpace(request.ConsolidatedToolLabel))
      return request.ConsolidatedToolLabel.Trim();

    var executed = runs.Where(r => r.Executed).Select(r => r.Tool).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    if (executed.Count == 1)
      return executed[0];

    if (executed.Count > 1)
      return "Verification";

    return "Verification";
  }

  private static DateTimeOffset ResolveReportStart(DateTimeOffset fallback, IReadOnlyList<VerificationToolRunResult> runs)
  {
    var executedStarts = runs.Where(r => r.Executed).Select(r => r.StartedAt).ToList();
    return executedStarts.Count == 0 ? fallback : executedStarts.Min();
  }

  private static DateTimeOffset ResolveReportFinish(DateTimeOffset fallback, IReadOnlyList<VerificationToolRunResult> runs)
  {
    var executedFinishes = runs.Where(r => r.Executed).Select(r => r.FinishedAt).ToList();
    return executedFinishes.Count == 0 ? fallback : executedFinishes.Max();
  }
}
