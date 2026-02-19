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

  public async Task<VerificationWorkflowResult> RunAsync(VerificationWorkflowRequest request, CancellationToken ct)
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

    toolRuns.Add(await RunEvaluateStigIfConfigured(request, diagnostics, ct));
    toolRuns.Add(await RunScapIfConfigured(request, diagnostics, ct));

    var artifacts = BuildArtifactPaths(request.OutputRoot);
    var toolLabel = ResolveConsolidatedToolLabel(request, toolRuns);

    // Discover all result files (CKL + XCCDF XML)
    var cklFiles = Directory.GetFiles(request.OutputRoot, "*.ckl", SearchOption.AllDirectories);
    var xmlFiles = Directory.GetFiles(request.OutputRoot, "*.xml", SearchOption.AllDirectories);
    var allFiles = cklFiles.Concat(xmlFiles)
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Parse through orchestrator adapter chain
    var orchestrator = new VerifyOrchestrator();
    var consolidated = orchestrator.ParseAndMergeResults(allFiles);

    // Bridge NormalizedVerifyResult -> ControlResult for downstream VerifyReport pipeline
    var results = consolidated.Results.Select(ToControlResult).ToList();

    // Append orchestrator diagnostics
    diagnostics.AddRange(consolidated.DiagnosticMessages);

    var report = new VerifyReport
    {
      Tool = toolLabel,
      ToolVersion = "unknown",
      StartedAt = ResolveReportStart(workflowStartedAt, toolRuns),
      FinishedAt = ResolveReportFinish(workflowStartedAt, toolRuns),
      OutputRoot = request.OutputRoot,
      Results = results
    };

    VerifyReportWriter.WriteJson(artifacts.ConsolidatedJsonPath, report);
    VerifyReportWriter.WriteCsv(artifacts.ConsolidatedCsvPath, report.Results);

    var coverage = VerifyReportWriter.BuildCoverageSummary(report.Results);
    VerifyReportWriter.WriteCoverageSummary(artifacts.CoverageSummaryCsvPath, artifacts.CoverageSummaryJsonPath, coverage);

    if (report.Results.Count == 0)
      diagnostics.Add(BuildNoResultDiagnostic(request.OutputRoot, toolRuns));

    var workflowFinishedAt = DateTimeOffset.Now;

    return new VerificationWorkflowResult
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
    };
  }

  private async Task<VerificationToolRunResult> RunEvaluateStigIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics, CancellationToken ct)
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
      var runResult = await _evaluateStigRunner.RunAsync(
        request.EvaluateStig.ToolRoot,
        request.EvaluateStig.Arguments,
        string.IsNullOrWhiteSpace(request.EvaluateStig.WorkingDirectory) ? null : request.EvaluateStig.WorkingDirectory,
        ct,
        TimeSpan.FromSeconds(request.EvaluateStig.TimeoutSeconds));

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

  private async Task<VerificationToolRunResult> RunScapIfConfigured(VerificationWorkflowRequest request, List<string> diagnostics, CancellationToken ct)
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

    try
    {
      var sanitizedArgs = SanitizeScapArgs(request.Scap.Arguments, diagnostics);

      var runResult = await _scapRunner.RunAsync(
        request.Scap.CommandPath,
        sanitizedArgs,
        string.IsNullOrWhiteSpace(request.Scap.WorkingDirectory) ? null : request.Scap.WorkingDirectory,
        ct,
        TimeSpan.FromSeconds(request.Scap.TimeoutSeconds));

      if (runResult.ExitCode != 0)
        diagnostics.Add($"{toolName} exited with code {runResult.ExitCode}.");

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

  private static string SanitizeScapArgs(string args, List<string> diagnostics)
  {
    if (string.IsNullOrWhiteSpace(args)) return args;
    var tokens = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    for (int i = 0; i < tokens.Count; i++)
    {
      if (string.Equals(tokens[i], "-f", StringComparison.OrdinalIgnoreCase))
      {
        // -f requires a following filename argument
        if (i + 1 >= tokens.Count || tokens[i + 1].StartsWith("-"))
        {
          tokens.RemoveAt(i);
          diagnostics.Add("SCAP argument '-f' was missing a filename; removed invalid switch.");
          i--;
        }
      }
    }
    return string.Join(" ", tokens);
  }

  private static ControlResult ToControlResult(NormalizedVerifyResult r)
  {
    return new ControlResult
    {
      VulnId = r.VulnId,
      RuleId = r.RuleId,
      Title = r.Title,
      Severity = r.Severity,
      Status = MapStatusToString(r.Status),
      FindingDetails = r.FindingDetails,
      Comments = r.Comments,
      Tool = r.Tool,
      SourceFile = r.SourceFile,
      VerifiedAt = r.VerifiedAt
    };
  }

  private static string MapStatusToString(VerifyStatus status) => status switch
  {
    VerifyStatus.Pass => "NotAFinding",
    VerifyStatus.Fail => "Open",
    VerifyStatus.NotApplicable => "Not_Applicable",
    VerifyStatus.NotReviewed => "Not_Reviewed",
    VerifyStatus.Informational => "Informational",
    VerifyStatus.Error => "Error",
    _ => "Not_Reviewed"
  };

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
      ConsolidatedJsonPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedJsonFile),
      ConsolidatedCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.ConsolidatedCsvFile),
      CoverageSummaryJsonPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryJsonFile),
      CoverageSummaryCsvPath = Path.Combine(outputRoot, VerificationWorkflowDefaults.CoverageSummaryCsvFile)
    };
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

  private static string BuildNoResultDiagnostic(string outputRoot, IReadOnlyList<VerificationToolRunResult> runs)
  {
    var toolNames = runs
      .Where(r => r.Executed)
      .Select(r => r.Tool)
      .Where(tool => !string.IsNullOrWhiteSpace(tool))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var executedTools = toolNames.Count == 0 ? "none" : string.Join(", ", toolNames);
    return $"No CKL results were found under output root '{outputRoot}' from executed tools: {executedTools}.";
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
